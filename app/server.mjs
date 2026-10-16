// Concilium — local server.
// Wraps the Claude Agent SDK (Claude Code as a library) around a person's health folder.
import express from 'express';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath } from 'node:url';
import { spawn, execFile } from 'node:child_process';
import { query, listSessions, getSessionMessages } from '@anthropic-ai/claude-agent-sdk';

const ROOT = path.dirname(fileURLToPath(import.meta.url));
// The folder template people can also use without the app; its skills and principles are the single source.
const TEMPLATE = process.env.CONCILIUM_TEMPLATE || path.join(ROOT, '..', 'concilium-markdown');
// Portable by default: data lives next to the program. Run from the source checkout, it goes to the
// user's app-data folder instead, so nobody's data can end up in the repository.
const appData = () => process.platform === 'win32' ? process.env.APPDATA
  : process.platform === 'darwin' ? path.join(os.homedir(), 'Library', 'Application Support')
  : process.env.XDG_CONFIG_HOME || path.join(os.homedir(), '.config');
const DATA = process.env.CONCILIUM_DATA
  || (fs.existsSync(path.join(ROOT, '..', '.git')) ? path.join(appData(), 'Concilium') : path.join(ROOT, 'data'));
const PORT = Number(process.env.PORT || 4747);
const PRINCIPLES = fs.readFileSync(path.join(TEMPLATE, 'PRINCIPLES.md'), 'utf8') + `
## In this app

The person may be elderly and not technical at all. They do not know what files, memory, skills, agents or markdown are, and they must never need to. Never mention files, folders, skills, tools, sessions or \`AGENTS.md\`: say "I noted that", not "I updated PATIENT FACTS.md". Short, warm, plain words.
`;

fs.mkdirSync(DATA, { recursive: true });

// The SDK loads skills from a plugin; build it from the template's skills on every start.
const PLUGIN = path.join(DATA, 'plugin');
fs.rmSync(PLUGIN, { recursive: true, force: true });
fs.mkdirSync(path.join(PLUGIN, '.claude-plugin'), { recursive: true });
fs.writeFileSync(path.join(PLUGIN, '.claude-plugin', 'plugin.json'), JSON.stringify({ name: 'concilium', description: 'Concilium health skills' }));
// Plain recursive copy: fs.cpSync crashes Electron's process on non-ASCII paths.
const copyDir = (from, to) => {
  fs.mkdirSync(to, { recursive: true });
  for (const e of fs.readdirSync(from, { withFileTypes: true })) {
    if (e.isDirectory()) copyDir(path.join(from, e.name), path.join(to, e.name));
    else fs.copyFileSync(path.join(from, e.name), path.join(to, e.name));
  }
};
copyDir(path.join(TEMPLATE, '.claude', 'skills'), path.join(PLUGIN, 'skills'));
const CONFIG = path.join(DATA, 'config.json');
const STATE = path.join(DATA, 'state.json');
const readJson = (f, d) => { try { return JSON.parse(fs.readFileSync(f, 'utf8')); } catch { return d; } };
const writeJson = (f, v) => fs.writeFileSync(f, JSON.stringify(v, null, 2));

const config = () => readJson(CONFIG, { people: [] });
const person = (id) => {
  const p = config().people.find((x) => x.id === id);
  if (!p) throw Object.assign(new Error('unknown person'), { status: 404 });
  return p;
};
const inside = (dir, f) => {
  const rel = path.relative(path.resolve(dir), path.resolve(dir, f));
  return rel && !rel.startsWith('..') && !path.isAbsolute(rel);
};

// ---------- agent ----------

const SKILLS = fs.readdirSync(path.join(PLUGIN, 'skills'));
const READ_TOOLS = new Set(['Read', 'Glob', 'Grep', 'WebSearch', 'WebFetch', 'Skill', 'Task', 'Agent', 'TodoWrite', 'ToolSearch']);
const WRITE_TOOLS = new Set(['Write', 'Edit', 'MultiEdit']);

// When started from inside the Claude desktop app, its host-session variables leak in; drop them.
const cleanEnv = Object.fromEntries(Object.entries(process.env).filter(([k]) =>
  !(process.env.CLAUDE_CODE_DESKTOP_APP_VERSION && (k.startsWith('CLAUDE_CODE_') || k === 'ANTHROPIC_BASE_URL' || k === 'CLAUDECODE'))));

