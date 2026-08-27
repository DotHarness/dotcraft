import { describe, expect, it } from 'vitest'
import {
  consumeOratorioNavigation,
  onOratorioNavigation,
  parseOratorioNavigationUrl,
  requestOratorioNavigation
} from './oratorio-navigation'

describe('Oratorio native navigation', () => {
  it('maps Oratorio MCP App links to native targets', () => {
    expect(parseOratorioNavigationUrl('oratorio://open/board')).toEqual({ kind: 'board' })
    expect(parseOratorioNavigationUrl('oratorio://open/task/ORA-42')).toEqual({ kind: 'task', taskId: 'ORA-42' })
    expect(parseOratorioNavigationUrl('oratorio://open/settings/github')).toEqual({ kind: 'settings', section: 'github' })
  })

  it('does not claim unrelated URLs', () => {
    expect(parseOratorioNavigationUrl('https://example.com')).toBeNull()
    expect(parseOratorioNavigationUrl('oratorio://evil/task/1')).toBeNull()
  })

  it('delivers board, task, and settings targets only while subscribed', () => {
    const received: unknown[] = []
    const unsubscribe = onOratorioNavigation((target) => received.push(target))

    requestOratorioNavigation({ kind: 'board' })
    requestOratorioNavigation({ kind: 'task', taskId: 'ORA-42' })
    requestOratorioNavigation({ kind: 'settings', section: 'github' })
    unsubscribe()
    requestOratorioNavigation({ kind: 'board' })

    expect(received).toEqual([
      { kind: 'board' },
      { kind: 'task', taskId: 'ORA-42' },
      { kind: 'settings', section: 'github' }
    ])
    expect(consumeOratorioNavigation()).toEqual({ kind: 'board' })
  })
})
