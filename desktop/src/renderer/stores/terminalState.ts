import type { ConversationItem } from '../types/conversation'
import { isShellToolName } from '../utils/shellTools'
import { limitShellRuntimeOutput } from './shellRuntimeBuffer'

function terminalStatusToExecutionStatus(status: string | undefined): ConversationItem['executionStatus'] | undefined {
  switch (status) {
    case 'running':
      return 'inProgress'
    case 'completed':
      return 'completed'
    case 'killed':
    case 'timedOut':
      return 'cancelled'
    case 'failed':
    case 'lost':
      return 'failed'
    default:
      return undefined
  }
}

export function shouldUseTerminalSnapshotOutput(event: string, output: string | undefined): output is string {
  return typeof output === 'string'
    && output.length > 0
    && !(event === 'terminal/started' && output === '(no output)')
}

export function appendTerminalDelta(output: string | undefined, delta: string): string {
  const base = output && output !== '(no output)' ? output : ''
  return limitShellRuntimeOutput(`${base}${delta}`)
}

function mergeTerminalIntoExecToolCall(
  item: ConversationItem,
  terminal: Record<string, unknown>,
  event: string,
  delta: string
): ConversationItem {
  if (item.type !== 'toolCall') return item
  if (!isShellToolName(item.toolName)) return item
  const callId = terminal.callId as string | undefined
  if (!callId || item.toolCallId !== callId) return item

  const status = terminalStatusToExecutionStatus(terminal.status as string | undefined)
  const output = terminal.output as string | undefined
  const aggregatedOutput = shouldUseTerminalSnapshotOutput(event, output)
    ? output
    : delta
      ? appendTerminalDelta(item.aggregatedOutput, delta)
      : item.aggregatedOutput

  return {
    ...item,
    command: (terminal.command as string | undefined) ?? item.command,
    workingDirectory: (terminal.workingDirectory as string | undefined) ?? item.workingDirectory,
    commandSource: (terminal.source as 'host' | undefined) ?? item.commandSource,
    aggregatedOutput,
    executionStatus: status ?? item.executionStatus,
    exitCode: (terminal.exitCode as number | null | undefined) ?? item.exitCode,
    duration: (terminal.wallTimeMs as number | undefined) ?? item.duration
  }
}

export function mergeTerminalAcrossItems(
  items: ConversationItem[],
  terminal: Record<string, unknown>,
  event: string,
  delta: string
): ConversationItem[] {
  return items.map((item) => mergeTerminalIntoExecToolCall(item, terminal, event, delta))
}
