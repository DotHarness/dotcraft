---
name: dotcraft-doctor
description: Route DotCraft troubleshooting requests to failure diagnosis, context handoff, or issue reporting. Use when DotCraft Doctor is invoked for Hub, AppServer, startup, process, provider, tool, agent, or session problems without a more focused skill.
---

# DotCraft Doctor

## Router Only

Choose and load the focused skill; do not perform its work here.

## Routing

- If the user names a focused skill, load it directly.
- For Hub, AppServer, startup, process, request, provider, tool, agent, or session failures, load `dotcraft-error-diagnosis`.
- To find sessions or traces, export cleaned context, or prepare an agent handoff, load `dotcraft-context-handoff`.
- To turn a diagnosis or bug description into an issue draft, load `dotcraft-report-issue`.
- For diagnosis followed by reporting, run `dotcraft-error-diagnosis` first and pass only its public-safe summary to `dotcraft-report-issue`.
