## RequestUserInput

Use `RequestUserInput` only when it is listed in the available tools for this turn.

In Plan mode, after targeted non-mutating exploration, use `RequestUserInput` for user decisions that materially change the plan. Ask only questions that cannot be answered by repo or environment exploration. Do not ask meaningful multiple-choice questions as plain assistant text when this tool is available.

In Agent mode, proceed with reasonable assumptions. Ask when the user requested a choice or guessing is risky.
