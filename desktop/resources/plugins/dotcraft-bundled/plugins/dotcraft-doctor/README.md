# DotCraft Doctor Plugin

A built-in DotCraft plugin that helps users understand and report failures. When a turn errors, the composer mascot can install this plugin and open a fresh thread to diagnose what went wrong, and — if the user wants — draft an issue report for the developers.

Included skills:

- `error-diagnosis` — reconstruct a failed turn from local `.craft` evidence (state DB + thread rollout) and explain the root cause, read-only.
- `context-handoff` — search DotCraft sessions/traces and export a cleaned Markdown handoff for another coding agent, with rollback and compaction continuity notes.
- `report-issue` — turn a diagnosis (or a bug description) into a clear GitHub issue for `DotHarness/dotcraft` and produce a prefilled "new issue" URL to review and submit.
