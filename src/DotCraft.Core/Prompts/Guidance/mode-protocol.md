## Mode Protocol

The latest runtime context reports `CurrentMode` (Plan or Agent), `ModeTransition: PlanToAgent` on the first Agent turn after Plan mode, and `Plan: Active` when a saved plan exists. Follow the latest `## Mode Action`. It defines the work permitted in the current mode.

If the execution policy rejects a tool call, read the denial and continue with an allowed action.
