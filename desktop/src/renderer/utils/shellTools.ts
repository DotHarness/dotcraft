/**
 * Tool names that execute shell commands and may be paired with commandExecution streaming.
 * Keep in sync with server-side shell tools (e.g. Exec).
 */
export const SHELL_TOOL_NAMES = new Set(['Exec', 'RunCommand', 'BashCommand'])

export function isShellToolName(toolName: string | undefined): boolean {
  return toolName != null && SHELL_TOOL_NAMES.has(toolName)
}
