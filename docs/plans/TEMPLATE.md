<!--
Copy this file to docs/plans/NNNN-slug.md, where NNNN is the next number
(check docs/plans/README.md's index for the last one used) and slug is a
short kebab-case name. Delete these HTML-comment instructions as you fill
each section in; keep the section headings themselves so every plan has the
same shape and an implementing agent always knows where to look for what.

Add the new file's row to docs/plans/README.md's index table when you create
it, and again whenever Status changes.
-->

# Plan NNNN: <title>

Status: Draft
<!--
One of: Draft (still being discussed) / Ready for implementation (grilled,
decisions settled, nobody's started) / In progress / Implemented / Abandoned
(with a one-line reason if so). Keep this line and the index table in sync —
whoever changes one should change the other in the same edit.
-->

## Why

<!--
The problem or goal in plain terms. If this changes or reverses something a
doc already states as true (CONTEXT.md, AGENTS.md, an ADR), say so explicitly
here and name the doc — that's exactly the kind of thing an implementing
agent won't think to go looking for on its own.
-->

## Settled decisions

<!--
Numbered list. This is the output of a planning/grilling session with the
project owner — each item is something a reasonable implementer could
otherwise guess differently, now pinned down so they don't have to guess.
Include brief reasoning inline where it isn't obvious, not just the verdict.
-->

## Current state

<!--
Facts about the code AS OF WRITING THIS PLAN — method names, file paths,
existing behavior the new work builds on or replaces. Get these from reading
the actual code, not from memory of how the app "probably" works. Flag this
section as something the implementing agent should re-verify, since it may
have drifted between planning and implementation.
-->

## Design

<!--
Broken into one subsection per file/module touched. Concrete enough to
remove ambiguity on the decisions above, but framed as a strong sketch the
implementing agent should verify against the real code rather than code to
paste in verbatim — file line numbers and exact current shapes drift.
-->

## Docs to update

<!--
Which existing docs (CONTEXT.md, AGENTS.md, ADRs, other plans) say something
that will stop being true once this ships, and what they should say instead.
-->

## Tests

<!--
What existing tests break or need to move, and what new coverage this needs.
-->

## Verification checklist

<!--
What "done" means, concretely — build, full test suite, and any live/manual
verification (e.g. via the qr-simple-ui skill's Playwright loop) needed
before this can be marked Implemented. This is the last thing the
implementing agent should check before flipping Status.
-->

## Log

<!--
Dated, append-only. Anyone who touches this plan — the planning session, the
implementing agent, a later fix — adds an entry here rather than editing the
sections above once they're settled. Keep entries short; this is a trail,
not a rewrite of the plan itself. Example:

2026-08-17: Plan drafted and grilled with project owner; decisions above are
final as of this date.
2026-08-18: Implementation started in the devcontainer. Hit <gotcha> —
<how it was resolved>.
2026-08-19: Implemented, verified live per the checklist above. Status ->
Implemented.
-->
