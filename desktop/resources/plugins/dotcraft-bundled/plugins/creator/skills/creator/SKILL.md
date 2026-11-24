---
name: creator
description: Extend DotCraft itself from Desktop by building or changing a plugin, skill, hook, workflow, Desktop extension, or .NET extension, then activating it and checking it in the running app. Use when the user wants DotCraft to gain or change a capability. Not for using or configuring an existing plugin.
---

# Creator

Build what the user wants DotCraft to do, make it active, and show that it works in the running app. Done means evidence, not files.

## Build

Settle what DotCraft should gain and who will use it. Ask only when the answer changes what you build. Choose the smallest form: a Skill for reusable instructions, a hook for lifecycle automation, a Workflow for a repeatable script, and a plugin when the capability bundles several parts, runs code, or will be shared.

Load the Skill that owns the form and follow it for the content: `plugin-creator` for plugins, including Desktop and .NET extensions, `skill-creator`, `create-hooks`, or `workflow-authoring`. Write in this workspace unless the user wants the capability everywhere.

## Activate

Run `dotcraft plugin verify` on a plugin and fix every error. Then call `ActivatePlugin` with its id, searching for the tool if it is deferred. `ListPlugins` shows ids and state. Desktop asks the user before it installs, enables, or trusts anything. After each later change, call `ActivatePlugin` again to reload the plugin, except for .NET source, which `CSharp.Build` applies. If the user declines, stop and say what is left for them.

## Check

Exercise each contribution as `plugin-creator` describes, in this conversation where you can.

For UI that a Desktop extension adds, look at it yourself. When the runtime context has `desktop.cdp`, attach to that endpoint with the `dotcraft-desktop-debugging` skill, open the view or run the action, and capture a screenshot. When that skill reports that the Playwright CLI is missing, ask the user to check the UI and mark it unverified. That skill needs the `dotcraft` plugin, so activate it first when the skill is missing. Without `desktop.cdp`, ask the user to turn on Settings › General › CDP debugging and restart DotCraft, or to check the UI themselves, and mark the UI unverified until then.

When something fails, fix the source, reload with `ActivatePlugin`, and check again.

## Finish

Report what you built and where, what you exercised and what you saw, and any step still waiting on the user.
