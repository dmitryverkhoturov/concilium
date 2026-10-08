---
name: update-memory
description: Consolidate the health folder's memory after conversations ("dream") — extract facts, update hypotheses, questions, TODO, history and branches, compact and reorganize. Use after a conversation ends, when asked to "update memory", "remember this", "dream", or to tidy the notes.
---

# Update memory ("dream")

You maintain the long-term memory of one person's health folder. The person never sees this work; afterwards you give them a two- to four-line summary in plain words, in their language.

## Input

- If the arguments contain conversation transcripts, those are the material.
- Otherwise use the current conversation.

## Steps

1. Read `AGENTS.md`, then MEMORY, then the files it points to (PATIENT FACTS, HYPOTHESES, QUESTIONS, TODO, HISTORY, branches). File names may have a project prefix. Learn the existing layout before changing anything.
2. From the material, pull out what must survive:
   - **facts** — only what the person said or a document shows. Keep the date and the source ("said on 2026-10-08", "blood test 12.09.25"). Never turn a guess into a fact;
   - answers to open questions — close the question, move the answer into facts;
   - new or weakened evidence for a hypothesis;
   - decisions, done or declined actions;
   - new open questions, next steps, risks.
3. Write each item to its home:
   - PATIENT FACTS — facts;
   - HYPOTHESES — evidence changes and status (do not re-research here; that is the `hypotheses` skill);
   - QUESTIONS — open questions only; answered ones leave;
   - TODO — future only; done items leave, and if they mattered they go to HISTORY;
   - HISTORY — a dated entry: what changed and why;
   - `branches/` — a topic with its own context (its own facts, rivals, sources, steps) that overloads MEMORY gets its own branch file, named like the existing ones (`<prefix> <topic>.md`). Update existing branches first; create a new one only when the topic clearly stands alone;
   - MEMORY — only the current compact model and the map of files and branches.
4. Compact. Merge duplicates, drop noise, move detail out of MEMORY into branches or domain files, keep MEMORY short. Do not lose anything important: compress it into HISTORY instead of deleting.
5. Link with Obsidian wikilinks (`[[<file name without .md>]]`) whenever a file mentions another file or branch — the app draws its graph from these links.
6. Never edit human documents (anything that is not a memory file or a branch) and never delete files.

## Output

Two to four plain lines for the person: what you noted and anything that changed in the picture. No file names.