function agentOptions(p, extra = {}) {
  return {
    cwd: p.path,
    ...(CLI !== 'claude' && { pathToClaudeCodeExecutable: CLI }),
    env: cleanEnv,
    plugins: [{ type: 'local', path: PLUGIN }],
    systemPrompt: { type: 'preset', preset: 'claude_code', append: PRINCIPLES },
    permissionMode: 'default',
    disallowedTools: ['Bash', 'PowerShell', 'NotebookEdit', 'KillShell'],
    // Reads and web research are free; writes only inside the person's folder.
    canUseTool: async (tool, input) => {
      if (READ_TOOLS.has(tool) || tool.startsWith('mcp__')) return { behavior: 'allow', updatedInput: input };
      if (WRITE_TOOLS.has(tool)) {
        const f = input.file_path || '';
        if (inside(p.path, f)) return { behavior: 'allow', updatedInput: input };
        return { behavior: 'deny', message: `Writing outside the health folder is not allowed: ${f}` };
      }
      return { behavior: 'deny', message: `${tool} is not available in this app.` };
    },
    ...extra,
  };
}

const busy = new Map(); // personId -> number of running chats

// Plain text of a stored conversation (for the UI and for the memory pass).
function textOf(content) {
  if (typeof content === 'string') return content;
  if (!Array.isArray(content)) return '';
  return content.filter((b) => b.type === 'text').map((b) => b.text).join('\n');
}
function prettyCommand(t) {
  const m = t.match(/<command-name>\/?(?:concilium:|health-agent:)?([\w-]+)<\/command-name>/);
  if (m) return { skill: { 'update-memory': 'sleep', hypotheses: 'research' }[m[1]] || m[1] };
  if (/^<(local-command|command-message|command-args)/.test(t.trim())) return null;
  return { text: t };
}
async function conversation(p, sessionId) {
  const msgs = await getSessionMessages(sessionId, { dir: p.path });
  const out = [];
  for (const m of msgs) {
    const c = m.message?.content;
    if (m.type === 'user') {
      if (Array.isArray(c) && c.some((b) => b.type === 'tool_result')) continue;
      const t = textOf(c).trim();
      if (!t || t.startsWith('Base directory for this skill')) continue;
      const pc = prettyCommand(t);
      if (pc) out.push({ role: 'user', ...pc });
    } else if (m.type === 'assistant') {
      const t = textOf(c).trim();
      if (!t) continue;
      const last = out[out.length - 1];
      if (last && last.role === 'assistant') last.text += '\n\n' + t;
      else out.push({ role: 'assistant', text: t });
    }
  }
  return out;
}

// ---------- automatic memory pass ("dream") ----------
// After a conversation ends, a separate headless session runs the sleep skill on it.

const dreaming = new Map(); // personId -> Promise
const dreamLog = new Map(); // personId -> {status, at, summary}

function undreamed(p, sessions) {
  const st = readJson(STATE, {});
  const seen = st[p.id]?.dreamed || {};
  return sessions.filter((s) => !s.tag?.startsWith('dream') && (seen[s.sessionId] || 0) < s.lastModified);
}

async function dream(p, sessionIds) {
  if (dreaming.has(p.id)) return dreaming.get(p.id);
  const run = (async () => {
    dreamLog.set(p.id, { status: 'running', at: Date.now() });
    try {
      const parts = [];
      const stamps = {};
      for (const id of sessionIds) {
        const conv = await conversation(p, id);
        if (!conv.length) continue;
        const info = (await listSessions({ dir: p.path })).find((s) => s.sessionId === id);
        stamps[id] = info?.lastModified || Date.now();
        parts.push(`<conversation id="${id}" date="${new Date(stamps[id]).toISOString().slice(0, 10)}">\n` +
          conv.map((m) => `${m.role === 'user' ? 'PERSON' : 'AGENT'}: ${m.text ?? `[asked for: ${m.skill}]`}`).join('\n\n') +
          '\n</conversation>');
      }
      let summary = '';
      if (parts.length) {
        const q = query({
          prompt: `/concilium:sleep\n\nConversations to consolidate:\n\n${parts.join('\n\n')}`,
          options: agentOptions(p, { persistSession: false }),
        });
        for await (const m of q) if (m.type === 'result') summary = m.result || '';
      }
      const st = readJson(STATE, {});
      st[p.id] ??= { dreamed: {} };
      for (const id of sessionIds) st[p.id].dreamed[id] = stamps[id] || Date.now();
      writeJson(STATE, st);
      dreamLog.set(p.id, { status: 'done', at: Date.now(), summary, count: parts.length });
    } catch (e) {
      dreamLog.set(p.id, { status: 'error', at: Date.now(), summary: String(e.message || e) });
    } finally {
      dreaming.delete(p.id);
    }
  })();
  dreaming.set(p.id, run);
  return run;
}

