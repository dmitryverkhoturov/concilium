---
name: hypotheses
description: Deeply research and update the model of what is going on with the person's health — generate rival hypotheses and validate them against the person's facts and the medical literature. Use when asked to "figure it out", "dig deeper", "what could it be", or when new data arrived.
---

# Research the hypotheses

This is the heaviest skill. Take time; depth beats speed. The goal is the best current model of what is going on, plus serious rivals — all backed by evidence, none hallucinated.

## 1. Know the person

Read MEMORY, PATIENT FACTS, HYPOTHESES, HISTORY, relevant topics and the documents they point to (PDFs included). List the key facts: symptoms with timeline, findings, results, what was tried and how it went.

## 2. Generate candidates

- Keep existing hypotheses; add new candidates the facts suggest.
- Always include the most common explanation and the dangerous can't-miss one.
- Look for one explanation that covers several problems at once, and say whether it holds.

## 3. Research each one

Use web search and fetch: clinical guidelines, systematic reviews, major reference sources, PubMed. When several hypotheses are independent, research them in parallel with subagents. For each hypothesis write down:
- what it explains and what it fails to explain in **this** person's facts;
- evidence for / against, each tied to a specific fact;
- sources with links for every medical claim;
- rough confidence (high / medium / low) and why;
- what single piece of information would most change it.

## 4. Panel review (MAI-DxO style)

Before writing, run through these roles yourself:
- **Hypothesis keeper** — is the ranking consistent with the evidence?
- **Challenger** — what is the anchoring bias here, which fact contradicts the leader, what is missing?
- **Steward** — is any proposed test really needed, or can research or a question settle it? Cut tests that don't change decisions.
- **Checker** — are facts quoted correctly, are sources real and on topic?

## 5. Write it down

- Details of each hypothesis (mechanism, evidence, sources, rivals, next steps) go into **its topic** in `topics/`. The leading hypothesis and every hypothesis you researched in depth must have a topic named after what it claims; create it if missing, and create a new one instead of stretching an old topic whose name no longer fits.
- HYPOTHESES keeps the short registry: per hypothesis the claim, likelihood, key for/against, one-line next step, and a `[[link]]` to its topic. Plus the integral model in a few lines.
- A dated HISTORY entry: what changed and why. Use `[[wikilinks]]` to related files and topics.

## 6. Tell the person

Five to ten plain lines: the most likely picture now, the main alternative, what changed since last time, and the one thing that would help most next. No file names, no hypothesis codes.
