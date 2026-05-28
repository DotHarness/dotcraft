---
name: oratorio
description: Use when the current thread is connected to Oratorio, or when the user asks to inspect or manage Oratorio board items, local tasks, review rounds, or agent-work coordination.
---

# Oratorio

## Overview

Oratorio is an operator-facing board for agent work. It tracks local tasks, GitHub issues, pull requests, review rounds, run history, decisions, comments, drafts, and source-write audit records. Use Oratorio tools when the user wants DotCraft to understand or manage that board.

## Core Concepts

- Board items are the durable work records shown in Oratorio.
- Local tasks are operator-created work items that may not come from GitHub.
- Review rounds queue DotCraft-backed analysis for an existing board item.
- Runs, decisions, comments, drafts, and timeline entries belong to Oratorio state.
- Oratorio keeps source writes under operator control. Do not claim that a GitHub write, merge, approval, or publish happened unless Oratorio reports it.

## Tool Use

Use `oratorio.ListBoardItems` to scan the board. Filter by state, source, repository, assignee, search text, or limit when the user asks for a subset.

Use `oratorio.GetBoardItem` before making claims about one item or before queuing work against it. Prefer item ids or short ids from Oratorio results. Do not invent ids.

Use `oratorio.CreateBoardTask` when the user clearly wants a durable Oratorio task. Create concise titles and useful descriptions. Include repository, branch, assignee, and labels only when known or explicitly requested.

Use `oratorio.QueueReviewRound` when the user wants Oratorio to start a review-analysis round for an existing board item. Include a short note describing the requested focus.

## Interaction Pattern

When the user asks what is on the board, summarize items by status and highlight likely next actions.

When the user asks to create tasks from a discussion, extract concrete tasks, avoid duplicates when visible board context is available, and create only tasks that are specific enough to act on.

When the target item is ambiguous, ask a short clarification before using write or queue tools.

When using write or queue tools, rely on DotCraft approval for the final confirmation. Do not add a second verbal confirmation unless the request is ambiguous or risky.

## Output

Mention Oratorio item titles, short ids, states, repositories, and next actions when they help the user act. Keep summaries concise and avoid exposing raw tool payloads unless the user asks for details.
