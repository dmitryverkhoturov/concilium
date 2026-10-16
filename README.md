# Concilium

A local health research agent for the whole family. You talk about what bothers you; it keeps a long-term memory of your facts, rival hypotheses and plan, researches before it suggests tests, and tells you the one best next step.

It runs on your computer on top of Claude Code. Each person's data is a plain folder of markdown files that stays on their machine.

It is not a doctor and does not replace one.

## Two ways to use it

- **The app** — a window with a chat, four buttons and a map of your notes. Made for people who should never see a file name or a terminal.
- **[concilium-markdown](concilium-markdown/)** — the same agent as a folder of markdown files and skills, for people who already use Claude Code or Codex. No app needed.

Both share the same memory layout, principles and skills: the app is a shell around `concilium-markdown`.

## Repository

```
app/                  the desktop app: local server over the Claude Agent SDK, web UI
concilium-markdown/   the folder template: AGENTS, MEMORY skeleton, principles, skills
```

## Run the app from source

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
