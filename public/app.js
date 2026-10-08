// Health Agent — front end. Plain JS, no build step.
const $ = (s) => document.querySelector(s);
const api = async (url, opts = {}) => {
  const r = await fetch(url, { headers: { 'Content-Type': 'application/json' }, ...opts, body: opts.body && JSON.stringify(opts.body) });
  const j = await r.json().catch(() => ({}));
  if (!r.ok) throw new Error(j.error || r.statusText);
  return j;
};

// ---------- i18n ----------
const I18N = {
  ru: {
    app: 'Здоровье', map: 'Карта', note: 'Заметка', newChat: 'Новый разговор', chats: 'Разговоры', send: 'Отправить',
    placeholder: 'Расскажите, что беспокоит, или задайте вопрос…', addTitle: 'Добавить человека', name: 'Имя',
    folder: 'Папка с заметками о здоровье', cancel: 'Отмена', add: 'Добавить', addPerson: '+ Добавить',
    hello: (n) => `Здравствуйте${n ? ', ' + n : ''}!`, helloSub: 'Расскажите своими словами, что беспокоит. Или выберите, с чего начать:',
    noPeople: 'Добавьте первого человека: его имя и папку с заметками о здоровье.',
    a: {
      'next-step': ['🧭', 'Что делать дальше', 'Один лучший следующий шаг'],
      'ask-questions': ['💬', 'Задай мне вопросы', 'Уточнить то, что важно'],
      hypotheses: ['🔎', 'Разберись глубже', 'Изучить и обновить картину'],
      'update-memory': ['📝', 'Запомни разговор', 'Сохранить главное'],
    },
    tool: { Read: 'Читаю', Glob: 'Ищу файлы', Grep: 'Ищу в заметках', WebSearch: 'Ищу в интернете', WebFetch: 'Читаю источник', Write: 'Записываю', Edit: 'Записываю', MultiEdit: 'Записываю', Skill: 'Беру метод', Task: 'Поручаю помощнику', Agent: 'Поручаю помощнику', TodoWrite: 'Планирую' },
    login: { title: 'Подключим помощника', text: 'Нажмите кнопку — откроется страница Claude. Войдите в свой аккаунт и нажмите «Разрешить» (Authorize). Потом вернитесь в это окно.', btn: 'Войти через Claude', waiting: 'Жду подтверждения на странице Claude…', again: 'Открыть страницу ещё раз', code: 'Если страница показала код — вставьте его сюда:', codeBtn: 'Готово', failed: 'Не получилось войти. Попробуйте ещё раз.' },
    needsLogin: 'Нужно войти в Claude.',thinking: 'Думаю…', stopped: 'Остановлено.', failed: 'Не получилось: ',
    mem: { idle: '', running: 'Обновляю память…', done: 'Память обновлена', error: 'Память не обновилась', nothing: 'Нового для памяти нет', waiting: 'Обновлю память, когда закончим' },
    memBtn: 'Обновить память сейчас', micNo: 'Голосовой ввод работает в Chrome и Edge. В Windows можно нажать Win+H.',
    legend: { memory: 'Главное', 'patient facts': 'Факты', hypotheses: 'Версии', questions: 'Вопросы', todo: 'Планы', history: 'История', branch: 'Темы', note: 'Заметки', vault: 'Из хранилища', document: 'Документы', missing: 'Нет заметки' },
    ago: (m) => m < 1 ? 'только что' : m < 60 ? `${m} мин назад` : m < 1440 ? `${Math.round(m / 60)} ч назад` : `${Math.round(m / 1440)} дн назад`,
  },
  en: {
    app: 'Health', map: 'Map', note: 'Note', newChat: 'New conversation', chats: 'Conversations', send: 'Send',
    placeholder: 'Tell me what bothers you, or ask a question…', addTitle: 'Add a person', name: 'Name',
    folder: 'Folder with health notes', cancel: 'Cancel', add: 'Add', addPerson: '+ Add',
    hello: (n) => `Hello${n ? ', ' + n : ''}!`, helloSub: 'Tell me in your own words what bothers you. Or pick where to start:',
    noPeople: 'Add the first person: a name and their health notes folder.',
    a: {
      'next-step': ['🧭', 'What should I do next', 'One best next step'],
      'ask-questions': ['💬', 'Ask me questions', 'Clarify what matters'],
      hypotheses: ['🔎', 'Dig deeper', 'Research and update the picture'],
      'update-memory': ['📝', 'Remember this talk', 'Save what matters'],
    },
    tool: { Read: 'Reading', Glob: 'Looking for files', Grep: 'Searching notes', WebSearch: 'Searching the web', WebFetch: 'Reading a source', Write: 'Writing down', Edit: 'Writing down', MultiEdit: 'Writing down', Skill: 'Using method', Task: 'Asking a helper', Agent: 'Asking a helper', TodoWrite: 'Planning' },
    login: { title: "Let's connect the assistant", text: 'Press the button — a Claude page will open. Sign in to your account and press "Authorize". Then come back to this window.', btn: 'Sign in with Claude', waiting: 'Waiting for confirmation on the Claude page…', again: 'Open the page again', code: 'If the page shows a code, paste it here:', codeBtn: 'Done', failed: 'Sign-in failed. Please try again.' },
    needsLogin: 'Claude sign-in is needed.',thinking: 'Thinking…', stopped: 'Stopped.', failed: 'Failed: ',
    mem: { idle: '', running: 'Updating memory…', done: 'Memory updated', error: 'Memory update failed', nothing: 'Nothing new to remember', waiting: 'Will update memory when we finish' },
    memBtn: 'Update memory now', micNo: 'Voice input works in Chrome and Edge. On Windows you can also press Win+H.',
    legend: { memory: 'Overview', 'patient facts': 'Facts', hypotheses: 'Hypotheses', questions: 'Questions', todo: 'Plans', history: 'History', branch: 'Topics', note: 'Notes', vault: 'From the vault', document: 'Documents', missing: 'No note yet' },
    ago: (m) => m < 1 ? 'just now' : m < 60 ? `${m} min ago` : m < 1440 ? `${Math.round(m / 60)} h ago` : `${Math.round(m / 1440)} d ago`,
  },
};
const store = { get: (k, d) => { try { return localStorage.getItem(k) ?? d; } catch { return d; } }, set: (k, v) => { try { localStorage.setItem(k, v); } catch {} } };
let lang = store.get('lang', 'ru');
const T = () => I18N[lang];
function applyI18n() {
  document.documentElement.lang = lang;
  document.querySelectorAll('[data-i18n]').forEach((e) => { e.textContent = T()[e.dataset.i18n]; });
  document.querySelectorAll('[data-i18n-ph]').forEach((e) => { e.placeholder = T()[e.dataset.i18nPh]; });
  $('#lang').textContent = lang === 'ru' ? 'EN' : 'RU';
  document.title = T().app;
}

