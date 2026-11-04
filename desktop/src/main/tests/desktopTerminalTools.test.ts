import { describe, expect, it } from 'vitest'
import { listTerminalTabsTool, readTerminalTool, type DesktopTerminalHost } from '../desktopTerminalTools'
import type { TerminalTabSnapshot } from '../viewerTerminal'

function snapshot(tabId: string, output: string, extra: Partial<TerminalTabSnapshot> = {}): TerminalTabSnapshot {
  return { tabId, cwd: '/workspace', cols: 80, rows: 24, output, outputTrimmed: false, ...extra }
}

function hostWith(byThread: Record<string, TerminalTabSnapshot[]>): DesktopTerminalHost {
  return {
    threadTabs: (threadId) => byThread[threadId] ?? [],
    waitForOutput: async () => 'timeout'
  }
}

function textOf(result: Awaited<ReturnType<typeof readTerminalTool>>): string {
  expect(result.success).toBe(true)
  const item = result.contentItems?.[0]
  if (item?.type !== 'text') throw new Error('Expected a text result')
  return item.text
}

function numbered(count: number): string {
  return Array.from({ length: count }, (_, index) => `line ${index + 1}`).join('\r\n')
}

describe('ReadTerminal', () => {
  it('reads rendered lines from the thread\'s primary terminal', async () => {
    const host = hostWith({
      'thread-1': [
        snapshot('primary', '$ npm test\r\nprogress 10%\rprogress 100%\r\n\x1b[31mFAIL\x1b[0m src/a.test.ts\r\n'),
        snapshot('server', 'listening on 3000\r\n')
      ],
      'thread-2': [snapshot('elsewhere', 'secret\r\n')]
    })

    const text = textOf(await readTerminalTool(host, 'thread-1', {}))

    expect(text).toContain('$ npm test\nprogress 100%\nFAIL src/a.test.ts')
    expect(text).not.toContain('\x1b[')
    expect(text).not.toContain('secret')
    expect(text).not.toContain('listening on 3000')
  })

  it('reports a terminal it cannot find', async () => {
    const host = hostWith({ 'thread-1': [snapshot('primary', 'ok\r\n')] })

    const missing = await readTerminalTool(host, 'thread-1', { tabId: 'elsewhere' })
    expect(missing).toMatchObject({ success: false, errorCode: 'TerminalNotFound' })

    const none = await readTerminalTool(host, 'thread-2', {})
    expect(none).toMatchObject({ success: false, errorCode: 'TerminalNotFound' })
  })

  it('returns the newest lines and pages back with offsetLines', async () => {
    const host = hostWith({ 'thread-1': [snapshot('primary', numbered(30))] })

    const recent = textOf(await readTerminalTool(host, 'thread-1', { lines: 5 }))
    expect(recent.split('\n').slice(0, 5)).toEqual(['line 26', 'line 27', 'line 28', 'line 29', 'line 30'])

    const page = textOf(await readTerminalTool(host, 'thread-1', { lines: 5, offsetLines: 5 }))
    expect(page.split('\n').slice(1)).toEqual(['line 21', 'line 22', 'line 23', 'line 24', 'line 25'])
  })

  it('greps the retained lines with numbered matches and context', async () => {
    const output = ['boot', 'error: first', 'a', 'b', 'c', 'd', 'e', 'error: second', 'f'].join('\r\n')
    const host = hostWith({ 'thread-1': [snapshot('primary', output)] })

    const all = textOf(await readTerminalTool(host, 'thread-1', { grep: 'ERROR' }))
    expect(all.split('\n').slice(1)).toEqual([
      '1- boot', '2: error: first', '3- a', '4- b',
      '--',
      '6- d', '7- e', '8: error: second', '9- f'
    ])

    const latest = textOf(await readTerminalTool(host, 'thread-1', { grep: 'error', lines: 4 }))
    expect(latest).toContain('8: error: second')
    expect(latest).not.toContain('2: error: first')

    const earlier = textOf(await readTerminalTool(host, 'thread-1', { grep: 'error', lines: 4, offsetLines: 2 }))
    expect(earlier).toContain('2: error: first')
    expect(earlier).not.toContain('8: error: second')
  })

  it('shows only the screen of a full-screen program', async () => {
    const output = `${numbered(5)}\r\n\x1b[?1049h\x1b[H\x1b[2Jtop - load average 0.42`
    const host = hostWith({ 'thread-1': [snapshot('primary', output)] })

    const text = textOf(await readTerminalTool(host, 'thread-1', {}))
    expect(text).toContain('top - load average 0.42')
    expect(text).not.toContain('line 1')
  })

  it('drops the partial first line of trimmed output', async () => {
    const host = hostWith({ 'thread-1': [snapshot('primary', `partial line\r\n${numbered(3)}`, { outputTrimmed: true })] })

    const text = textOf(await readTerminalTool(host, 'thread-1', {}))
    expect(text).toContain('line 1\nline 2\nline 3')
    expect(text).not.toContain('partial line')
  })

  it('lists only the thread\'s tabs', () => {
    const host = hostWith({
      'thread-1': [snapshot('primary', '', { title: 'Terminal' }), snapshot('server', '', { title: 'Dev server' })],
      'thread-2': [snapshot('elsewhere', '')]
    })

    const listed = JSON.parse(textOf(listTerminalTabsTool(host, 'thread-1'))) as Array<{ tabId: string; title: string }>
    expect(listed.map((tab) => [tab.tabId, tab.title])).toEqual([['primary', 'Terminal'], ['server', 'Dev server']])
  })

  it('reads the latest output after waiting for it', async () => {
    let output = 'before\r\n'
    const host: DesktopTerminalHost = {
      threadTabs: (threadId) => (threadId === 'thread-1' ? [snapshot('primary', output)] : []),
      waitForOutput: async () => {
        output += 'after\r\n'
        return 'output'
      }
    }

    const text = textOf(await readTerminalTool(host, 'thread-1', { waitForOutputMs: 100 }))
    expect(text).toContain('before\nafter')
  })
})
