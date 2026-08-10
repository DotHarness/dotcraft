---
name: dotcraft-doctor
description: Route broad DotCraft troubleshooting requests to diagnosis, context handoff, or issue reporting. Use when DotCraft Doctor is invoked without a focused skill or the user needs to choose or sequence those workflows.
---

# DotCraft Doctor

## Router Only

Choose and load the focused skill; do not perform its work here.

## Routing

- If the user names a focused skill, load it directly.
- For failed requests, unexpected behavior, provider errors, or session recovery problems, load `error-diagnosis`.
- To find sessions or traces, export cleaned context, or prepare an agent handoff, load `context-handoff`.
- To turn a diagnosis or bug description into an issue draft, load `report-issue`.
- For diagnosis followed by reporting, run `error-diagnosis` first and pass only its public-safe summary to `report-issue`.
