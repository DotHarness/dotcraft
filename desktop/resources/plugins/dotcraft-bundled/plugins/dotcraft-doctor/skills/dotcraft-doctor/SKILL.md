---
name: dotcraft-doctor
description: Route DotCraft troubleshooting requests to failure diagnosis, context handoff, or issue reporting. Use when DotCraft Doctor is invoked for Hub, AppServer, startup, process, provider, tool, agent, or session problems without a more focused skill.
---

# DotCraft Doctor

## Router Only

Choose and load the focused skill; do not perform its work here.

## Routing

- If the user names a focused skill, load it directly.
- For Hub, AppServer, startup, process, request, provider, tool, agent, or session failures, load `error-diagnosis`.
- To find sessions or traces, export cleaned context, or prepare an agent handoff, load `context-handoff`.
- To turn a diagnosis or bug description into an issue draft, load `report-issue`.
- For diagnosis followed by reporting, run `error-diagnosis` first and pass only its public-safe summary to `report-issue`.
