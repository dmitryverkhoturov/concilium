# AGENTS.md

## Purpose of this file

`AGENTS.md` is the portable contract for how an agent works with a project folder.

It defines:
- how the agent enters the project;
- how it tells whether the project is initialized;
- which memory files are required;
- how memory is kept up to date and within size budgets;
- what the agent may and may not edit.

`AGENTS.md` must not contain the name, domain or identity of a specific project. Those live in `MEMORY`.

## Mandatory startup

At the start of every new conversation, before answering anything on substance:
1. Find `MEMORY.md` or a `* MEMORY.md` file in the current folder.
2. If it exists, read it from disk with a tool, then continue with the reading order below.

Having `AGENTS.md` in context does not mean `MEMORY` has been read. If the current conversation contains no tool result from reading that file, read it now — even before a trivial question such as arithmetic, a greeting or a general question.

## Core principle

If this file has been copied into a new folder, the agent must not assume it already knows the project.

First it must:
- look through the folder;
- find the main human document or the nearest entry point;
- understand what the project is about;
- build a local memory around the actual contents of the folder.

## Memory limits

- The context of the current session is not durable project memory.
- The next model or session may not remember facts, decisions and agreements from the current conversation.
- Durable memory is the project files and other materials that were explicitly read.
- Everything that must survive a context reset is written to memory files in the current work cycle.
- If the conversation changes or refines the saved model of the project, the agent updates the relevant memory file itself.

## Memory files

Core roles:
- `MEMORY` — compact durable memory of the project and the map of topics; it opens with the project's goal and how success is judged;
- `HISTORY` — important completed changes, decisions and resolved forks;
- `TODO` — future actions, open questions and risks;
- `topics/` — detailed memory of individual lines of work.

Default file names:
- `MEMORY.md`;
- `HISTORY.md`;
- `TODO.md`.

Allowed variant with the project name:
- `<Project name> MEMORY.md`;
- `<Project name> HISTORY.md`;
- `<Project name> TODO.md`.

By default the agent creates the short names. The project-name variant is used if the user asked for it or it is already established in the project's memory. If the project-name prefix is used, domain extensions get it too.

Domain extensions are allowed when they are actually needed:
- `IDENTITY.md` — agent role, tone, answer format, stable behaviour rules;
- `HYPOTHESES.md`, `MESSAGES.md`;
- other local files for entities, sources, processes or cases.

Memory files are written in the language the user works in with this project, not in the language of this contract.

## Size budgets

Memory exists to give the next session what it needs at the lowest context cost. Every file has a budget in characters (roughly 4 characters ≈ 1 token in English; non-Latin scripts usually take more tokens per character):

| File | Budget | Read |
|---|---|---|
| `MEMORY` | 20,000 | every session |
| `TODO` | 8,000 | every session |
| `HISTORY` | 15,000 | on demand |
| topic or domain file | 15,000 | when the task touches it |

A project may override these numbers in its `MEMORY`. A budget is a ceiling, not a target.

When a file goes over budget, it is rebalanced, not truncated:
- `MEMORY` — details of a theme move to its topic; `MEMORY` keeps one line and a link.
- `TODO` — items of one line of work move to that topic's next steps; `TODO` keeps the ranked next steps, cross-cutting items and one pointer line per active topic.
- `HISTORY` — older entries are condensed into one line per stage; rejected options keep their reason.
- topic — condensed first: the log folds into the current state, verbatim sources move to their own file (raw sources have no budget). Split only by meaning, when it really holds several entities, never by size; the parent becomes a hub with the current summary and links.

Rebalancing must not lose decisions, rejected options with reasons, dead ends or untested ideas — they get shorter, not dropped.

## Topics

A topic is the detailed memory of one line of work: its context, current state, options, dead ends, sources and next steps. It is needed when a line of work is too detailed for `MEMORY` and too big for one `TODO` item.

Topics form a tree with cross-links:
- `MEMORY` holds the topic map: one line per topic saying what it covers and when to read it.
- A hub topic holds the summary of its area and the same kind of map for its child topics.
- Links are few and purposeful: a topic links to its parent and to the topics a task in it will need, and says why to go there. No boilerplate links to `MEMORY`, `HISTORY`, `TODO`.
- Each fact lives in exactly one place — the most specific node that covers it. Other places link to it.
- A topic describes the current state, not a log. How the state was reached takes one or two lines.
- Keep the tree shallow: `MEMORY` → topic → child topic is usually enough.

The agent must always be able to get from `MEMORY` to the information a task needs by following links, reading only what the task needs.

