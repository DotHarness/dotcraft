---
name: dotcraft-feature-workflow
description: Guides large DotCraft feature development from research and milestone planning through milestone specs, one-milestone-at-a-time implementation, and spec-based validation. Use when planning or delivering a substantial new DotCraft feature, a multi-milestone capability, or any request that should be designed before implementation.
---

# DotCraft Feature Workflow

Use this skill for large new features or major capability expansions in DotCraft.

The default stance is discussion-first, spec-before-code, one milestone at a time, and validation against the agreed milestone spec.

## Core rules

1. Do not jump straight into implementation for large features.
2. Discuss whether external reference research is needed before searching GitHub or cloning repositories.
3. Treat milestone specs as product and behavior contracts, not implementation design docs.
4. Implement only one milestone at a time unless the developer explicitly asks for a bundled delivery.
5. If implementation reality conflicts with the spec, stop and ask instead of silently changing scope.

## Standard workflow

### Phase 1: Scope and reference research

Start by clarifying:

- the feature goal
- who benefits from it
- what success looks like
- what is explicitly out of scope

If existing examples or prior art may matter, discuss that with the developer first.

When external reference research is approved:

- inspect GitHub for relevant projects, modules, or design patterns
- clone candidate repositories into `references/`
- analyze them in detail for architecture, workflow, UX, and lifecycle ideas
- summarize the findings in the conversation by default

Default artifact rule:

- keep research summaries in chat unless the developer explicitly asks to persist them
- do not create a permanent research document by default

Research output should capture:

- the strongest reusable ideas
- patterns that fit DotCraft
- patterns that do not fit DotCraft
- open questions created by the research

Soft gate:

- after research and the first design direction, pause for confirmation when there are meaningful trade-offs, unresolved uncertainty, or major scope choices

### Phase 2: Milestone outline

Produce a milestone outline before writing milestone specs.

Each milestone should have:

- a short name
- the user or product goal
- the expected outcome at the end of the milestone
- key scope boundaries
- major dependencies or blockers

Use a simple outline like this:

```markdown
## Milestone Outline

- M1: [name]
  Goal: [...]
  Expected outcome: [...]
  Out of scope: [...]
  Dependencies: [...]

- M2: [name]
  Goal: [...]
  Expected outcome: [...]
  Out of scope: [...]
  Dependencies: [...]
```

This outline is the planning scaffold for the later `specs/` files. It does not need to contain implementation details.

### Phase 3: Risk and feasibility review

Once the milestone outline exists, discuss risks and technical difficulty with the developer.

Review at least:

- architecture pressure on existing modules
- compatibility or migration concerns
- UX complexity
- testing difficulty
- whether the milestone split is still realistic

The goal is to decide whether the plan is feasible, not to design the final code yet.

### Phase 4: Milestone spec authoring

Write milestone spec files under `specs/`.

Naming rule:

- use numbered milestone files such as `feature-name-m1.md`, `feature-name-m2.md`

Before writing a new spec, inspect related documents already in `specs/` so the structure matches the repository's style.

Spec writing rules:

- do not include concrete implementation plans
- focus on workflow, behavior, constraints, UX expectations, and acceptance
- describe what must be true, not how every class or method will be written
- preserve enough clarity that implementation can later be evaluated against the spec

Recommended milestone spec sections:

1. Overview
2. Goal
3. Scope
4. Non-goals
5. User experience or behavioral contract
6. Required workflow or lifecycle
7. Constraints and compatibility notes
8. Acceptance checklist
9. Open questions

Recommended header style:

- top title
- metadata table with fields such as `Version`, `Status`, `Date`, and optional `Parent Spec`

Soft gate:

- after the milestone spec set is written, pause for developer review before moving into implementation

### Phase 5: One-milestone implementation planning

Do not start coding a milestone until a milestone-specific implementation plan exists.

For the current milestone only, write a concrete implementation plan that covers:

- touched modules and files
- public contracts or interfaces that may change
- data flow or control flow implications
- testing or verification approach
- risk notes and fallback strategy if a design choice proves wrong

This plan can be in the conversation unless the developer asks for a separate artifact.

Soft gate:

- before implementing each milestone, pause for confirmation if the implementation plan changes scope, introduces risk, or depends on a non-obvious trade-off

### Phase 6: Implementation

Implement only the current milestone.

During implementation:

- keep the work aligned with the current milestone spec
- avoid pulling future milestones into the same change unless explicitly requested
- surface spec mismatches as questions instead of silently redefining the milestone

If a useful adjustment is discovered during implementation:

1. explain the mismatch or new finding
2. compare it with the current milestone spec
3. ask whether the developer wants to update the scope, the spec, or the code plan

### Phase 7: Spec-based validation

After implementation, validate the result against the milestone spec.

Use the milestone spec as the primary checklist.

Review:

- required behaviors
- workflow or lifecycle coverage
- UX expectations
- declared constraints
- acceptance checklist items

If something is missing or different:

- call it out explicitly
- ask whether the difference is intentional
- only then decide whether to patch the implementation or adjust the spec

## What not to do

- do not skip from vague feature request straight to code
- do not write milestone specs that are mostly implementation details
- do not implement multiple milestones just because the code paths are nearby
- do not treat external reference projects as copy sources
- do not quietly diverge from the agreed milestone spec

## Response pattern

When this skill is active, prefer this working rhythm:

1. clarify the feature and decide whether external reference research is needed
2. research and summarize if approved
3. propose the milestone outline
4. discuss risks and feasibility
5. write milestone specs in `specs/`
6. choose one milestone
7. create the milestone implementation plan
8. implement
9. validate against the milestone spec

## DotCraft-specific reminders

- keep the repository's existing spec style in mind by reading related `specs/*.md` files before drafting a new one
- keep milestone specs high-level enough to survive implementation iteration
- use `references/` for cloned external repos when research is approved
- by default, summarize research in chat instead of creating additional documents
