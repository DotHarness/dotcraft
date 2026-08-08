---
name: oratorio
description: Use when the user asks to inspect or manage Oratorio board items, local tasks, review rounds, or agent-work coordination through the available Oratorio board tools.
---

# Oratorio

## Overview

Oratorio is an operator-facing board for agent work. It tracks local tasks, GitHub issues, pull requests, review rounds, run history, decisions, comments, drafts, and source-write audit records. Its board tools are delivered through the Oratorio App Binding and MCP. Refer to the stable local tool names below; DotCraft assigns the canonical MCP namespace, so do not guess or hard-code it.

## Core Concepts

- Board items are the durable work records shown in Oratorio.
- Local tasks are operator-created work items that may not come from GitHub.
- Review rounds queue DotCraft-backed analysis for an existing board item.
- Runs, decisions, comments, drafts, and timeline entries belong to Oratorio state.
- Oratorio keeps source writes under operator control. Do not claim that a GitHub write, merge, approval, or publish happened unless Oratorio reports it.

## Tool Use

Use `ListBoardItems` to scan the board. Filter by state, source, repository, assignee, search text, or limit when the user asks for a subset.

Use `GetBoardItem` before making claims about one item or before queuing work against it. Prefer item ids or short ids from Oratorio results. Do not invent ids.

Use `CreateBoardTask` when the user clearly wants a durable Oratorio task. Create concise titles and useful descriptions. Include repository, branch, assignee, and labels only when known or explicitly requested.

Use `QueueReviewRound` when the user wants Oratorio to start a review-analysis round for an existing board item. Include a short note describing the requested focus.

## Interaction Pattern

When the user asks what is on the board, summarize items by status and highlight likely next actions.

When the user asks to create tasks from a discussion, extract concrete tasks, avoid duplicates when visible board context is available, and create only tasks that are specific enough to act on.

Use `CreateBoardTask` or `QueueReviewRound` only when the user explicitly asks to change Oratorio state. Do not rely on a possible approval prompt as authorization. Execute clear, low-risk requests directly; ask a short clarification only when the target, scope, or risk is ambiguous. If DotCraft presents an approval prompt, treat it as an additional gate.

## Output

Mention Oratorio item titles, short ids, states, repositories, and next actions when they help the user act. Keep summaries concise and avoid exposing raw tool payloads unless the user asks for details.
