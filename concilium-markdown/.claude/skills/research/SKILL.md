---
name: research
description: Deep research of the person's health picture in the medical literature — first find the contradictions, unexplained facts and non-obvious links in their own memory, turn them into sharp research questions built on this person's specifics, then research, verify and update hypotheses. Use when asked to "dig deeper", "research", "figure it out", "what could it be", "check this in the literature", or when new data arrived.
---

# Deep research

Generic research ("what causes fatigue") finds what every textbook says and changes nothing. Good research starts from this person: the facts that do not fit, the conflicts inside the plan, the links between body systems nobody connected. The questions are the product; the search only answers them.

## 1. Know the person

Read `MEMORY`, `PATIENT FACTS` and its facts topics, `HYPOTHESES` and the topics of every hypothesis, `TODO`, and `HISTORY` — especially earlier research rounds: what was already asked, answered, and left uncovered. Open documents when a number matters.

Write yourself a **patient profile** — everything that can change what applies to them:
- age, sex, height, weight / BMI, constraints on treatment (other conditions, medicines, allergies);
- each symptom with onset, course, and what makes it better or worse;
- measured values with units, dates and reference ranges (borderline values matter: "TSH at the top of the range");
- what was tried, the dose and duration, and how it went — including effects that faded or reversed;
- substances, medicines, habits, past illnesses, family history, findings from unrelated specialties;
- the current model: each hypothesis with its probability.

## 2. Choose the focus

If the request names a direction, use it. If not, and memory holds more than one large direction, **ask one short question and stop**: offer two to four directions from the current picture (the main complaint, the leading hypothesis, an unresolved conflict, a strong unexplained finding), with one line each on why it may matter. Research everything at once is too broad to change anything.

If memory is nearly empty, research is premature: say so and ask the person to tell their story instead.

## 3. Find what does not fit

Before writing any question, go through memory looking for:
- **contradictions in the plan** — a step in `TODO` that the literature might argue against ("the plan says rest the knee, but trials showed graded loading helps");
- **facts no hypothesis explains**, and facts that fit one hypothesis and argue against another;
- **weak links** — a key conclusion that rests on one test, a test known for false positives, a value measured by an unreliable method, something never measured;
- **bridges between branches** — a mechanism that would merge two hypotheses into one, or a single cause behind several complaints in different body systems;
- **non-obvious connections** — findings from other specialties (joints, gut, skin, sleep, teeth, mood, old substances or medicines) that the literature links to the main complaint;
- **things that changed** — an effect that faded, a treatment that made it worse, a symptom that came with another event;
- what the earlier rounds left uncovered.

## 4. Write the questions

Each question is answerable from the literature and anchored in this person's facts. For each:
- the question itself, with the person's numbers and details in it;
- which hypothesis it tests, and what an answer either way would change: probability, the order of the plan, a test to add or drop;
- ★ if the answer can change the model or the plan.

Group by hypothesis. Include the questions that could **refute** the leading hypothesis, not only confirm it.

## 5. Size the run

At most **5–7 questions per run**, the ★ ones first; the rest stay as "next round" in the leading hypothesis's topic. Say the scale before starting: how many questions, roughly how long. Do not launch large multi-agent research harnesses without the person agreeing to the cost — a run of a few dozen questions can spawn a hundred agents and still cover only a part of them.

## 6. Research

For each question (independent ones in parallel with a few subagents, two or three questions each, every subagent given the full patient profile and the current model):
- search clinical guidelines, systematic reviews, PubMed / PMC, specialty journals;
- for each claim: the source with a link, the **level of evidence** (RCT / cohort / case series / mechanism or animal / opinion), the population studied and **how well it applies to this person** (age, sex, severity, how they were selected);
- the verdict for the named hypothesis: **strengthens / weakens / does not change**;
- open the primary source for every claim that would move a probability or the plan; a claim found only in a summary is marked as such.

Then check the result as a panel:
- **Challenger** — which finding contradicts the leader, what anchoring is left?
- **Steward** — does a proposed test really separate named hypotheses, with different actions for each outcome?
- **Checker** — are facts quoted correctly, are sources real and on topic?

Nothing found is a result too: write "searched, no data" — it stops the next round from repeating the search.

## 7. Write it down

- Findings go into the topic of the hypothesis they concern, worked into the existing text, with sources. A new hypothesis that the research raised gets its own topic and a registry line marked "assumed probability".
- Move a probability only on real data, and say why. Keep apart **how well a model explains the facts** and **how strong the evidence for it is** — a model can fit well and still be unproven.
- New tests and steps go to `TODO` only with: which hypotheses they separate, what each outcome changes, why research cannot settle it.
- The questions left for the next round go to the leading topic, not to the chat.
- A dated `HISTORY` entry: the focus, how many questions, what was covered and what was not, what changed.

## 8. Tell the person

In their language, plain words, explaining every term once (what a test is, what a syndrome is):
- what was confirmed and what was refuted, strongest first;
- what this changes in the picture and the plan — or that it changes nothing;
- what found no data;
- what the next round could look at.

No file names, no hypothesis codes.
