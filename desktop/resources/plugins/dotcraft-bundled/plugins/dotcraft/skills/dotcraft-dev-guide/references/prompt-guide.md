# Prompt guide

Use this guide when creating or reviewing DotCraft's system prompts, tool descriptions, and skills. Keep instructions focused on the decisions the agent needs to make.

## Establish the behavior

Identify the intended action, its target, the agent's authority, and the expected result from the owning specification and implementation. Follow the development guide's spec-first workflow when changing that behavior.

Inspect how the instructions are assembled and when each section is loaded. Check the relevant surrounding instructions for duplication or conflict. Describe the capabilities, paths, and state actually supplied by the runtime.

When researching a design, verify the relevant source and its activation conditions. Keep evidence and adaptation rationale in the change review. Incorporate the resulting rule into the guide without carrying over a product-specific mechanism or research narrative.

## Choose the instruction surface

| Surface | Content |
|---|---|
| System instructions | Shared behavior and essential responsibility boundaries for the available capabilities |
| Tool description | When to use the tool, its arguments and results, and its specific operating requirements |
| Runtime context | Resolved paths, available capabilities, current scope, and state |
| Skill description | The task that should select the skill, stated briefly and specifically |
| Skill body | The selected workflow's outcome, essential constraints, and links to details |
| Supporting reference | Details needed only for a particular task or mode |

Keep the information needed to select an action available at the point of selection. Load detailed procedures when they become relevant. Enforce permissions and required invariants in runtime code as appropriate.

## Write concise instructions

- State the usual action with a concrete verb and target. Add conditions when they change the action.
- Prefer positive instructions. Keep explicit restrictions that define real permission, ownership, or data-integrity boundaries.
- Use concepts the agent can understand from its instructions and tools. Include implementation terminology only when it helps the agent act correctly.
- Write short, natural sentences. Prefer periods and commas over semicolon chains.
- Include useful, non-obvious guidance. Remove repetition, generic encouragement, and speculative explanations.
- Describe outcomes and decision criteria when several approaches are valid. Specify a fixed sequence when correctness depends on it.
- State authorization and completion boundaries clearly. Preserve the user's existing choices and authorization within the requested scope.

For each addition, identify the decision it improves. For each deletion, check that the required action and boundary remain clear. Turn individual failures into representative validation cases. Add a general rule only when the failure reveals a missing contract.

## Keep skills focused

Write the description around the task the skill handles. Add an exclusion when it prevents a likely incorrect match.

Keep shared constraints and routing in the body. Link each supporting reference where it is needed and explain when to read it. A short, self-contained workflow can stay in one file.

Reduce context by selecting relevant resources while preserving the complete instructions needed for the selected workflow.

Give each instruction one maintained home. Refer to available tools and resources by their actual names. Keep the guidance usable in the environment where the skill is distributed.

## Compare and validate

For a substantive rewrite, compare the previous and proposed text by responsibility, loading condition, and expected behavior. When using a reference for comparison, compare equivalent sections and identify deliberate differences.

Use a consistent method to measure words or characters. Report tokens only when measured with the target tokenizer. Separate static instructions from injected content. Treat length as a way to find excess, not as a fixed target.

Validate changes with tasks that exercise the affected decisions. For a skill, include a matching request and a nearby request that should not select it. Cover runtime variations when they affect the changed behavior.

Inspect tool calls and resulting state as well as the answer. Use model replay to evaluate instruction following and deterministic tests for runtime behavior. Follow the development guide's testing rules rather than adding assertions about prompt wording.

Report what changed, why, and what was verified. For model replay, record the model and relevant configuration, and state any untested behavior. Keep verification proportional to the change.
