import { describe, expect, it } from 'vitest'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import { attachTerminalInputs, readTerminalInput } from '../utils/terminalInput'
import { coreToolFields } from './testToolPresentation'

function turn(id: string, items: ConversationItem[]): ConversationTurn {
  return { id, threadId: 'thread-1', status: 'completed', items, startedAt: '2026-10-14T00:00:00.000Z' }
}

describe('readTerminalInput', () => {
  it('reads lines the way a terminal does', () => {
    expect(readTerminalInput('', 'npm\x7f test\r\nsecret\x03y\n')).toEqual({ commands: ['np test', 'y'], buffer: '' })
    expect(readTerminalInput('', 'partial')).toEqual({ commands: [], buffer: 'partial' })
    expect(readTerminalInput('partial', ' line\n')).toEqual({ commands: ['partial line'], buffer: '' })
  })
})

describe('attachTerminalInputs', () => {
  it('attaches WriteStdin input from later turns to the command with the same session', () => {
    const command: ConversationItem = {
      id: 'exec', type: 'toolCall', toolCallId: 'call-exec', ...coreToolFields('Exec'), terminalSessionId: 'term_1'
    }
    const write = (id: string, input: string): ConversationItem => ({
      id, type: 'toolCall', toolCallId: id, ...coreToolFields('WriteStdin'), arguments: { sessionId: 'term_1', input }
    })

    const denied = { ...write('w4', 'rm -rf build\n'), success: false }
    const turns = attachTerminalInputs([
      turn('t1', [command]),
      turn('t2', [write('w1', ''), write('w2', 'y'), write('w3', 'es\n'), denied])
    ])

    expect(turns[0].items[0].terminalInputs).toEqual(['yes'])
  })
})