Finished topics are condensed into `HISTORY`; their open actions go to `TODO`; the map marks them closed. A topic whose whole content was merged or closed elsewhere is deleted and its links fixed; other topic files are not deleted without the user's permission.

## TODO is ranked by the goal

`TODO` is not a queue of whatever was left over from the last session. It is ordered by how much each step moves the project toward the goal stated in `MEMORY`.

Structure:
- **Next** — at most five steps, ranked. Each has one line on why it moves the goal.
- **Backlog** — the rest, grouped by topic; detailed sub-steps live in the topic, not here.
- **Questions and risks.**

How to rank: impact on the goal first; then whether the step unblocks other work; then a cheap check that can confirm or kill a whole direction early; then cost. Recency and "we already started it" are not reasons to rank a step higher.

One action easily spawns five follow-ups. New sub-steps are ranked against the whole list, not put on top automatically; most go to the topic's backlog. Before continuing a chain of sub-steps, check that it is still the most useful path to the goal and not a drift deeper into a side subject.

Done, outdated and duplicate items are removed; important ones are condensed into `HISTORY`.

## If the project is not initialized yet

Signs:
- `AGENTS.md` exists but there is no clear memory structure;
- there is no `MEMORY` / `HISTORY` / `TODO`, or their roles are unclear;
- there are only human notes, raw material or scattered files.

The agent must:
1. Look through the folder.
2. Find the main human document or the nearest entry file.
3. Not rewrite human documents without an explicit request.
4. Choose memory file names: short by default, or with the project name if the user asked for it.
5. Create a minimal memory structure.
6. Write to `MEMORY` what the project is, its goal, the main files and the visible entities.
7. Write to `HISTORY` that the project was initialized or rethought.
8. Write to `TODO` the remaining questions, risks and next steps.

## If the project is already initialized

Signs:
- `MEMORY` exists;
- the roles of the local memory files are clear;
- the project already has recorded memory.

Reading order:
1. `AGENTS.md`
2. `MEMORY`
3. `IDENTITY`, if present
4. `TODO`
5. the topics from the `MEMORY` map that the task touches
6. the main human document named in `MEMORY`, if the task needs it

`HISTORY`, domain files, sources and other materials are read when the task needs them — for example, when it touches past decisions or a specific entity.

## Updating memory

The agent compares the current conversation and the results of its work with the saved model of the project.

Memory must be updated when there is:
- a new stable rule;
- a new decision or architecture change;
- a new fact that changes the understanding of the project;
- an important clarification from the user;
- a mismatch between what the user says and what memory says;
- a resolved fork that must not simply be forgotten;
- a new risk, open question or next step.

Where to write:
- `MEMORY` — the current model of the project, stable rules, structure, key conclusions, topic map;
- `HISTORY` — changes that already happened, decisions, completed stages, condensed outcomes of forks;
- `TODO` — only the future: next steps, questions, risks and gaps;
- `topics/` — details of active lines of work;
- domain file — if the subject already has its own local memory.

If a `TODO` item is resolved, it must not just disappear. Noise can be deleted, but an important line of work is condensed into `HISTORY` and, if it changes the current model, reflected in `MEMORY`.

## Work cycle

After each substantive action the agent checks:
- whether the stable understanding of the project changed;
- whether a new decision, rule, fork or risk appeared;
- whether a new next step appeared, and where it ranks against the existing ones;
- whether `MEMORY`, `HISTORY`, `TODO`, `topics/` or a domain file must be updated right now;
- whether an updated file went over its budget and needs rebalancing.

When choosing what to do next — or what to suggest to the user — take the top of the ranked `TODO`, not the last unfinished sub-step.

Updating memory is part of the work, not a closing ritual. If the user gives an important correction about how the project works, the agent records it in the same cycle.

If the task belongs to a topic, the agent updates that topic first and carries only a condensed conclusion or a link into `MEMORY`.

## Write policy

- The agent writes freely to the project's local memory files.
- Human documents are not edited without an explicit request.
- Before changing a human document, the agent makes sure the task really requires it.
- Files are not deleted without the user's direct permission, except a topic emptied by a merge or close.
- Domain specifics live in `MEMORY`, `IDENTITY`, `topics/` or local domain files, not in `AGENTS.md`.

## Maintaining this file

`AGENTS.md` stays a short portable contract.

Change it only when the general protocol of agent work changes.

## Writing style

- No generic chatbot style.
- No sterile, marketing or overly smooth text.
- Write directly, densely and to the point.
- If a thought can be said more simply, say it more simply.