// ---------- graph ----------

const KINDS = ['MEMORY', 'PATIENT FACTS', 'HYPOTHESES', 'QUESTIONS', 'TODO', 'HISTORY', 'IDENTITY'];
function kindOf(rel) {
  const base = path.basename(rel, '.md');
  if (['topics', 'branches'].includes(rel.split(/[\\/]/)[0])) return 'branch';
  for (const k of KINDS) if (base === k || base.endsWith(' ' + k) || base.endsWith('_' + k.replace(' ', '_'))) return k.toLowerCase();
  if (base === 'AGENTS' || base === 'CLAUDE' || base === 'README') return 'system';
  if (!rel.endsWith('.md')) return 'document';
  return 'note';
}
function walk(dir, base = dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.name.startsWith('.') || e.name === 'node_modules') continue;
    const f = path.join(dir, e.name);
    if (e.isDirectory()) walk(f, base, out);
    else out.push(path.relative(base, f).replace(/\\/g, '/'));
  }
  return out;
}
// Obsidian resolves [[links]] across the whole vault, so do we: the vault is the nearest folder with `.obsidian`.
function vaultOf(dir) {
  for (let d = path.resolve(dir); ; d = path.dirname(d)) {
    if (fs.existsSync(path.join(d, '.obsidian'))) return d;
    if (path.dirname(d) === d) return null;
  }
}
const vaultIndex = new Map(); // vault -> {at, byName}
function vaultFiles(vault) {
  const c = vaultIndex.get(vault);
  if (c && Date.now() - c.at < 60_000) return c.byName;
  const byName = new Map();
  for (const f of walk(vault)) {
    const key = path.basename(f).replace(/\.md$/, '').toLowerCase();
    if (!byName.has(key) || f.endsWith('.md')) byName.set(key, f);
  }
  vaultIndex.set(vault, { at: Date.now(), byName });
  return byName;
}
// Node ids are paths relative to the person's folder; notes elsewhere in the vault get `../` paths.
function readable(p, f) {
  const full = path.resolve(p.path, f);
  const vault = vaultOf(p.path);
  return inside(p.path, f) || (vault && !path.relative(vault, full).startsWith('..'));
}