// ---------- state ----------
const S = { people: [], pid: store.get('pid', ''), sid: null, dirty: false, streaming: null, graph: null };
const cur = () => S.people.find((p) => p.id === S.pid);

// ---------- markdown ----------
const esc = (s) => s.replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
marked.use({ renderer: { html: (tok) => esc(tok.text || tok.raw || '') } });
const md = (t) => marked.parse(t || '');
const mdNote = (t) => md(t.replace(/^---\n[\s\S]*?\n---\n/, '').replace(/!?\[\[([^\]|#^]+)(?:[#^][^\]|]*)?(?:\|([^\]]*))?\]\]/g,
  (_, n, alias) => `<a class="wl" data-wl="${esc(n.trim())}">${esc((alias || n).trim())}</a>`));

// ---------- people ----------
async function loadPeople() {
  S.people = await api('/api/people');
  if (!cur()) S.pid = S.people[0]?.id || '';
  renderPeople();
  await selectPerson(S.pid);
}
function renderPeople() {
  const nav = $('#people');
  nav.innerHTML = '';
  for (const p of S.people) {
    const b = document.createElement('button');
    b.className = 'pill' + (p.id === S.pid ? ' active' : '');
    b.textContent = p.name;
    b.onclick = () => selectPerson(p.id);
    nav.append(b);
  }
  const add = document.createElement('button');
  add.className = 'pill add';
  add.textContent = T().addPerson;
  add.onclick = () => { $('#addErr').textContent = ''; $('#addPerson').showModal(); };
  nav.append(add);
}
$('#addForm').addEventListener('submit', async (e) => {
  if (e.submitter?.value !== 'ok') return;
  e.preventDefault();
  const f = new FormData(e.target);
  try {
    const { id } = await api('/api/people', { method: 'POST', body: { name: f.get('name'), path: f.get('path') } });
    $('#addPerson').close();
    S.pid = id;
    await loadPeople();
  } catch (err) { $('#addErr').textContent = err.message; }
});

async function selectPerson(id) {
  endConversation();
  S.pid = id;
  store.set('pid', id);
  S.sid = null;
  renderPeople();
  renderWelcome();
  if (!id) return;
  await Promise.all([loadSessions(), loadGraph(), pollMemory()]);
}

// ---------- sessions ----------
async function loadSessions() {
  if (!S.pid) return;
  const list = await api(`/api/people/${S.pid}/sessions`);
  const ul = $('#sessions');
  ul.innerHTML = '';
  for (const s of list) {
    const li = document.createElement('li');
    li.className = s.id === S.sid ? 'active' : '';
    li.innerHTML = `<div class="t"></div><div class="d"></div>`;
    li.querySelector('.t').textContent = cleanTitle(s.title);
    li.querySelector('.d').textContent = T().ago(Math.round((Date.now() - s.at) / 60000));
    li.onclick = () => openSession(s.id);
    ul.append(li);
  }
}
function cleanTitle(t) {
  const m = t.match(/health-agent:([\w-]+)/);
  if (m && T().a[m[1]]) return T().a[m[1]][1];
  return t.replace(/<[^>]+>/g, '').trim() || '…';
}
async function openSession(id) {
  if (S.streaming) return;
  if (id !== S.sid) endConversation();
  S.sid = id;
  closeSideOnNarrow();
  const box = $('#messages');
  box.innerHTML = '';
  const msgs = await api(`/api/people/${S.pid}/sessions/${id}`);
  for (const m of msgs) addMsg(m.role, m.text, m.skill);
  box.scrollTop = box.scrollHeight;
  loadSessions();
}
$('#newChat').onclick = () => { if (S.streaming) return; endConversation(); S.sid = null; renderWelcome(); loadSessions(); closeSideOnNarrow(); };

// A conversation "ends" when the person leaves it. Then memory is consolidated in the background.
function endConversation() {
  if (S.dirty && S.pid && S.sid) {
    api(`/api/people/${S.pid}/memory`, { method: 'POST', body: { sessionId: S.sid } }).then(showMemory).catch(() => {});
  }
  S.dirty = false;
}
window.addEventListener('pagehide', () => {
  if (S.dirty && S.pid && S.sid) navigator.sendBeacon(`/api/people/${S.pid}/memory`, new Blob([JSON.stringify({ sessionId: S.sid })], { type: 'application/json' }));
});
let idleTimer;
function armIdle() { clearTimeout(idleTimer); idleTimer = setTimeout(endConversation, 20 * 60 * 1000); }

// ---------- sign-in ----------
// One button: the server runs Claude's own sign-in, the browser opens, the person presses "Authorize".
let authTimer;
async function checkAuth() {
  const a = await api('/api/auth').catch(() => ({ loggedIn: true }));
  S.authed = a.loggedIn;
  document.body.classList.toggle('noauth', !a.loggedIn);
  if (!a.loggedIn) renderLogin(a);
  return a.loggedIn;
}
function renderLogin(a = {}) {
  const L = T().login;
  const box = $('#messages');
  box.innerHTML = '';
  const w = document.createElement('div');
  w.className = 'welcome login';
  w.innerHTML = `<div class="big-logo">+</div><h1></h1><p class="lead"></p><button class="primary huge"></button><div class="pending hidden"><p class="wait"></p><a target="_blank" rel="noopener" class="again"></a><label class="codebox"><span></span><div class="row"><input autocomplete="off"><button class="primary"></button></div></label></div><p class="err hidden"></p>`;
  w.querySelector('h1').textContent = L.title;
  w.querySelector('.lead').textContent = L.text;
  const btn = w.querySelector('.huge');
  btn.textContent = L.btn;
  w.querySelector('.wait').textContent = L.waiting;
  w.querySelector('.again').textContent = L.again;
  w.querySelector('.codebox span').textContent = L.code;
  w.querySelector('.codebox .primary').textContent = L.codeBtn;
  const pending = w.querySelector('.pending');
  // Claude opens the sign-in page itself. The fallback link and code box appear only if nothing happened for a while.
  const fallback = [w.querySelector('.again'), w.querySelector('.codebox')];
  const showPending = (url) => {
    btn.classList.add('hidden');
    pending.classList.remove('hidden');
    if (url) w.querySelector('.again').href = url;
    fallback.forEach((x) => x.classList.add('hidden'));
    setTimeout(() => { if (!S.authed) fallback.forEach((x) => x.classList.remove('hidden')); }, 20000);
  };
  btn.onclick = async () => {
    const r = await api('/api/auth/login', { method: 'POST' });
    showPending(r.url);
    poll();
  };
  w.querySelector('.codebox .primary').onclick = async () => {
    const code = w.querySelector('.codebox input').value.trim();
    if (code) await api('/api/auth/code', { method: 'POST', body: { code } }).catch(() => {});
  };
  async function poll() {
    clearTimeout(authTimer);
    const s = await api('/api/auth').catch(() => ({}));
    if (s.loggedIn) { document.body.classList.remove('noauth'); S.authed = true; renderWelcome(); return; }
    if (s.url) w.querySelector('.again').href = s.url;
    if (s.failed && !s.pending) {
      const e = w.querySelector('.err'); e.textContent = L.failed; e.classList.remove('hidden');
      btn.classList.remove('hidden'); pending.classList.add('hidden');
      return;
    }
    authTimer = setTimeout(poll, 2000);
  }
  if (a.pending) { showPending(a.url); poll(); }
  box.append(w);
}

// ---------- chat ----------
function renderWelcome() {
  if (S.authed === false) return renderLogin();
  const box = $('#messages');
  box.innerHTML = '';
  const w = document.createElement('div');
  w.className = 'welcome';
  if (!S.people.length) { w.innerHTML = `<h1>${T().hello('')}</h1><p>${T().noPeople}</p>`; box.append(w); return; }
  w.innerHTML = `<h1></h1><p>${T().helloSub}</p><div class="cards"></div>`;
  w.querySelector('h1').textContent = T().hello(cur()?.name);
  for (const [k, [e, title, sub]] of Object.entries(T().a)) {
    const c = document.createElement('button');
    c.className = 'card';
    c.innerHTML = `<div class="e">${e}</div><b></b><span></span>`;
    c.querySelector('b').textContent = title;
    c.querySelector('span').textContent = sub;
    c.onclick = () => send({ skill: k });
    w.querySelector('.cards').append(c);
  }
  box.append(w);
}
function renderActions() {
  const a = $('#actions');
  a.innerHTML = '';
  for (const [k, [e, title]] of Object.entries(T().a)) {
    const b = document.createElement('button');
    b.className = 'chip';
    b.textContent = `${e} ${title}`;
    b.onclick = () => send({ skill: k });
    a.append(b);
  }
}
function addMsg(role, text, skill) {
  const box = $('#messages');
  box.querySelector('.welcome')?.remove();
  const d = document.createElement('div');
  if (skill) { d.className = 'msg skill'; const a = T().a[skill]; d.textContent = a ? `${a[0]} ${a[1]}` : skill; }
  else if (role === 'user') { d.className = 'msg user'; d.textContent = text; }
  else { d.className = 'msg assistant'; d.innerHTML = md(text); }
  box.append(d);
  box.scrollTop = box.scrollHeight;
  return d;
}

async function send({ text, skill } = {}) {
  if (S.streaming || !S.pid) return;
  text = (text ?? '').trim();
  if (!text && !skill) return;
  stopMic();
  addMsg('user', text, skill);
  $('#input').value = '';
  autosize();
  const box = $('#messages');
  const work = document.createElement('div');
  work.className = 'work';
  work.innerHTML = `<div class="now">${T().thinking}</div>`;
  box.append(work);
  let bubble = null, buf = '';
  const ctrl = new AbortController();
  S.streaming = ctrl;
  setBusy(true);
  try {
    const r = await fetch(`/api/people/${S.pid}/chat`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ text, skill, sessionId: S.sid }), signal: ctrl.signal,
    });
    if (!r.ok) throw new Error((await r.json().catch(() => ({}))).error || r.statusText);
    const reader = r.body.getReader();
    const dec = new TextDecoder();
    let pending = '';
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      pending += dec.decode(value, { stream: true });
      let i;
      while ((i = pending.indexOf('\n\n')) >= 0) {
        const chunk = pending.slice(0, i); pending = pending.slice(i + 2);
        const ev = /^event: (.*)$/m.exec(chunk)?.[1];
        const data = JSON.parse(/^data: (.*)$/m.exec(chunk)?.[1] || '{}');
        if (ev === 'session') { S.sid = data.sessionId; }
        else if (ev === 'block') { if (buf) buf += '\n\n'; }
        else if (ev === 'text') {
          buf += data.t;
          if (!bubble) { bubble = addMsg('assistant', ''); box.append(work); }
          bubble.innerHTML = md(buf);
          box.scrollTop = box.scrollHeight;
        } else if (ev === 'tool') {
          const label = T().tool[data.name] || data.name;
          const now = work.querySelector('.now');
          if (now) { now.className = ''; }
          const line = document.createElement('div');
          line.className = 'now';
          line.textContent = `${label}${data.input ? ': ' + data.input : ''}`;
          work.append(line);
          while (work.children.length > 4) work.firstChild.remove();
          box.scrollTop = box.scrollHeight;
        } else if (ev === 'done' && !data.ok) {
          const e = document.createElement('div'); e.className = 'err'; const needs = data.error === 'NEEDS_LOGIN' || /authenticat|OAuth/i.test(data.error || '');
          e.textContent = needs ? T().needsLogin : T().failed + (data.error || '');
          if (needs) setTimeout(checkAuth, 1500);
          box.append(e);
        }
      }
    }
  } catch (e) {
    const d = document.createElement('div'); d.className = 'err';
    d.textContent = ctrl.signal.aborted ? T().stopped : T().failed + e.message;
    box.append(d);
  } finally {
    work.remove();
    S.streaming = null;
    S.dirty = skill !== 'update-memory';
    if (skill === 'update-memory') S.dirty = false;
    setBusy(false);
    armIdle();
    loadSessions();
    loadGraph();
  }
}
function setBusy(b) {
  $('#send').textContent = b ? '■' : T().send;
  document.querySelectorAll('.chip, .card').forEach((x) => { x.disabled = b; });
}
$('#send').onclick = () => (S.streaming ? S.streaming.abort() : send({ text: $('#input').value }));
$('#input').addEventListener('keydown', (e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send({ text: $('#input').value }); } });
function autosize() { const t = $('#input'); t.style.height = 'auto'; t.style.height = Math.min(t.scrollHeight, innerHeight * 0.4) + 'px'; }
$('#input').addEventListener('input', autosize);

