---
name: sleep
description: Remember the conversation — integrate it into the health folder's memory and keep memory efficient. Run when a conversation ends, before a new one, when context is running out, or when the person says "remember this", "sleep", "save", "update memory", "we're done" (or the same in any other language). Works new facts, hypothesis changes and decisions into PATIENT FACTS / HYPOTHESES / topics / TODO / HISTORY / MEMORY, compacts what no longer matters, and keeps the topic tree small and easy to walk.
---

# Sleep: integrate, compact, rebalance

Goal: the next conversation starts from the same place and knows what you know, at the lowest context cost. The person should never have to repeat themselves or check what was lost.

Sleep is not a filing step. It integrates what the conversation brought into what memory already holds, forgets what no longer matters, and reorganizes so memory stays small.

## Input

- If the arguments contain conversation transcripts, those are the material.
- Otherwise use the current conversation.

## Good memory looks like this

- **`MEMORY` is the gate.** The person's goal at the top, how the agent works here, the current picture (complaints, working model, plan), a map of topics. Narrow topics are reached through their hub.
- **`PATIENT FACTS` holds facts only** — what the person said or a document shows, each with a date and a source ("said 2026-10-08", "blood test 12.09.25"). A guess is never a fact.
- **`HYPOTHESES` is a short registry** — per hypothesis the claim, probability, key for/against, one-line next step, a link to its topic. Details live in the topic.
- **One topic, one entity.** A hypothesis, a working model, one body area's facts, a doctor, a treatment trial. A confirmed diagnosis and its treatment are two entities: the treatment (a diet, a medicine, a course of infusions) gets its own topic once it starts. The file holds its current state: facts, mechanism, sources, rivals, next step. How the state was reached takes one or two lines.
- **The leading hypothesis always has its own topic**, named after what it claims. So does every hypothesis or model the conversation spent real time on.
- **Files stay within budget.** In characters: `MEMORY` 20,000 · `TODO` 8,000 · `HISTORY` 15,000 · topic 15,000. Documents and verbatim sources live in their own files with no budget; memory holds the digest and a link.
- **Memory is one connected web.** The person sees it as a map drawn from Obsidian wikilinks, so a file nobody links to is lost, and a memory file named in plain text or `code` is a missed link. Each `HYPOTHESES` entry links its topic; each topic links its hub, the facts it rests on (`PATIENT FACTS` or a facts topic), its rivals and the documents behind its numbers; each `TODO` step and question links the topic it tests; each `HISTORY` line links the topic it changed; `MEMORY` links every memory file and every open topic. Links sit inside the sentence ("the rash is in [[facts — skin]]"), not as a list of "see also" tails. No link to a file that does not exist. A document is linked by its full file name with the extension — `[[2026-09-12 blood test.pdf]]`; a date, a description or a path in `code` is not a link, and the document hangs loose on the map.
- **One fact, one place** — the most specific node where a task needs it. Others link to it.
- **Memory is what the person said and decided.** Their corrections in their meaning; your conclusions marked as hypotheses; no filler.

## What is kept, what is forgotten

For each line ask: if it were deleted, would a future conversation give different advice or repeat a mistake? If not, it goes. Always kept: facts with dates, results with numbers and units, decisions, done and declined steps with the reason, rejected hypotheses with the reason, untested ideas (marked "untested"), the person's corrections. A topic that absorbed new material stays about the same size: room comes from condensing older content, not from spreading text into new files.

Something the person repeated, or "I already told you", is a failure signal: record it where it will be read first.

## Where each thing goes

- facts → `PATIENT FACTS` or its facts topic;
- an answer to an open question → the fact goes to facts, the question leaves `TODO`;
- new or weakened evidence → the hypothesis's topic, and its line in `HYPOTHESES` (probability, key for/against). Do not re-research here; that is the `research` skill;
- decisions, done or declined steps → `HISTORY` in a dated line; the future → `TODO`;
- new open questions → `TODO` under **Questions**, with which hypotheses they separate;
- the picture changed → `MEMORY` "Current picture" and its date;
- a document the person shared → it stays where it is (`documents/` for new ones), untouched; each fact taken from it links the file, and so does the topic whose numbers rest on it.

## Operations

A toolbox, not a checklist: compare memory with the picture above and apply what it needs now.

- **Integrate** — work new material into the existing text of the right file; replace what it corrects everywhere; no chronicle appended at the end.
- **Compact** — fold logs into the current state, drop superseded numbers and repeated reasoning. Before compacting, note the file's decisions, numbers and rejected options; afterwards check each survived.
- **Create** — a topic for a new entity. Creating a topic is cheap; a missing topic means the direction gets smeared across `HYPOTHESES` and forgotten. Check no topic covers it first.
- **Split** — only by meaning, when one file holds several entities. The parent becomes a hub.
- **Merge** — topics that are one entity, or a child always read together with its parent.
- **New home** — a direction that grew out of a topic and now has a different subject (another main complaint, another mechanism) gets a new topic; the old one keeps its scope and links to the new one. Never stretch an old topic over a new direction.
- **Close** — a dropped hypothesis or finished line goes to `HISTORY` in a line or two with the reason, its open actions to `TODO`, its map line marked closed.
- **Prune links** and **rename** so names are clear without context; fix the links.

## Procedure

1. **Enter.** Read `AGENTS.md` and `MEMORY` if not read yet; look at `topics/`, `TODO`, the latest `HISTORY`. File names may carry a prefix (`Mom MEMORY.md`). Measure sizes.
2. **Collect.** From the material: new or changed facts, evidence for or against hypotheses, new hypotheses, decisions, done and declined steps, the person's corrections, new questions, next steps.
   Deep mode (the person said "deep sleep"): also read the person's messages from past conversations after the last sleep date in `HISTORY` (Claude Code: `~/.claude/projects/<folder>/*.jsonl`); record the sleep date in `HISTORY`.
3. **Integrate** each item to its place (above). Compact every touched file.
4. **Topic check.** List every hypothesis, model or subject the conversation spent real time on. For each: which topic holds it, and does its name say what it is? No topic or a name that does not fit → create one now. Is the leading hypothesis in `HYPOTHESES` a short entry with a link, or a wall of detail? Move the detail into its topic.
5. **Re-rank `TODO`** against the goal in `MEMORY`: remove done and outdated items (important ones go to `HISTORY` in a line), at most five in **Next**, each with why.
6. **Check.** Old wording of corrected facts is gone. Every memory file and topic is linked from at least one other and links onward as described above; every link resolves to an existing file. Every document in the folder (PDF, photo, scan — any file that is not `.md`, outside `.claude/`) is linked from the fact or topic that uses it; an unlinked one is opened, its facts checked against memory, and linked now. Files are within budget. A new conversation reading only `MEMORY` and one topic can continue without asking the person.

Sleep is not interactive: never ask or wait. When unclear, choose the reasonable option, mark it "assumed", and add it to **Questions** in `TODO`.

## Output

Two to four plain lines for the person, in their language: what you noted and what changed in the picture. If you started a new topic, say it in plain words ("I started a separate note about …"). No file names.

## Never

- Delete files, except a topic whose whole content was merged elsewhere (fix the links).
- Edit documents, human notes, `AGENTS.md` or `PRINCIPLES.md`.
- Turn a guess into a fact, or drop a fact because it does not fit the leading hypothesis.
- Write to memory if the person said "test" or "don't record this".
