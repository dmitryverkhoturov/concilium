---
name: sleep
description: Project sleep — integrate the session into memory and keep memory efficient. Run at the end of a session, before starting a parallel session, when context is running out, or when the user says "sleep", "wrap up", "save state", "update memory and topics", "reorganize memory", "we're done" (or the same in any other language). Works new material into the existing logic of MEMORY / topics / HISTORY / TODO, compacts and forgets what no longer matters, and gradually rebalances the topic tree (merge, split, move, close, prune links) so every file stays within budget and the needed fact is a few obvious steps away. Works in projects with AGENTS.md and memory files.
---

# Sleep: integrate, compact, rebalance

Goal: the next session starts from the same place and knows what you know, at the lowest context cost. The user should not have to explain where to put things or check what was lost.

Sleep is not a filing step. It integrates what the session brought into what memory already holds, forgets what no longer matters, and reorganizes so memory stays small and easy to walk.

## Good memory looks like this

Judge every sleep against this picture:
- **`MEMORY` is the gate.** Goal and success criteria at the top, hard rules, canon, a map of the main areas. Narrow topics are reached through their hub, not listed in `MEMORY` one by one.
- **One topic, one entity.** A file holds the current state of one thing: context, state, options, dead ends, sources, next step. How the state was reached takes one or two lines.
- **Files stay within budget.** In characters: `MEMORY` 20,000 · `TODO` 8,000 · `HISTORY` 15,000 · topic 15,000. Raw sources (transcripts, articles, quotes, tables) live verbatim in their own files with no budget; the topic holds the digest and links to them.
- **Links are few and purposeful.** A link exists only where a task in this file will need to follow it, and says what is there and why to go. No boilerplate "related: MEMORY, HISTORY, TODO".
- **Few steps, few links.** Too few links means long walks to a fact; too many turns the graph into noise. The balance comes from where topics sit in the tree, not from linking "just in case". The tree is shallow: `MEMORY` → topic → child.
- **One fact, one place** — the most specific node where a task needs it. Others link to it.
- **Memory is what the user thinks.** The user's decisions, corrections and requirements in their meaning; your own conclusions marked as hypotheses; no rules the user did not approve; no filler.

## What is kept, what is forgotten

Sessions pile notes on top of each other until a file is a log with no conclusions. For each line ask: if it were deleted, would a future session make a different decision or repeat a mistake? If not, it goes. Always kept: decisions, final numbers (units, window, date), rejected options with the reason, dead ends, untested ideas (marked "untested"), the user's corrections. Before calling something stale, verify it (code, data, date). A topic that absorbed new material stays about the same size: room comes from condensing older, less important content, not from spreading text into new files.

A requirement the user repeated, or "I already told you", is a failure signal: it becomes a rule with the reason, at the top of `MEMORY` or in its topic.

## Operations

A toolbox, not a checklist: compare memory with the picture above and apply only what it needs now.

- **Integrate** — work new material into the existing text of the right file; replace what it corrects everywhere; no chronicle appended at the end.
- **Compact** — fold logs into the current state, drop superseded numbers and repeated reasoning. Before compacting, note the file's decisions, final numbers and rejected options; afterwards check each survived.
- **Extract source** — move verbatim material into a source file and keep the digest.
- **Create** — only for a new logical entity with its own logic, or a genuinely large separate part of an existing one. Check no topic covers it first. Never invent an entity to fit a budget.
- **Split** — only by meaning, when one file really holds several entities. The parent becomes a hub: summary, canon, map of children.
- **Merge** — topics that are one entity, and children always read together with their parent. Different entities are not merged just because they sit next to each other.
- **Move** — put a topic under the parent where it is closest in meaning, so `MEMORY` and hubs point to fewer, better places.
- **Close** — a finished line goes to `HISTORY` in a line or two, its open actions to `TODO`, its map line marked closed.
- **Prune links** — remove links nobody would follow for a task here; add a missing one where a task gets stuck.
- **Rename** — a name clear without context, distinct from neighbours, in the project's style; fix the links.

Canon is separate from research: a current best pipeline, algorithm or spec is its own entry-point topic, research links to it.

## Signals

- Two topics answer the same questions → merge.
- `MEMORY` lists several topics of one area → hub, move them under it.
- Many files link to a topic for the same single fact → move the fact to where it is needed.
- A topic nobody opens on its own, always read with its parent → merge into the parent.
- A topic untouched for long and needed by no `TODO` item → close.
- A file over budget → compact, extract sources; split only if it holds several entities.
- A file reads like a dated log → rewrite as current state.
- Links to `MEMORY` / `HISTORY` / `TODO` or to half the project from one file → prune.

## Procedure

1. **Enter.** Read `AGENTS.md` and `MEMORY` if not read in this session; look at `topics/`, `TODO`, latest `HISTORY`. Measure sizes: `python -c "import sys; [print(len(open(f, encoding='utf-8').read()), f) for f in sys.argv[1:]]" *.md topics/*.md`
2. **Collect.** From the session: what is new or changed against current memory — decisions and rejected options with reasons, corrections in the user's wording, stressed requirements, results with source and date, new entities, what turned out wrong, open questions, next steps.
   Deep mode (the user said "sleep" or "deep sleep"): also read user messages from past sessions after the last sleep date in `HISTORY` (Claude Code: `~/.claude/projects/<project folder>/*.jsonl`); record the sleep date in `HISTORY`.
3. **Integrate.** Each item to its place: `MEMORY` (thin gate), topic (details of one entity), `HISTORY` (closed decisions and stages, briefly), `TODO` (only the future), or nowhere. Integrate and compact every touched file and its neighbours by meaning. If the subject belongs to another project (`../<project>/`), write into its topic and link from here.
4. **Rebalance.** Look for the signals in the files this session touched and their neighbours. Elsewhere, if you notice signals, apply one or two operations per sleep and put the rest in `TODO`: the tree improves gradually, not in one sweep. If the user asked to reorganize, go wider, still in steps.
5. **Re-rank `TODO`.** Re-read the goal in `MEMORY` (if missing, write the one the work implies, marked "unconfirmed"). Remove done, outdated and duplicate items; important ones go to `HISTORY` in a line. Rank by impact on the goal, then unblocking other work, then a cheap check that can confirm or kill a direction, then cost; recency and "already started" are not reasons. **Next**: at most five steps, each with one line on why. The rest: **Backlog** by topic, or inside the topic. If the top is a deep sub-step of one line while another would move the goal more, say so.
6. **Check.** Old wording of corrected facts is gone. Links to created, merged, moved and renamed topics resolve. Every file is within budget or the report says why. A new session reading only `MEMORY` and the topic its task needs can continue without asking the user.
7. **Report**, short, in the user's language: what was integrated where; topics created, merged, split, moved, closed; what was forgotten or condensed; new top of **Next**; budget overruns; what remains open and which rebalancing was left for later.

Sleep is not interactive: never ask or wait. When unclear, choose the reasonable option, mark it "assumed" where written, add it to open questions in `TODO`, mention it in the report.

## Never
- Delete files without the user's permission, except a topic whose whole content was merged or closed elsewhere (delete it and fix the links). Raw sources and human documents are never deleted.
- Edit human documents or `AGENTS.md` without an explicit request.
- Change entries of another session or agent when their origin is unclear.
- Create files or mechanisms nobody will read.
- Put code, passwords or keys into a notes vault.
- Write to memory if the user said "test" or "don't record anything".