// ---------- memory status ----------
let memTimer;
function showMemory(s) {
  const el = $('#memory');
  const label = T().mem[s.status] ?? '';
  el.innerHTML = '';
  const line = document.createElement('div');
  line.textContent = label + (s.status === 'done' && s.at ? ` · ${T().ago(Math.round((Date.now() - s.at) / 60000))}` : '');
  el.append(line);
  const b = document.createElement('button');
  b.textContent = T().memBtn;
  b.onclick = async () => showMemory(await api(`/api/people/${S.pid}/memory`, { method: 'POST', body: {} }));
  if (s.status !== 'running') el.append(b);
  clearTimeout(memTimer);
  if (s.status === 'running' || s.status === 'waiting') memTimer = setTimeout(pollMemory, 5000);
  if (s.status === 'done') loadGraph();
}
async function pollMemory() { if (S.pid) showMemory(await api(`/api/people/${S.pid}/memory`)); }

// ---------- graph ----------
const COLORS = { memory: '#2f8f62', 'patient facts': '#3b7dd8', hypotheses: '#d0663d', questions: '#9a5bd1', todo: '#c99a1d', history: '#7d8a99', identity: '#7d8a99', branch: '#d3478a', note: '#6aa7a0', vault: '#6aa7a0', document: '#a3a093', system: '#c9c4b8', missing: '#c9c4b8' };
async function loadGraph() {
  if (!S.pid || $('#layout')?.classList.contains('nopanel')) return;
  const data = await api(`/api/people/${S.pid}/graph`);
  const deg = {};
  for (const l of data.links) { deg[l.source] = (deg[l.source] || 0) + l.weight; deg[l.target] = (deg[l.target] || 0) + l.weight; }
  data.nodes.forEach((n) => { n.deg = deg[n.id] || 0; });
  const el = $('#graph');
  const dark = matchMedia('(prefers-color-scheme: dark)').matches;
  if (!S.graph) {
    S.graph = ForceGraph()(el)
      .nodeId('id')
      .nodeVal((n) => 2 + Math.sqrt(n.deg) * 2)
      .nodeColor((n) => COLORS[n.kind] || COLORS.note)
      .nodeLabel((n) => n.label)
      .linkColor(() => (matchMedia('(prefers-color-scheme: dark)').matches ? 'rgba(255,255,255,.14)' : 'rgba(0,0,0,.12)'))
      .linkWidth((l) => Math.min(3, 0.6 + Math.log(l.weight + 1)))
      .nodeCanvasObjectMode(() => 'after')
      .nodeCanvasObject((n, ctx, scale) => {
        if (scale < 1.1 && n.deg < 6 && n.kind !== 'memory') return;
        const size = Math.max(10 / scale, 3);
        ctx.font = `${size}px Segoe UI, sans-serif`;
        ctx.textAlign = 'center'; ctx.textBaseline = 'top';
        ctx.fillStyle = matchMedia('(prefers-color-scheme: dark)').matches ? '#cfccc4' : '#4a463f';
        const r = Math.sqrt(2 + Math.sqrt(n.deg) * 2) * 4;
        ctx.fillText(n.label.replace(/^\S+ (?=(MEMORY|HISTORY|TODO|HYPOTHESES|QUESTIONS|IDENTITY|PATIENT FACTS)$)/, ''), n.x, n.y + r + 1);
      })
      .onNodeClick((n) => { if (n.kind !== 'missing') openNote(n.id); })
      .cooldownTicks(120);
    S.graph.d3Force('charge').strength(-120);
    new ResizeObserver(() => S.graph.width(el.clientWidth).height(el.clientHeight)).observe(el);
  }
  S.graph.graphData(data);
  const kinds = new Set(data.nodes.map((n) => n.kind));
  $('#legend').innerHTML = Object.entries(T().legend).filter(([k]) => kinds.has(k))
    .map(([k, v]) => `<span><i style="background:${COLORS[k]}"></i>${v}</span>`).join('');
  void dark;
}
async function openNote(f) {
  showTab('note');
  const n = $('#note');
  if (!f.endsWith('.md')) { n.innerHTML = `<iframe src="/api/people/${S.pid}/file?f=${encodeURIComponent(f)}" style="width:100%;height:80vh;border:0"></iframe>`; return; }
  const { text } = await api(`/api/people/${S.pid}/file?f=${encodeURIComponent(f)}`);
  n.innerHTML = mdNote(text);
  n.parentElement.scrollTop = 0;
}
$('#note').addEventListener('click', (e) => {
  const a = e.target.closest('a.wl');
  if (!a) return;
  const name = a.dataset.wl.toLowerCase();
  const node = S.graph?.graphData().nodes.find((x) => x.label.toLowerCase() === name || x.id.toLowerCase() === name);
  if (node && node.kind !== 'missing') openNote(node.id);
});
function showTab(t) {
  document.querySelectorAll('.tab').forEach((b) => b.classList.toggle('active', b.dataset.tab === t));
  $('#graphTab').classList.toggle('hidden', t !== 'graph');
  $('#noteTab').classList.toggle('hidden', t !== 'note');
}
document.querySelectorAll('.tab').forEach((b) => (b.onclick = () => showTab(b.dataset.tab)));
function setPanel(open) {
  document.querySelector('.layout').classList.toggle('nopanel', !open);
  store.set('panel', open ? '1' : '0');
  if (open) { $('.layout').id = 'layout'; loadGraph(); }
}
$('#toggleMap').onclick = () => setPanel(document.querySelector('.layout').classList.contains('nopanel'));
$('#closePanel').onclick = () => setPanel(false);
$('.layout').id = 'layout';

