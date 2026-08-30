# Plans

This folder holds implementation handoff docs for nontrivial pieces of `qr-simple` work — the kind worth pinning down with the project owner *before* an agent starts writing code, especially anything that reverses or extends a decision already documented elsewhere (`CONTEXT.md`, `AGENTS.md`, `docs/adr/`).

**How this differs from `docs/adr/`**: an ADR records *why* a specific trade-off was decided, permanently, and doesn't track execution. A plan here is a *scoped, executable spec* for a chunk of work — decisions plus enough concrete design to hand to an agent, plus a running log of what actually happened while building it. A plan may reference or produce an ADR-worthy decision; it isn't a replacement for one.

## Workflow

1. **Plan.** Work through the design with the project owner in a planning/grilling session (the `/grilling` skill is the usual tool for this) until every real decision point is pinned down, not guessed at.
2. **Write it up.** Copy `TEMPLATE.md` to `NNNN-slug.md` (next number below; short kebab-case slug). Fill in *Why*, *Settled decisions*, *Current state* (verified against the actual code, not memory), and a *Design* sketch broken down by file/module. Add a row to the index table below with Status `Draft` or `Ready for implementation`.
3. **Hand off.** Give an agent a copy-paste prompt that points at the plan file and tells it to work inside the devcontainer (see `AGENTS.md`'s "Running tests" section for why). A reusable prompt skeleton is at the bottom of this file — fill in the bracketed parts.
4. **Implement.** The implementing agent reads the plan, treats the Design section as a strong sketch (verify against real code, which may have drifted since planning), executes the Settled decisions exactly, and appends dated entries to the plan's own **Log** section as it goes — especially anything that didn't match the plan's assumptions. It flips `Status` to `Implemented` (or back to something else if it stalls) only once the plan's own Verification checklist actually passes, and updates this file's index row to match.
5. **Leave it.** A finished plan stays in this folder as history — don't delete it once implemented. If later work changes what it describes, that's a new plan (or a Log entry here) referencing this one, not an edit to the old decisions.

Anyone — planning session or implementing agent — should feel free to leave notes in a plan's Log section; it's the place for "here's what actually happened," not just "here's what we decided."

## Index

| # | Title | Status | Summary |
|---|-------|--------|---------|
| [0001](0001-document-file-upload.md) | Photo/document file upload for Equipment | Implemented | Switched Equipment photo/document links from URL-only to real file upload, bytes stored in Postgres, served via `GET /documents/{id}/content`. |
| [0002](0002-inspection-records.md) | Periodic inspection records for Equipment | Implemented | New `Inspection` entity (dated, noted, attributed PDF) uploaded per Equipment by Admin/Operator, browsable publicly from the scan page at `/e/{id}/inspections` with a 6-month recent window and older records collapsed. |

## Handoff prompt skeleton

Fill in the bracketed parts and paste into a fresh agent session running inside the devcontainer.

```
Implement the plan in docs/plans/[NNNN-slug].md — [one-sentence description of the feature].

Read the plan doc first, in full, before touching any code. It captures decisions already made with the user; do not silently change any of them — if one turns out to be a bad idea once you're in the code, stop and flag it rather than deviating quietly. Also read CONTEXT.md and AGENTS.md in full before starting — AGENTS.md documents real gotchas already hit building adjacent features that are likely relevant here.

Treat the plan's "Design" section as a strong sketch, not literal code to paste in — verify every file/line reference against the actual current code first (it may have drifted since the plan was written), and use your own judgment on exact implementation as long as it satisfies the "Settled decisions" section.

Work inside the devcontainer, not the bare host shell (see AGENTS.md's "Running tests" section for why). If you're not already running inside it for this session, say so before doing anything else rather than falling back to the bare-host path silently.

As you work, append dated entries to the plan doc's own Log section — what you did, what didn't match the plan's assumptions, any gotchas hit and how you resolved them. This is the durable record of what actually happened, not just what was planned.

Before declaring this done:
- Full build clean, full test suite green, run inside the devcontainer.
- Live-verify in an actual browser via Playwright MCP (load the qr-simple-ui skill) per the plan's own Verification checklist section — confirm each item, not just the ones that seem obviously relevant.
- Update AGENTS.md/CONTEXT.md per the plan's "Docs to update" section, if it has one.
- Flip the plan's Status line to Implemented (or note why it can't be, yet) and update this file's index row to match.

When you report back, use the same level of specificity as prior implementation summaries in this repo's commit history (verified counts, what was actually clicked/tested live vs. only unit-tested, any gotchas hit) — not just "done, tests pass."
```
