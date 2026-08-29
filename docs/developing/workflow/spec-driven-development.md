# SDD Workflow

DotCraft is built spec-first: protocols and cross-module behavior are written as specifications before they are implemented, and the implementation is then held to the spec. When requirements change, the spec changes first and the code follows. This page describes the workflow used to contribute substantial features to DotCraft.

![The DotCraft SDD workflow: a main spec is split into temporary milestone specs, implemented and accepted one at a time, then consolidated back into the main spec while the temporary files are deleted](/sdd-workflow-flow.svg)

## Working rules

- **Spec before code.** When a change touches a protocol design or cross-module flow defined in `specs/`, update the spec first, then implement.
- **Resolve conflicts at the spec level.** When a change contradicts an existing spec, settle it in the spec before touching code.
- **Conformance tests track the spec.** Code that depends on a protocol carries tests aligned with its spec, so divergence surfaces as failures.
- **Docs follow behavior.** When behavior changes, update the user-facing documentation in both languages.
- **Specs state the contract, not the implementation.** A spec records what must be true, and stays high-level enough to survive implementation iterations.

## The main spec and milestone specs

The workflow revolves around the version link between one main spec and a set of temporary milestone specs:

- The **main spec** is the durable contract for the complete feature, kept under `specs/` in the project's established spec format. Behavior, architecture, or workflow changes start there.
- **Milestone specs** are the development-time split, kept under the repository-root `references/` directory by default and **never committed**. Each one links back to the main spec through its metadata table (Version, Status, Date, Parent Spec), and records only that milestone's goal, boundaries, and acceptance criteria — no implementation steps.

## The workflow

1. **Research and scope.** Read the relevant code, existing specs, tests, and history to pin down the goal, success criteria, and non-goals. Resolve material uncertainty before the design is treated as final.
2. **Split.** Draft the main spec, derive the milestone outline and per-milestone contracts from it, and present the whole set for review. Implementation does not start before the review confirms it.
3. **Implement one milestone at a time.** Each milestone gets its own implementation plan first, then the implementation. When behavior has to change, the order is main spec → unimplemented milestone specs → the active plan → the code.
4. **Validate and accept.** Check the result against both the milestone spec and the main spec, deliver the evidence, and stop for acceptance. Only an accepted milestone unlocks the next one.
5. **Consolidate.** After every milestone is accepted, merge the durable final behavior and decisions back into the main spec, verify the spec matches the implementation, then delete all temporary milestone files.

Milestones are a development coordination mechanism, not a product concept. Milestone labels and phase narration stay out of production code, tests, documentation, and commit messages.

## Tooling

Two official plugins carry this workflow, and with both enabled the agent runs it for you (install them via [Plugins and tools](../../features/agent-system/plugins-tools)): the `dotcraft` plugin supplies DotCraft's spec locations and format conventions, and the `harness-workflow` plugin's `$feature-workflow` implements the process above — a request like "plan a new feature" activates it.

## See also

- [Architecture Overview](../architecture/overview) — the runtime these specs describe.
- [Plugins and tools](../../features/agent-system/plugins-tools) — install and enable `dotcraft` and `harness-workflow`.
