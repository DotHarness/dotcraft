import type { ConversationTurn } from '../types/conversation'
import { isToolExecutionFailure } from './toolCallDisplay'
import { resolveToolKind } from './toolKind'

export function readTerminalInput(buffer: string, stdin: string): { commands: string[]; buffer: string } {
  const commands: string[] = []
  let pending = buffer
  for (const char of stdin) {
    if (char === '\r' || char === '\n') {
      const command = pending.trim()
      if (command) commands.push(command)
      pending = ''
    } else if (char === '\u0003') {
      pending = ''
    } else if (char === '\b' || char === '\u007f') {
      pending = pending.slice(0, -1)
    } else {
      pending += char
    }
  }
  return { commands, buffer: pending }
}

export function attachTerminalInputs(turns: ConversationTurn[]): ConversationTurn[] {
  const inputsBySession = new Map<string, string[]>()
  const buffers = new Map<string, string>()
  for (const turn of turns) {
    for (const item of turn.items) {
      if (item.type !== 'toolCall' || resolveToolKind(item) !== 'writeStdin' || isToolExecutionFailure(item)) continue
      const sessionId = item.arguments?.sessionId
      const input = item.arguments?.input
      if (typeof sessionId !== 'string' || typeof input !== 'string' || input.length === 0) continue
      const read = readTerminalInput(buffers.get(sessionId) ?? '', input)
      buffers.set(sessionId, read.buffer)
      if (read.commands.length > 0) {
        inputsBySession.set(sessionId, [...(inputsBySession.get(sessionId) ?? []), ...read.commands])
      }
    }
  }
  if (inputsBySession.size === 0) return turns

  const inputsFor = (sessionId: string | undefined) => (sessionId ? inputsBySession.get(sessionId) : undefined)
  return turns.map((turn) => {
    if (!turn.items.some((item) => item.type === 'toolCall' && inputsFor(item.terminalSessionId))) return turn
    return {
      ...turn,
      items: turn.items.map((item) => {
        const inputs = item.type === 'toolCall' ? inputsFor(item.terminalSessionId) : undefined
        return inputs ? { ...item, terminalInputs: inputs } : item
      })
    }
  })
}
