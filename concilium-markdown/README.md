# Concilium — markdown edition

A personal health agent as a plain folder of markdown files. No app: open this folder in an AI coding agent (Claude Code, Codex, or anything that reads `AGENTS.md`) and talk about your health.

The agent keeps a long-term memory in these files instead of one endless chat: facts apart from hypotheses, a ranked plan, a history of what was tried. Every conversation starts from what is already known.

It is not a doctor. It helps you think: separate facts from guesses, keep rival explanations, research before ordering tests, and prepare short, useful questions for a real doctor.

## Start

1. Copy this folder somewhere private — for example into your Obsidian vault. Keep one folder per person.
2. Open it in Claude Code (`claude` in the folder, or the Code tab of the Claude desktop app) or in Codex.
3. Say what bothers you, in any language. The agent fills `MEMORY`, `PATIENT FACTS` and the rest as you talk.
4. At the end say "remember this" (or "sleep"): the agent integrates the conversation into memory.

## Files

| File | What it holds |
|---|---|
| `MEMORY.md` | The gate: your goal, how the agent works here, the current picture, a map of topics |
| `PATIENT FACTS.md` | Only what you said or a document shows, each with a date and source |
| `HYPOTHESES.md` | Short registry of rival explanations with probabilities, each linked to its topic |
| `TODO.md` | The ranked plan and open questions |
| `HISTORY.md` | What changed and why, including what was ruled out |
| `topics/` | One file per hypothesis, working model or body area — the details |
| `documents/` | Your lab results, discharge notes, photos — kept untouched |
| `PRINCIPLES.md` | How the agent thinks about health: research first, rivals, red flags, safety |
| `AGENTS.md` | The contract: how any agent enters the folder and keeps memory |

## Skills

In `.claude/skills/`, picked up by Claude Code automatically:

- **sleep** — remember the conversation: integrate it into memory and keep memory small.
- **ask-questions** — the one to three questions that would most change the picture.
- **hypotheses** — deep research: rival hypotheses checked against your facts and the literature.
- **next-step** — the single best next step, by value of information, not by reflex.

Skills use the open `SKILL.md` format. For other agents, copy `.claude/skills/` to the place your agent reads skills from.

## Privacy

Everything stays in this folder on your computer. What leaves it is what the AI provider receives during a conversation.
