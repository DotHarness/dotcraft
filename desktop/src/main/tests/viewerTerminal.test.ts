import { beforeEach, describe, expect, it, vi } from 'vitest'

const { spawnMock } = vi.hoisted(() => ({
  spawnMock: vi.fn()
}))

vi.mock('@lydell/node-pty', () => ({
  spawn: spawnMock
}))

import { ViewerTerminalManager } from '../viewerTerminal'

interface MockPty {
  pid: number
  onData: (cb: (data: string) => void) => void
  onExit: (cb: (event: { exitCode: number; signal: number | null }) => void) => void
  write: (data: string) => void
  resize: (cols: number, rows: number) => void
  kill: () => void
}

function createMockPty(): MockPty {
  return {
    pid: 4321,
    onData: vi.fn(),
    onExit: vi.fn(),
    write: vi.fn(),
    resize: vi.fn(),
    kill: vi.fn()
  }
}

function createMockWindow() {
  const send = vi.fn()
  return {
    id: 99,
    isDestroyed: vi.fn(() => false),
    webContents: {
      isDestroyed: vi.fn(() => false),
      send
    }
  } as unknown as Electron.BrowserWindow
}

describe('ViewerTerminalManager', () => {
  let onDataHandler: ((data: string) => void) | null
  let onExitHandler: ((event: { exitCode: number; signal: number | null }) => void) | null
  let mockPty: MockPty

  beforeEach(() => {
    onDataHandler = null
    onExitHandler = null
    mockPty = createMockPty()
    mockPty.onData = (cb) => {
      onDataHandler = cb
    }
    mockPty.onExit = (cb) => {
      onExitHandler = cb
    }
    spawnMock.mockReset()
    spawnMock.mockReturnValue(mockPty)
  })

  it('creates tab, forwards output and returns attach snapshot', () => {
    const manager = new ViewerTerminalManager()
    const win = createMockWindow()
    const created = manager.createTab(win, {
      tabId: 'tab-1',
      threadId: 'thread-1',
      cwd: 'C:\\repo',
      cols: 80,
      rows: 24
    })

    expect(created.tabId).toBe('tab-1')
    expect(created.pid).toBe(4321)
    expect(spawnMock).toHaveBeenCalledTimes(1)
    expect(created.shell).toBe(spawnMock.mock.calls[0][0])

    onDataHandler?.('hello\r\n')
    expect(vi.mocked(win.webContents.send)).toHaveBeenCalledWith(
      'viewer:terminal:data',
      { tabId: 'tab-1', data: 'hello\r\n' }
    )

    const attached = manager.attachTab(win, 'tab-1')
    expect(attached.buffer).toContain('hello')
    expect(attached.pid).toBe(4321)
    expect(attached.shell).toBe(created.shell)
  })

  it('writes/resizes active terminal and marks exit state', () => {
    const manager = new ViewerTerminalManager()
    const win = createMockWindow()
    manager.createTab(win, {
      tabId: 'tab-1',
      threadId: 'thread-1',
      cwd: 'C:\\repo',
      cols: 80,
      rows: 24
    })

    manager.write(win, { tabId: 'tab-1', data: 'echo hi\r' })
    manager.resize(win, { tabId: 'tab-1', cols: 120, rows: 40 })
    expect(mockPty.write).toHaveBeenCalledWith('echo hi\r')
    expect(mockPty.resize).toHaveBeenCalledWith(120, 40)

    onExitHandler?.({ exitCode: 0, signal: null })
    expect(vi.mocked(win.webContents.send)).toHaveBeenCalledWith(
      'viewer:terminal:exit',
      { tabId: 'tab-1', code: 0, signal: null }
    )

    const attached = manager.attachTab(win, 'tab-1')
    expect(attached.exited?.code).toBe(0)
  })

  it('destroys only tabs belonging to the requested thread', () => {
    const manager = new ViewerTerminalManager()
    const win = createMockWindow()
    const threadOneTabA = createMockPty()
    threadOneTabA.pid = 1001
    const threadOneTabB = createMockPty()
    threadOneTabB.pid = 1002
    const threadTwoTab = createMockPty()
    threadTwoTab.pid = 2001
    spawnMock
      .mockReset()
      .mockReturnValueOnce(threadOneTabA)
      .mockReturnValueOnce(threadOneTabB)
      .mockReturnValueOnce(threadTwoTab)

    manager.createTab(win, {
      tabId: 'tab-1',
      threadId: 'thread-1',
      cwd: 'C:\\repo',
      cols: 80,
      rows: 24
    })
    manager.createTab(win, {
      tabId: 'tab-2',
      threadId: 'thread-1',
      cwd: 'C:\\repo',
      cols: 80,
      rows: 24
    })
    manager.createTab(win, {
      tabId: 'tab-3',
      threadId: 'thread-2',
      cwd: 'C:\\repo',
      cols: 80,
      rows: 24
    })

    manager.destroyThread(win, 'thread-1')

    expect(threadOneTabA.kill).toHaveBeenCalledTimes(1)
    expect(threadOneTabB.kill).toHaveBeenCalledTimes(1)
    expect(threadTwoTab.kill).not.toHaveBeenCalled()
    expect(() => manager.attachTab(win, 'tab-1')).toThrow('Terminal tab not found')
    expect(() => manager.attachTab(win, 'tab-2')).toThrow('Terminal tab not found')
    expect(manager.attachTab(win, 'tab-3').pid).toBe(2001)
  })

  it('restarts a tab in the requested folder, runs the command, and ignores the old shell', () => {
    const manager = new ViewerTerminalManager()
    const win = createMockWindow()
    const handlers: Array<{ data?: (data: string) => void; exit?: (event: { exitCode: number; signal: number | null }) => void }> = []
    const ptys = [createMockPty(), createMockPty()].map((pty, index) => {
      pty.pid = 100 + index
      handlers[index] = {}
      pty.onData = (cb) => { handlers[index].data = cb }
      pty.onExit = (cb) => { handlers[index].exit = cb }
      return pty
    })
    spawnMock.mockReset().mockReturnValueOnce(ptys[0]).mockReturnValueOnce(ptys[1])

    manager.createTab(win, { tabId: 'run', threadId: 'thread-1', cwd: 'C:\\repo', cols: 80, rows: 24 })
    handlers[0].data?.('old output')
    const restarted = manager.restartTab(win, {
      tabId: 'run', threadId: 'thread-1', cwd: 'C:\\repo\\app', cols: 80, rows: 24, command: 'npm test'
    })

    expect(ptys[0].kill).toHaveBeenCalledTimes(1)
    expect(spawnMock.mock.calls[1][2]).toMatchObject({ cwd: 'C:\\repo\\app' })
    expect(restarted).toMatchObject({ tabId: 'run', pid: 101, cwd: 'C:\\repo\\app' })
    const written = vi.mocked(ptys[1].write).mock.calls[0][0]
    expect(written).toContain('npm test')
    expect(written.endsWith('\r')).toBe(true)

    vi.mocked(win.webContents.send).mockClear()
    handlers[0].exit?.({ exitCode: 1, signal: null })
    handlers[0].data?.('late output')
    expect(win.webContents.send).not.toHaveBeenCalled()
    expect(manager.attachTab(win, 'run')).toMatchObject({ pid: 101, buffer: '' })
    expect(manager.attachTab(win, 'run').exited).toBeUndefined()
  })

  it('starts a restarted shell with the requested variables on top of the app environment', () => {
    const manager = new ViewerTerminalManager()
    const win = createMockWindow()
    spawnMock.mockReset().mockReturnValueOnce(createMockPty())

    manager.restartTab(win, {
      tabId: 'setup',
      threadId: 'thread-1',
      cwd: 'C:\\repo',
      cols: 80,
      rows: 24,
      command: 'npm install',
      env: { DOTCRAFT_WORKTREE_PATH: 'C:\\repo' }
    })

    const env = spawnMock.mock.calls[0][2].env as Record<string, string>
    expect(env.DOTCRAFT_WORKTREE_PATH).toBe('C:\\repo')
    expect(env.PATH ?? env.Path).toBe(process.env.PATH ?? process.env.Path)
  })

  it('lists only the thread\'s tabs, keeping a restarted tab in its place', () => {
    const manager = new ViewerTerminalManager()
    const win = createMockWindow()
    spawnMock.mockReset().mockImplementation(() => createMockPty())
    const spawn = (tabId: string, threadId: string): void => {
      manager.createTab(win, { tabId, threadId, cwd: 'C:\\repo', cols: 80, rows: 24 })
    }

    spawn('first', 'thread-1')
    spawn('other-thread', 'thread-2')
    spawn('second', 'thread-1')
    manager.restartTab(win, { tabId: 'first', threadId: 'thread-1', cwd: 'C:\\repo', cols: 100, rows: 30, command: 'npm test' })
    manager.resize(win, { tabId: 'second', cols: 132, rows: 50 })

    const tabs = manager.threadTabs(win, 'thread-1')
    expect(tabs.map((tab) => tab.tabId)).toEqual(['first', 'second'])
    expect(tabs[0]).toMatchObject({ cols: 100, rows: 30 })
    expect(tabs[1]).toMatchObject({ cols: 132, rows: 50 })
  })

  it('moves tabs to the thread they are handed to', () => {
    const manager = new ViewerTerminalManager()
    const win = createMockWindow()
    manager.createTab(win, { tabId: 'tab-1', threadId: 'welcome:/workspace', cwd: 'C:\\repo', cols: 80, rows: 24 })

    manager.rebindThread(win, 'welcome:/workspace', 'thread-1')

    expect(manager.threadTabs(win, 'welcome:/workspace')).toEqual([])
    expect(manager.threadTabs(win, 'thread-1').map((tab) => tab.tabId)).toEqual(['tab-1'])
  })

  it('stops listing a tab once its shell exits', () => {
    const manager = new ViewerTerminalManager()
    const win = createMockWindow()
    manager.createTab(win, { tabId: 'tab-1', threadId: 'thread-1', cwd: 'C:\\repo', cols: 80, rows: 24 })

    onExitHandler?.({ exitCode: 1, signal: null })

    expect(manager.threadTabs(win, 'thread-1')).toEqual([])
  })

  it('resolves output waits on new output, exit, removal, or timeout', async () => {
    vi.useFakeTimers()
    try {
      const manager = new ViewerTerminalManager()
      const win = createMockWindow()
      manager.createTab(win, { tabId: 'tab-1', threadId: 'thread-1', cwd: 'C:\\repo', cols: 80, rows: 24 })

      const output = manager.waitForOutput(win, 'tab-1', 5_000)
      onDataHandler?.('ready\r\n')
      await expect(output).resolves.toBe('output')

      const timedOut = manager.waitForOutput(win, 'tab-1', 5_000)
      vi.advanceTimersByTime(5_000)
      await expect(timedOut).resolves.toBe('timeout')

      const exited = manager.waitForOutput(win, 'tab-1', 5_000)
      onExitHandler?.({ exitCode: 1, signal: null })
      await expect(exited).resolves.toBe('exit')
      await expect(manager.waitForOutput(win, 'tab-1', 5_000)).resolves.toBe('exit')

      manager.destroyTab(win, 'tab-1')
      await expect(manager.waitForOutput(win, 'tab-1', 5_000)).resolves.toBe('gone')
    } finally {
      vi.useRealTimers()
    }
  })
})
