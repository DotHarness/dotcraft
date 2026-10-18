import type { CustomCommandInfo } from '../hooks/useCustomCommandCatalog'

interface CommandExecuteWireResult {
  handled?: boolean
  message?: string | null
  isMarkdown?: boolean
  expandedPrompt?: string | null
}

interface ResolveCustomCommandExecutionArgs {
  text: string
  threadId: string
  commands: CustomCommandInfo[]
  sendRequest: (method: string, params?: unknown) => Promise<unknown>
}

export interface ResolveCustomCommandExecutionResult {
  matchedCustomCommand: boolean
  shouldSendTurn: boolean
  textForTurn: string
  message: string | null
  isMarkdown: boolean
}

function parseCommandInvocation(text: string): { commandToken: string; arguments: string[] } | null {
  const trimmed = text.trim()
  if (!trimmed.startsWith('/')) return null
  const parts = trimmed.split(/\s+/).filter(Boolean)
  if (parts.length === 0) return null
  return {
    commandToken: parts[0]!,
    arguments: parts.slice(1)
  }
}

function toCanonicalCommandSet(command: CustomCommandInfo): Set<string> {
  const set = new Set<string>()
  const push = (value: string): void => {
    const v = value.trim().toLowerCase()
    if (!v) return
    set.add(v.startsWith('/') ? v : `/${v}`)
  }
  push(command.name)
  for (const alias of command.aliases) push(alias)
  return set
}

function findMatchingCustomCommand(token: string, commands: CustomCommandInfo[]): CustomCommandInfo | null {
  const normalized = token.trim().toLowerCase()
  if (!normalized.startsWith('/')) return null
  for (const command of commands) {
    const names = toCanonicalCommandSet(command)
    if (names.has(normalized)) return command
  }
  return null
}

export async function resolveCustomCommandExecution({
  text,
  threadId,
  commands,
  sendRequest
}: ResolveCustomCommandExecutionArgs): Promise<ResolveCustomCommandExecutionResult> {
  const invocation = parseCommandInvocation(text)
  if (!invocation) {
    return {
      matchedCustomCommand: false,
      shouldSendTurn: true,
      textForTurn: text,
      message: null,
      isMarkdown: false
    }
  }
  const matched = findMatchingCustomCommand(invocation.commandToken, commands)
  if (!matched) {
    return {
      matchedCustomCommand: false,
      shouldSendTurn: true,
      textForTurn: text,
      message: null,
      isMarkdown: false
    }
  }

  const rawResult = (await sendRequest('command/execute', {
    threadId,
    command: invocation.commandToken,
    arguments: invocation.arguments
  })) as CommandExecuteWireResult
  const expandedPrompt =
    typeof rawResult.expandedPrompt === 'string' && rawResult.expandedPrompt.trim() !== ''
      ? rawResult.expandedPrompt
      : null
  const handled = rawResult.handled !== false
  const shouldSendTurn = handled && expandedPrompt !== null
  return {
    matchedCustomCommand: true,
    shouldSendTurn,
    textForTurn: shouldSendTurn ? expandedPrompt! : '',
    message: typeof rawResult.message === 'string' && rawResult.message.trim() !== ''
      ? rawResult.message
      : null,
    isMarkdown: rawResult.isMarkdown === true
  }
}
