---
name: remember
description: Remember the conversation — integrate it into the health folder's memory and keep memory efficient. Run when a conversation ends, before a new one, when context is running out, or when the person says "remember this", "sleep", "save", "update memory", "we're done" (or the same in any other language). Runs the `sleep` skill with the health folder's rules on top and ends with a few plain lines for the person.
---

# Remember: sleep, with the health folder's rules

The core is the `sleep` skill, kept unchanged: read `../sleep/SKILL.md` (next to this skill's folder) and follow it. Below are the rules of a health folder. Where they differ from `sleep`, these win. "The user" in `sleep` is the person whose health this folder holds; "the session" is the material below.

## Material

- If the arguments contain conversation transcripts, those are the material; a transcript names the file of the full conversation if a detail (a tool result, a document's text) is needed.
- Otherwise use the current conversation.

## Where each thing goes

- **`PATIENT FACTS` holds facts only** — what the person said or a document shows, each with a date and a source ("said 2026-10-08", "blood test 12.09.25"). A guess is never a fact. When one body area grows large, its facts move to a facts topic and `PATIENT FACTS` links it.
- **`HYPOTHESES` is a short registry** — per hypothesis the claim, probability, key for/against, one-line next step, a link to its topic.
- **The leading hypothesis always has its own topic**, named after what it claims. A direction that grew out of a topic and now has another subject gets its own topic; never stretch an old topic over it.
- A confirmed diagnosis and its treatment are two entities: the treatment gets its own topic once it starts.
- An answer to an open question → the fact to facts, the question leaves `TODO`. New open questions → `TODO` under **Questions**, with which hypotheses they separate.
- New or weakened evidence → the hypothesis's topic and its line in `HYPOTHESES`. Do not re-research here; that is the `research` skill.
- The picture changed → `MEMORY` "Current picture" and its date.
- Always kept, besides what `sleep` keeps: results with numbers and units, done and declined tests or treatments with the reason, rejected hypotheses with the reason.

## Documents and links

- The person sees memory as a map drawn from Obsidian `[[links]]`. A memory file nobody links to is lost on the map.
- A document (PDF, photo, scan) stays where it is, untouched. Each fact taken from it and the topic whose numbers rest on it link it by its full file name with the extension: `[[2026-09-12 blood test.pdf]]`. A path in `code` is not a link.
- Check documents that are new since the last remember: each is linked; an unlinked one is opened, its facts checked against memory, and linked. Older documents are not reopened.

## Never

- Edit documents, human notes, `AGENTS.md` or `PRINCIPLES.md`.
- Turn a guess into a fact, or drop a fact because it does not fit the leading hypothesis.
- Ask or wait: this runs on its own, nobody answers.

## Output

First the `sleep` report, as `sleep` says. Then, as the very last thing, a block for the person:

<person>
One to three short plain lines in the person's language: what was noted and what changed in the picture ("Noted: pressure 130/85 on 30 October. The thyroid idea looks more likely now."). No file names, no technical words. If nothing new was worth keeping: "Nothing new to note."
</person>
