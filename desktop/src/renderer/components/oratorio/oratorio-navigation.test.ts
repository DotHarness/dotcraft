import { describe, expect, it } from 'vitest'
import { parseOratorioNavigationUrl } from './oratorio-navigation'

describe('Oratorio native navigation', () => {
  it('maps trusted Oratorio MCP App links to native targets', () => {
    expect(parseOratorioNavigationUrl('oratorio://open/board')).toEqual({ kind: 'board' })
    expect(parseOratorioNavigationUrl('oratorio://open/task/ORA-42')).toEqual({ kind: 'task', taskId: 'ORA-42' })
    expect(parseOratorioNavigationUrl('oratorio://open/settings/github')).toEqual({ kind: 'settings', section: 'github' })
  })

  it('does not claim unrelated URLs', () => {
    expect(parseOratorioNavigationUrl('https://example.com')).toBeNull()
    expect(parseOratorioNavigationUrl('oratorio://evil/task/1')).toBeNull()
  })
})