// ---------- voice ----------
const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
let rec = null;
function stopMic() { if (rec) { rec.stop(); rec = null; $('#mic').classList.remove('on'); } }
$('#mic').onclick = () => {
  if (rec) return stopMic();
  if (!SR) { alert(T().micNo); return; }
  rec = new SR();
  rec.lang = lang === 'ru' ? 'ru-RU' : 'en-US';
  rec.continuous = true;
  rec.interimResults = true;
  const base = $('#input').value ? $('#input').value.trimEnd() + ' ' : '';
  rec.onresult = (e) => {
    let t = '';
    for (const r of e.results) t += r[0].transcript;
    $('#input').value = base + t;
    autosize();
  };
  rec.onerror = (e) => { if (e.error === 'not-allowed' || e.error === 'network') alert(T().micNo); stopMic(); };
  rec.onend = () => { if (rec) stopMic(); };
  rec.start();
  $('#mic').classList.add('on');
};

// ---------- layout bits ----------
$('#lang').onclick = () => { lang = lang === 'ru' ? 'en' : 'ru'; store.set('lang', lang); applyI18n(); renderPeople(); renderActions(); if (!S.sid) renderWelcome(); loadSessions(); loadGraph(); };
$('#toggleSide').onclick = () => $('#side').classList.toggle('open');
function closeSideOnNarrow() { $('#side').classList.remove('open'); }

applyI18n();
renderActions();
setPanel(store.get('panel', innerWidth > 900 ? '1' : '0') === '1');
checkAuth().then(() => loadPeople()).catch((e) => { $('#messages').textContent = e.message; });
