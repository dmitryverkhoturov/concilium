// Simulated patient: one agent plays a person with a hidden diagnosis, Concilium (the markdown edition,
// run exactly as a user would run it in Claude Code) tries to find it. Like SDBench for MAI-DxO, but
// sequential and with long-term memory. Usage: node eval/simulate.mjs <case> [maxTurns] [--schedule open,free,/sleep] [--out <folder>]
// --out puts the person's folder there instead of eval/runs (e.g. a demo folder the app opens).
// --app <app data folder> talks exactly as the Windows app does: its plugin and principles (short, plain words),
// a bare memory skeleton in the folder, buttons as /concilium:<skill>. In the schedule, `sleep` without a slash is
// the app's background memory pass: a separate run that never shows in the conversation; `/sleep` is the button.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { query } from '../app/node_modules/@anthropic-ai/claude-agent-sdk/sdk.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const flag = (name) => { const i = process.argv.indexOf(name); if (i < 0) return null; const v = process.argv[i + 1]; process.argv.splice(i, 2); return v; };
const scheduleArg = flag('--schedule');
const outArg = flag('--out');
const appData = flag('--app');
const caseId = process.argv[2] || 'iron-man';
const CASE = path.join(HERE, 'cases', caseId);
const caseText = fs.readFileSync(path.join(CASE, 'case.md'), 'utf8');
const truth = caseText.match(/## Truth[^\n]*\n([\s\S]*?)\n## /)[1].trim();
const patientBrief = caseText.replace(/## Truth[\s\S]*?(?=\n## )/, '');

const stamp = new Date().toISOString().slice(0, 16).replace(/[:T]/g, '-');
const RUN = path.join(HERE, 'runs', `${caseId}-${stamp}`);
const FOLDER = path.join(outArg ? path.resolve(outArg) : RUN, caseText.match(/## Persona\s+(\w+)/)?.[1] || 'Person');
const TEMPLATE = path.join(HERE, '..', 'concilium-markdown');
if (appData) {
  // The app keeps skills in its plugin; a new person's folder gets only the memory skeleton.
  fs.mkdirSync(FOLDER, { recursive: true });
  for (const f of fs.readdirSync(TEMPLATE)) if (f.endsWith('.md') && f !== 'README.md') fs.copyFileSync(path.join(TEMPLATE, f), path.join(FOLDER, f));
} else {
  fs.cpSync(TEMPLATE, FOLDER, { recursive: true });
  fs.rmSync(path.join(FOLDER, 'README.md'));
}
if (fs.existsSync(path.join(CASE, 'documents'))) fs.cpSync(path.join(CASE, 'documents'), path.join(FOLDER, 'documents'), { recursive: true });

const env = Object.fromEntries(Object.entries(process.env).filter(([k]) =>
  !(k.startsWith('CLAUDE_CODE_') || k === 'ANTHROPIC_BASE_URL' || k === 'CLAUDECODE')));
const log = (s) => { fs.appendFileSync(path.join(RUN, 'transcript.md'), s + '\n\n'); console.log(s.slice(0, 300)); };

// The person's turns. Free turns are written by the patient agent; slash commands are the buttons.
const SCHEDULE = scheduleArg ? scheduleArg.split(',')
  : ['open', 'free', 'free', '/ask-questions', 'free', '/research', 'free', '/next-step', 'free', 'free', '/next-step', 'free', '/sleep'];
const maxTurns = Number(process.argv[3] || SCHEDULE.length);

const inside = (f) => { const r = path.relative(FOLDER, path.resolve(FOLDER, f)); return r && !r.startsWith('..') && !path.isAbsolute(r); };
let sessionId;
const options = (extra) => ({
  cwd: FOLDER, env,
  settingSources: ['project'], // only the folder's own CLAUDE.md, AGENTS.md and skills: the markdown edition as shipped
  disallowedTools: ['Bash', 'PowerShell', 'NotebookEdit'],
  canUseTool: async (tool, input) => {
    if (['Write', 'Edit', 'MultiEdit'].includes(tool)) return inside(input.file_path || '')
      ? { behavior: 'allow', updatedInput: input } : { behavior: 'deny', message: 'Write only inside the folder.' };
    return { behavior: 'allow', updatedInput: input };
  },
  ...(appData ? {
    plugins: [{ type: 'local', path: path.join(appData, 'plugin') }],
    systemPrompt: { type: 'preset', preset: 'claude_code', append: fs.readFileSync(path.join(appData, 'principles.md'), 'utf8') },
  } : {}),
  ...extra,
});

async function concilium(prompt) {
  let text = '';
  const q = query({ prompt, options: options({ resume: sessionId }) });
  for await (const m of q) {
    if (m.type === 'system' && m.subtype === 'init') sessionId = m.session_id;
    if (m.type === 'result') text = m.result || `[${m.subtype}]`;
  }
  return text;
}

// The app's background memory pass: the conversation so far, filed by a separate run that leaves no conversation behind.
async function dream() {
  const conv = talk.map((t) => `${t.who === 'patient' ? 'PERSON' : 'AGENT'}: ${t.text}`).join('\n\n');
  const date = new Date().toISOString().slice(0, 10);
  const q = query({
    prompt: `/concilium:sleep\n\nConversations to consolidate:\n\n<conversation id="${sessionId}" date="${date}">\n${conv}\n</conversation>`,
    options: options({ persistSession: false }),
  });
  let text = '';
  for await (const m of q) if (m.type === 'result') text = m.result || `[${m.subtype}]`;
  return text;
}

const talk = [];
async function patient(instruction) {
  const history = talk.map((t) => `${t.who === 'patient' ? 'YOU' : 'HEALTH AGENT'}: ${t.text}`).join('\n\n');
  const q = query({
    prompt: `${history ? `The conversation so far:\n\n${history}\n\n` : ''}${instruction}`,
    options: {
      env, tools: [], settingSources: [], maxTurns: 1, persistSession: false, model: 'sonnet',
      systemPrompt: `You role-play a patient talking to a health agent app. Stay in character; never mention you are an AI or a simulation.

${patientBrief}

Rules:
- Answer only what is asked; volunteer nothing from "What Mark knows" unless the condition says so. Short, plain messages.
- You do not know your diagnosis. Never name the disease from the Truth or hint at its cause.
- Tests: if the health agent recommended specific tests or imaging in its earlier messages, you may say you got them done since the last message and report the results from "Results" for exactly those tests, nothing more. A test not in the list is normal. Tests nobody recommended were not done.
- If the agent asks you to choose a direction, pick what bothers you most.
- Output only your next message, nothing else.`,
    },
  });
  let text = '';
  for await (const m of q) if (m.type === 'result') text = m.result || '';
  return text.trim();
}

fs.mkdirSync(RUN, { recursive: true });
log(`# Run ${caseId} — ${stamp}`);
for (let i = 0; i < Math.min(maxTurns, SCHEDULE.length); i++) {
  const step = SCHEDULE[i];
  if (step === 'sleep') { log(`*Memory pass:* ${await dream()}`); continue; }
  let msg;
  if (step === 'open') msg = caseText.match(/## Opening message\s+"([\s\S]*?)"/)[1].replace(/\s+/g, ' ');
  else if (step === 'free') msg = await patient('Write your next message to the health agent.');
  else msg = step;
  talk.push({ who: 'patient', text: msg });
  log(`**Patient:** ${msg}`);
  const reply = await concilium(appData && msg.startsWith('/') ? '/concilium:' + msg.slice(1) : msg);
  talk.push({ who: 'agent', text: reply });
  log(`**Concilium:** ${reply}`);
}

// Judge: compare the final memory with the truth.
const read = (f) => { try { return fs.readFileSync(path.join(FOLDER, f), 'utf8'); } catch { return ''; } };
const q = query({
  prompt: `TRUTH:\n${truth}\n\nFINAL HYPOTHESES.md:\n${read('HYPOTHESES.md')}\n\nFINAL MEMORY.md:\n${read('MEMORY.md')}\n\nFINAL TODO.md:\n${read('TODO.md')}\n\n` +
    `Score the health agent. Reply with JSON only: {"leading_correct": true|false|"partial", "true_dx_rank": number|null, "true_dx_probability": number|null, ` +
    `"turns": ${talk.length / 2}, "tests_recommended": [..], "unnecessary_tests": [..], "notes": "two sentences"}`,
  options: { env, tools: [], settingSources: [], maxTurns: 1, persistSession: false },
});
let verdict = '';
for await (const m of q) if (m.type === 'result') verdict = m.result || '';
fs.writeFileSync(path.join(RUN, 'result.json'), verdict.replace(/^```(json)?|```$/gm, '').trim());
log(`## Verdict\n\n${verdict}`);
console.log(`\nRun folder: ${RUN}\nConcilium session: ${sessionId}`);