function graph(p) {
  const files = walk(p.path);
  const byName = new Map();
  for (const f of files) byName.set(path.basename(f).replace(/\.md$/, '').toLowerCase(), f);
  const vault = vaultOf(p.path);
  const inVault = vault ? vaultFiles(vault) : new Map();
  const nodes = new Map();
  const links = new Map();
  const add = (id, extra) => { if (!nodes.has(id)) nodes.set(id, { id, label: path.basename(id).replace(/\.md$/, ''), ...extra }); };
  for (const f of files) add(f, { kind: kindOf(f), size: fs.statSync(path.join(p.path, f)).size });
  for (const f of files.filter((x) => x.endsWith('.md'))) {
    const txt = fs.readFileSync(path.join(p.path, f), 'utf8');
    if (/^excalidraw-plugin:/m.test(txt)) { nodes.get(f).kind = 'system'; continue; }
    for (const m of txt.matchAll(/!?\[\[([^\]|#^]+)(?:[#^][^\]|]*)?(?:\|[^\]]*)?\]\]/g)) {
      const name = m[1].trim();
      const key = path.basename(name).replace(/\.md$/, '').toLowerCase();
      let target = byName.get(name.toLowerCase()) || byName.get(key);
      if (!target && inVault.has(key)) {
        target = path.relative(p.path, path.join(vault, inVault.get(key))).replace(/\\/g, '/');
        add(target, { kind: target.endsWith('.md') ? 'vault' : 'document' });
      }
      const to = target || `?${name}`;
      if (!target) add(to, { kind: 'missing', label: name });
      if (to === f) continue;
      const lk = [f, to].sort().join('→');
      const l = links.get(lk) || { source: f, target: to, weight: 0 };
      l.weight++;
      links.set(lk, l);
    }
  }
  return { nodes: [...nodes.values()], links: [...links.values()] };
}

// ---------- sign-in ----------
// The SDK ships its own Claude Code binary; sign-in runs through it, so nobody needs a terminal.

const CLI = (() => {
  // In the packaged app the binary is unpacked next to app.asar: executables cannot run from inside the archive.
  const exe = path.join(ROOT.replace(/app\.asar$/, 'app.asar.unpacked'), 'node_modules/@anthropic-ai', `claude-agent-sdk-${process.platform}-${process.arch}`, process.platform === 'win32' ? 'claude.exe' : 'claude');
  return fs.existsSync(exe) ? exe : 'claude';
})();

function authStatus() {
  return new Promise((resolve) => execFile(CLI, ['auth', 'status', '--json'], { env: cleanEnv, timeout: 90000 }, (err, out) => {
    try { const j = JSON.parse(out); resolve({ loggedIn: !!j.loggedIn, email: j.email || j.account?.email }); }
    catch { resolve({ loggedIn: false, error: String(err?.message || 'status failed') }); }
  }));
}

let login = null; // { proc, url, output, exited }
function startLogin() {
  if (login && !login.exited) return login;
  const proc = spawn(CLI, ['auth', 'login', '--claudeai'], { env: cleanEnv, stdio: ['pipe', 'pipe', 'pipe'] });
  const l = { proc, url: null, output: '', exited: false, started: Date.now() };
  const onData = (d) => {
    l.output += d.toString();
    l.url ||= l.output.match(/https:\/\/\S+/)?.[0] || null;
  };
  proc.stdout.on('data', onData);
  proc.stderr.on('data', onData);
  proc.on('exit', (code) => { l.exited = true; l.code = code; });
  proc.on('error', (e) => { l.exited = true; l.output += String(e.message); });
  login = l;
  return l;
}

// ---------- http ----------

const app = express();
app.use(express.json({ limit: '2mb' }));
app.use(express.static(path.join(ROOT, 'public')));
app.use('/vendor/force-graph', express.static(path.join(ROOT, 'node_modules/force-graph/dist')));
app.use('/vendor/marked', express.static(path.join(ROOT, 'node_modules/marked/lib')));

const h = (fn) => (req, res) => Promise.resolve(fn(req, res)).catch((e) => res.status(e.status || 500).json({ error: String(e.message || e) }));

app.get('/api/auth', h(async (req, res) => {
  const st = await authStatus();
  if (st.loggedIn && login && !login.exited) login.proc.kill();
  res.json({ ...st, pending: !!(login && !login.exited), url: login?.url || null, failed: !!(login?.exited && login.code) });
}));
app.post('/api/auth/login', h((req, res) => { const l = startLogin(); res.json({ pending: true, url: l.url }); }));
app.post('/api/auth/code', h((req, res) => {
  const code = String(req.body?.code || '').trim();
  if (!login || login.exited || !code) return res.status(400).json({ error: 'no login in progress' });
  login.proc.stdin.write(code + '\n');
  res.json({ ok: true });
}));

app.get('/api/people', h((req, res) => res.json(config().people)));
app.post('/api/people', h(async (req, res) => {
  const { name, path: dir } = req.body || {};
  if (!name || !dir || !fs.existsSync(dir) || !fs.statSync(dir).isDirectory()) return res.status(400).json({ error: 'folder not found' });
  const c = config();
  const id = `${Date.now().toString(36)}`;
  c.people.push({ id, name, path: path.resolve(dir) });
  writeJson(CONFIG, c);
  // Conversations that existed before the folder was added count as already consolidated.
  const st = readJson(STATE, {});
  st[id] = { dreamed: Object.fromEntries((await listSessions({ dir: path.resolve(dir) })).map((s) => [s.sessionId, s.lastModified])) };
  writeJson(STATE, st);
  res.json({ id });
}));

app.get('/api/people/:id/sessions', h(async (req, res) => {
  const p = person(req.params.id);
  const s = await listSessions({ dir: p.path });
  res.json(s.filter((x) => !x.tag?.startsWith('dream')).sort((a, b) => b.lastModified - a.lastModified)
    .map((x) => ({ id: x.sessionId, title: x.customTitle || x.summary || x.firstPrompt || '', at: x.lastModified })));
}));
app.get('/api/people/:id/sessions/:sid', h(async (req, res) => res.json(await conversation(person(req.params.id), req.params.sid))));

app.get('/api/people/:id/graph', h((req, res) => res.json(graph(person(req.params.id)))));
app.get('/api/people/:id/file', h((req, res) => {
  const p = person(req.params.id);
  const f = String(req.query.f || '');
  if (!readable(p, f)) return res.status(400).json({ error: 'bad path' });
  const full = path.join(p.path, f);
  if (f.endsWith('.md')) return res.json({ name: f, text: fs.readFileSync(full, 'utf8') });
  res.sendFile(full);
}));

app.get('/api/people/:id/memory', h((req, res) => res.json(dreamLog.get(req.params.id) || { status: 'idle' })));
app.post('/api/people/:id/memory', h(async (req, res) => {
  const p = person(req.params.id);
  if (busy.get(p.id)) return res.json({ status: 'waiting', reason: 'chat running' });
  const all = await listSessions({ dir: p.path });
  const ids = req.body?.sessionId ? [req.body.sessionId] : undreamed(p, all).map((s) => s.sessionId);
  if (!ids.length) return res.json({ status: 'nothing' });
  dream(p, ids);
  res.json({ status: 'running', count: ids.length });
}));

// Chat: streams Server-Sent Events.
app.post('/api/people/:id/chat', h(async (req, res) => {
  const p = person(req.params.id);
  const { text, sessionId, skill } = req.body || {};
  if (!text && !skill) return res.status(400).json({ error: 'empty' });
  if (skill && !SKILLS.includes(skill)) return res.status(400).json({ error: 'unknown skill' });
  const prompt = skill ? `/concilium:${skill}${text ? ' ' + text : ''}` : text;

  res.writeHead(200, { 'Content-Type': 'text/event-stream; charset=utf-8', 'Cache-Control': 'no-cache', Connection: 'keep-alive' });
  const send = (ev, data) => res.write(`event: ${ev}\ndata: ${JSON.stringify(data)}\n\n`);
  const abort = new AbortController();
  res.on('close', () => { if (!res.writableEnded) abort.abort(); });

  busy.set(p.id, (busy.get(p.id) || 0) + 1);
  try {
    const q = query({ prompt, options: agentOptions(p, { resume: sessionId || undefined, includePartialMessages: true, abortController: abort }) });
    for await (const m of q) {
      if (m.type === 'system' && m.subtype === 'init') send('session', { sessionId: m.session_id });
      else if (m.type === 'stream_event' && !m.parent_tool_use_id) {
        const e = m.event;
        if (e.type === 'content_block_delta' && e.delta?.type === 'text_delta') send('text', { t: e.delta.text });
        if (e.type === 'content_block_start' && e.content_block?.type === 'text') send('block', {});
      } else if (m.type === 'assistant') {
        for (const b of m.message.content) if (b.type === 'tool_use') send('tool', { name: b.name, input: summarizeInput(b.name, b.input), sub: !!m.parent_tool_use_id });
      } else if (m.type === 'result') send('done', { ok: m.subtype === 'success' && !m.is_error, cost: m.total_cost_usd, error: m.is_error ? m.result : m.subtype !== 'success' ? m.subtype : undefined });
    }
  } catch (e) {
    if (!abort.signal.aborted) send('done', { ok: false, error: /authenticat|login|OAuth/i.test(String(e.message)) ? 'NEEDS_LOGIN' : String(e.message || e) });
  } finally {
    busy.set(p.id, busy.get(p.id) - 1);
    res.end();
  }
}));

function summarizeInput(name, i = {}) {
  if (i.file_path) return path.basename(i.file_path);
  if (i.pattern) return i.pattern;
  if (i.query) return i.query;
  if (i.url) { try { return new URL(i.url).hostname; } catch { return i.url; } }
  if (i.skill) return i.skill;
  if (i.description) return i.description;
  return '';
}

app.listen(PORT, '127.0.0.1', () => console.log(`Concilium: http://localhost:${PORT}`));
