<p align="center"><img src="docs/icon.svg" width="112" alt=""></p>

<h1 align="center">Concilium</h1>

<p align="center"><b>A health companion that follows one person through time.</b><br>
You tell it what bothers you, in your own words. It remembers, thinks it over, and says the one next step worth taking.</p>

<p align="center"><img src="docs/welcome.png" alt="Concilium start screen: four buttons and, on the right, the map of the person's notes"></p>
<p align="center"><sub>Start screen. Four buttons show what it can do; on the right, the person's memory as a map: overview, facts, hypotheses, plans and one note per topic.</sub></p>

Health problems are rarely solved in one visit. They unfold over months: a test here, a new symptom there, a treatment that half works. Doctors see a snapshot; chatbots forget you the moment the tab closes. Concilium keeps the whole story.

- **It remembers.** Every conversation is filed into a long-term memory: facts with dates and sources, results with numbers, what was tried and how it went. Come back in three weeks and it picks up where you left off.
- **It keeps rival explanations.** Not one diagnosis but a short list of hypotheses with what speaks for and against each, updated as new facts arrive. A fact that does not fit is kept, not dropped.
- **It gives one next step, not a list of tests.** A test is suggested only when it is clear which explanations it separates and what happens after each result. Red flags come first, always.
- **It is made for your parents and grandparents.** A window, a text box and four buttons: *What should I do next*, *Ask me questions*, *Dig deeper*, *Remember this talk*. No files, no terminal, no setup beyond signing in.
- **It stays on your computer.** Each person is a plain folder of markdown notes on their own machine. It runs on top of Claude Code with your own Claude account.

It is not a doctor and does not replace one. It helps you understand what is going on and arrive at the doctor with the right questions.

<p align="center"><img src="docs/first-talk.png" alt="First conversation: the agent reads the person's blood test and asks why the tablets are not working; on the right, a note on the leading hypothesis with its results"></p>
<p align="center"><sub>First conversation. Before answering, it reads the blood test she shared and quotes it: anaemia, very low ferritin, raised liver enzymes. Instead of just "ask for infusions" it asks why six months of tablets did nothing. On the right, the note on the leading hypothesis, with the results it rests on.</sub></p>

<table>
<tr>
<td width="50%"><img src="docs/conversation.png" alt="Three weeks later: she reports the diet and the infusion; the agent remembers everything and answers in plain words"></td>
<td width="50%"><img src="docs/next-step.png" alt="'What should I do next': one main step, why it helps, and when a blood test would be worth it"></td>
</tr>
<tr>
<td>Three weeks later. She writes that she is on the diet and had the infusion but is still tired. It remembers the whole story and explains what is normal and what is worth checking. A new topic appears on the map.</td>
<td><i>What should I do next</i>: one step, why it helps, and when a test would actually change something.</td>
</tr>
</table>

The person in the screenshots, Anna, is fictional: one of the simulated patients in [`eval/`](eval/), followed over two conversations three weeks apart.

## Two ways to use it

- **The app** — a window with a chat, four buttons and a map of your notes. Made for people who should never see a file name or a terminal.
- **[concilium-markdown](concilium-markdown/)** — the same agent as a folder of markdown files and skills, for people who already use Claude Code or Codex. No app needed.

Both share the same memory layout, principles and skills: the app is a shell around `concilium-markdown`.

## Repository

```
windows/              the Windows app: native WPF (.NET 8), talks to Claude Code directly
app/                  the older Electron app: local server over the Claude Agent SDK, web UI
concilium-markdown/   the folder template: AGENTS, MEMORY skeleton, principles, skills
eval/                 simulated patients with a hidden diagnosis, played by a second agent
docs/                 icon and screenshots
```

## Windows app

Native WPF on .NET 8. It runs the Claude Code binary in stream-json mode — no Node, no local server — and starts it ahead of time, so the first answer does not wait for Claude to boot. Conversations are Claude Code's own (`~/.claude/projects/...`).

```bash
cd windows
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o ../dist/Concilium
```

The published folder holds `Concilium.exe`, the `concilium-markdown` template and `claude/claude.exe` (taken from `app/node_modules` of the Agent SDK). App data goes to `Concilium data` next to the exe; run from `bin/` it goes to `%APPDATA%\Concilium`. `CONCILIUM_SHOT=<png>` saves a picture of the window and quits (smoke test, README images); it runs beside an open app window. With it: `CONCILIUM_DATA=<folder>` uses another list of people, `CONCILIUM_SHOT_SEND="message || /next-step"` sends messages first (`@last` opens the latest conversation), `CONCILIUM_SHOT_SCROLL=top` shows a conversation from its start, `CONCILIUM_SHOT_NOTE=<note>` opens a note on the right. The app speaks the Windows language (English if it has no strings for it); `CONCILIUM_LANG=en` overrides it.

## Run the older Electron app from source

Requires Node.js 20+ and a Claude account.

```bash
cd app
npm install
npm start
```

Then open http://localhost:4747. On Windows, `app/Concilium.cmd` starts the server hidden and opens an app window.

App data (the list of people and their folders) is stored next to the program in `data/`; when run from a source checkout it goes to the user's app-data folder (`%APPDATA%\Concilium`, `~/Library/Application Support/Concilium`, `~/.config/Concilium`), so nobody's data ends up in the repository.

## License

MIT
