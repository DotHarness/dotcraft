---
name: report-issue
description: Turn a DotCraft failure or diagnosis into a clear, well-structured GitHub issue for the DotHarness/dotcraft repository, and produce a prefilled "new issue" URL the user can review and submit. Use after error-diagnosis (or when a user wants to report a bug), to draft the issue title and body and hand off a ready-to-open link. Does not submit on the user's behalf.
---

# DotCraft Issue Report

## Overview

Use this skill to convert what went wrong - ideally the output of the [[error-diagnosis]] skill, or a user's bug description - into a single, high-signal GitHub issue for the **DotHarness/dotcraft** repository. The goal is a concise report a maintainer can triage: clear title, public reproduction context, non-sensitive diagnostics, and environment.

This skill **drafts and hands off**; it never submits. The user always reviews the draft and clicks submit on GitHub themselves.

Important distinction: thread IDs and local DotCraft evidence are useful for **local diagnosis**, but they are not automatically suitable for the **public GitHub issue**. Treat any source thread ID supplied by Desktop as a local lookup hint unless the user explicitly approves sharing it.

## When To Use

- Right after `error-diagnosis` produced a finding the user wants to report.
- When a user explicitly asks to file a bug / send feedback to the developers.
- Not for feature requests phrased as vague wishes — first ask the user for the concrete behavior they expected.

## Safety And Privacy Rules

- Never include secrets: API keys, tokens, full auth headers, cookies, or absolute paths that reveal usernames you were not asked to share. Redact to `<redacted>`.
- Keep issue drafts minimal by default. Include the problem summary, impact, environment, short error preview, public reproduction steps, and necessary non-sensitive diagnostic conclusions.
- Thread IDs, rollout paths, trace/session IDs, user prompts, model output, and conversation excerpts may be used as local diagnosis inputs only. Do not copy them into the issue title or body by default.
- Prefer component names, tool/provider names, error classes, finish reasons, status codes, timestamps without identifying IDs, and short sanitized error previews over internal IDs or conversation content.
- Keep user prompts and model output out of the issue unless the user confirms they are safe to share and they are needed to reproduce.
- If the issue depends on a specific conversation operation, ask before including any thread/conversation information. Explain which fields would be included, why they matter, and how they will be redacted.
- If the user does not approve sharing thread/conversation details, replace them with a sanitized summary such as `a resumed Desktop thread after compaction`.
- Show the user the full draft before producing the submit link. Make it easy to edit title and body.

## Inputs To Gather

1. **What failed** — one sentence (from the diagnosis finding, or the user).
2. **Diagnostics** — optional, concise bullets: component/tool/provider, error class, finish reason, status code, sanitized short error preview, version/model/provider details. Use local IDs only to diagnose, not as default issue content.
3. **Reproduction** — the minimal public steps that triggered it, if known. If they are not known, write `not yet known` or ask the user for publicly shareable steps.
4. **Environment** — capture automatically where possible:
   - DotCraft version (and Desktop version if applicable)
   - OS and version
   - Active model / provider and reasoning effort
   - Runtime (.NET version) when relevant

## When To Ask Before Sharing Conversation Details

Ask the user for permission before adding thread IDs, rollout paths, trace/session IDs, user prompts, model output, or conversation excerpts to the issue. Only consider including them when the bug itself depends on conversation-specific operations, for example:

- resume, fork, compaction, or context restoration behavior
- thread binding, queued input, or cross-thread routing
- a specific sequence of tool calls or approvals
- trace/session persistence mismatches that cannot be described without the affected session identity

When asking, name the exact fields you want to include and propose the shortest redacted form. If the user does not confirm, keep the issue public-safe and describe the scenario generically.

## Issue Format

Title: a specific, searchable one-liner. Pattern: `<area>: <symptom> when <trigger>`.
Examples: `Edit tool: "old_string not found" after compaction`, `Provider: 400 unsupported param on stream with reasoning=high`.

Body (Markdown):

```markdown
## Summary
<one or two sentences: what happens and impact>

## Environment
- DotCraft: <version>
- Desktop: <version, if relevant>
- OS: <os and version>
- Model / provider: <model> · <provider> · reasoning <effort>

## Steps to reproduce
1. ...
2. ...

## Expected
<what should happen>

## Actual
<what happens, with a short error preview in a code block>

## Diagnosis (from DotCraft Doctor)
- Finding: <root cause / failure class>
- Diagnostics:
  - <optional sanitized component/error/status note>

## Notes
<anything uncertain, or extra evidence that would help>
```

## Handoff

After the user approves the draft, produce a **prefilled new-issue URL** so they can open it in the browser and submit:

```
https://github.com/DotHarness/dotcraft/issues/new?title=<url-encoded title>&body=<url-encoded body>
```

- URL-encode both the title and the body (encode spaces, newlines, `#`, `&`, etc.).
- GitHub caps the URL length; if the encoded body is very long, trim the lowest-value details (long evidence lists) and tell the user the full draft is above so they can paste it if needed.
- The Desktop composer mascot opens this URL with the system browser; outside Desktop, just present the URL and the raw title/body so the user can paste them.
- Remind the user to review for anything sensitive before submitting.

## Composing With error-diagnosis

Typical flow: `error-diagnosis` runs first and returns Finding / Evidence / Fix / Residual risk. Map that directly:

- Finding → Summary + Diagnosis.Finding
- Evidence → Diagnosis.Diagnostics after removing thread IDs, rollout paths, trace/session IDs, prompts, model output, conversation excerpts, and other sensitive or overly specific local identifiers.
- Fix → omit from the issue (that is the maintainer's call), or include only if the user wants to propose it.
- Residual risk → Notes.
