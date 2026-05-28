# Spec-Driven Development

DotCraft is built **spec-first**: every protocol and cross-cutting behavior is written down as a specification before it is implemented, and the code is held to that specification afterward. This page explains the approach and how to follow it when contributing to DotCraft.

## What is Spec-Driven Development?

Spec-Driven Development (SDD) is a methodology where a versioned, human-readable **specification — not the code — is the source of truth** for what a system should do. You describe the intended behavior, contracts, and constraints first; implementation and tests are then derived from and validated against that spec. When requirements change, you edit the spec and bring the code back in line.

SDD rose to prominence in 2025–2026 as a structured alternative to ad-hoc "vibe coding" with AI assistants, which tends to drift from intent and decay as a project grows. Keeping intent in a durable spec gives both humans and agents a stable contract to work from.

> SDD is usually described at three levels of rigor: **spec-first** (write the spec before the code), **spec-anchored** (keep the spec as the living contract the code is checked against), and **spec-as-source** (regenerate code from the spec). DotCraft operates at the spec-first / spec-anchored level — specs are authored before implementation and remain the contract.

## How DotCraft applies it

The `specs/` directory holds DotCraft's living specifications — `session-core`, `appserver-protocol`, `app-binding`, and others. They define the **what**: behavior, wire contracts, lifecycle, and constraints — not the implementation details.

The working rules:

- **Spec before code.** When you change a protocol design or cross-cutting flow defined in `specs/`, update the spec first, then implement.
- **Resolve conflicts at the spec level.** If a proposed change conflicts with an existing spec, settle the spec-level conflict before touching code.
- **Conformance tests track the spec.** Protocol-dependent code carries tests aligned to the spec, so drift surfaces as a failing test.
- **Docs follow behavior.** User-facing docs — in both languages — are updated when behavior changes.
- **Specs describe contracts, not code.** A spec states what must be true and stays high-level enough to survive implementation iteration.

## The workflow

For a substantial feature, the rhythm is discussion-first, spec-before-code, one milestone at a time:

| Phase | What happens |
|---|---|
| 1. Scope & research | Clarify the goal, who benefits, success criteria, and non-goals. Research prior art only when it helps. |
| 2. Milestone outline | Split the work into milestones with goals, outcomes, and scope boundaries. |
| 3. Risk & feasibility | Review architecture pressure, compatibility, UX, and testing difficulty. |
| 4. Milestone specs | Write numbered specs under `specs/` (e.g. `feature-name-m1.md`) — behavior and acceptance, not implementation. |
| 5. Implementation plan | For the current milestone only, plan touched modules, contracts, and verification. |
| 6. Implementation | Build one milestone; surface spec mismatches as questions instead of silently diverging. |
| 7. Spec-based validation | Check the result against the milestone spec's acceptance checklist. |

## Tooling: the dotcraft-dev plugin

The development plugin at [`samples/plugins/dotcraft-dev`](https://github.com/DotHarness/dotcraft/tree/master/samples/plugins/dotcraft-dev) packages this workflow as agent skills, so the agent follows SDD when you ask it to plan or build something. Enable it from the Plugins catalog (see [Plugins & Tools](../features/plugins-tools.md)), then drive work through its skills:

| Skill | Role |
|---|---|
| `dev-guide` | The project's spec-first norms, testing rules, and bilingual-docs guidance. |
| `feature-workflow` | Runs the seven-phase flow above for large features — milestone specs, one-at-a-time implementation, spec validation. |
| `error-diagnosis` | Read-only diagnosis of LLM / agent / tool / session failures. |
| `ui-prototype` | Standalone Desktop UI prototypes before touching production code. |
| `svg-design` | Designing and validating repo-native SVG assets. |
| `release-draft` | Drafting release notes. |

With the plugin enabled, a request like "plan a new feature" leads the agent into the `feature-workflow` skill: it discusses scope, proposes milestones, writes specs into `specs/`, and only then implements and validates against them. The individual skills also ship standalone under `samples/skills/` if you want to adopt them without the bundle.

## See also

- [Architecture](./architecture.md) — the runtime the specs describe.
- [Plugins & Tools](../features/plugins-tools.md) — installing and enabling plugins like `dotcraft-dev`.
- [Samples & Templates](../resources/samples.md) — the `samples/` directory, including standalone skill templates.

Further reading: [What is Spec-Driven Development? (IBM)](https://www.ibm.com/think/topics/spec-driven-development) · [Understanding Spec-Driven Development (Martin Fowler)](https://martinfowler.com/articles/exploring-gen-ai/sdd-3-tools.html)
