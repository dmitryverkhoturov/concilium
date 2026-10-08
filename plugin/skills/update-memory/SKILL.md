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
   - `branches/` — see "Branches" below;
   - MEMORY — only the current compact model and the map of files and branches.
4. Compact. Merge duplicates, drop noise, move detail out of MEMORY and HYPOTHESES into branches, keep both short. Do not lose anything important: compress it into HISTORY instead of deleting.

## Branches

A branch is the home of one direction of thought: its facts, mechanism, sources, rivals, next steps. Creating a branch is cheap; a missing branch means the direction gets smeared across HYPOTHESES and forgotten. When in doubt, create.

Rules:
- **The leading hypothesis always has its own branch**, named after what it claims (e.g. `<prefix> iron deficiency without anemia — fatigue and hair loss.md`), not after an old diagnosis label. Same for any hypothesis or working model that the conversation discussed in depth.
- **A branch whose name no longer describes its content is the wrong home.** If a direction grew out of an existing branch and now has a different subject (other main complaint, other mechanism), create a new branch for it and leave the old branch its original scope with a link to the new one. Never stretch an old branch over a new direction.
- HYPOTHESES keeps a short entry per hypothesis (claim, likelihood, the key for/against, one-line next step) and a `[[link]]` to its branch. Details live in the branch, not in both places.
- Name branches like the existing ones (`<prefix> <topic>.md`, in the person's language) and link them from MEMORY's branch map.

Before finishing, run this check and fix what fails:
1. List every topic, hypothesis or model the conversation spent real time on.
2. For each: which branch holds it? Does that branch's name say what the topic is?
3. No branch, or the name does not fit → create one now.
4. Is the leading hypothesis in HYPOTHESES a short entry with a link, or a wall of detail? Move the detail into its branch.
5. Link with Obsidian wikilinks (`[[<file name without .md>]]`) whenever a file mentions another file or branch — the app draws its graph from these links.
6. Never edit human documents (anything that is not a memory file or a branch) and never delete files.

## Output

Two to four plain lines for the person: what you noted and anything that changed in the picture. If you started a new topic, say so in plain words ("I started a separate note about …"). No file names.
