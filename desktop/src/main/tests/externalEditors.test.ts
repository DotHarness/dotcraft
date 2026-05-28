import { beforeEach, describe, expect, it, vi } from 'vitest'
import { EventEmitter } from 'events'
import { shell } from 'electron'

const accessMock = vi.hoisted(() => vi.fn())
const statMock = vi.hoisted(() => vi.fn())
const execFileMock = vi.hoisted(() => vi.fn())
const spawnMock = vi.hoisted(() => vi.fn())
const getFileIconMock = vi.hoisted(() => vi.fn())

vi.mock('fs/promises', () => ({
  access: accessMock,
  stat: statMock
}))

vi.mock('child_process', () => ({
  execFile: execFileMock,
  spawn: spawnMock
}))

vi.mock('electron', () => ({
  app: {
    getFileIcon: getFileIconMock
  },
  shell: {
    openPath: vi.fn().mockResolvedValue('')
  }
}))

import { clearEditorDetectionCache, detectEditors, launchEditor } from '../externalEditors'

function setupWhereCommand(responses: Record<string, string | null>): void {
  const lookupBinary = process.platform === 'win32' ? 'where.exe' : 'which'
  execFileMock.mockImplementation(
    (
      command: string,
      args: string[],
      _options: unknown,
      callback: (error: Error | null, stdout: string) => void
    ) => {
      if (command !== lookupBinary) {
        callback(new Error('not found'), '')
        return
      }
      const key = args[0]
      const response = responses[key]
      if (typeof response === 'string' && response.length > 0) {
        callback(null, `${response}\n`)
        return
      }
      callback(new Error('not found'), '')
    }
  )
}

describe('externalEditors icon extraction', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    clearEditorDetectionCache()
    statMock.mockRejectedValue(new Error('missing'))
    spawnMock.mockImplementation(() => {
      const child = new EventEmitter() as EventEmitter & { unref: () => void }
      child.unref = vi.fn()
      queueMicrotask(() => child.emit('spawn'))
      return child
    })
  })

  it('adds iconDataUrl for detected editors with executable paths', async () => {
    const cursorExecutable = process.platform === 'win32' ? 'C:\\Tools\\Cursor.exe' : '/usr/bin/cursor'
    const explorerExecutable = process.platform === 'win32'
      ? `${process.env.SystemRoot ?? 'C:\\Windows'}\\explorer.exe`
      : ''
    setupWhereCommand({
      cursor: cursorExecutable,
      'cursor.cmd': cursorExecutable,
      'Cursor.exe': cursorExecutable
    })
    accessMock.mockImplementation(async (targetPath: string) => {
      if (targetPath === cursorExecutable || (explorerExecutable && targetPath === explorerExecutable)) {
        return
      }
      throw new Error('missing')
    })
    getFileIconMock.mockImplementation(async (targetPath: string) => ({
      isEmpty: () => false,
      toDataURL: () => `data:image/png;base64,${targetPath}`
    }))

    const result = await detectEditors()

    const cursor = result.find((entry) => entry.id === 'cursor')
    expect(cursor?.iconDataUrl).toBe(`data:image/png;base64,${cursorExecutable}`)
    if (process.platform === 'win32') {
      const explorer = result.find((entry) => entry.id === 'explorer')
      expect(explorer?.iconDataUrl).toBe(`data:image/png;base64,${explorerExecutable}`)
    }
  })

  it('keeps iconDataUrl undefined when getFileIcon throws', async () => {
    const cursorExecutable = process.platform === 'win32' ? 'C:\\Tools\\Cursor.exe' : '/usr/bin/cursor'
    setupWhereCommand({
      cursor: cursorExecutable,
      'cursor.cmd': cursorExecutable,
      'Cursor.exe': cursorExecutable
    })
    accessMock.mockImplementation(async (targetPath: string) => {
      if (targetPath === cursorExecutable) return
      if (process.platform === 'win32' && targetPath === 'C:\\Windows\\explorer.exe') return
      throw new Error('missing')
    })
    getFileIconMock.mockImplementation(async (targetPath: string) => {
      if (targetPath === cursorExecutable) throw new Error('icon error')
      return {
        isEmpty: () => false,
        toDataURL: () => `data:image/png;base64,${targetPath}`
      }
    })

    const result = await detectEditors()

    const cursor = result.find((entry) => entry.id === 'cursor')
    expect(cursor?.iconDataUrl).toBeUndefined()
    const explorer = result.find((entry) => entry.id === 'explorer')
    if (process.platform === 'win32') {
      expect(explorer?.iconDataUrl).toBe(`data:image/png;base64,${process.env.SystemRoot ?? 'C:\\Windows'}\\explorer.exe`)
    } else {
      expect(explorer?.iconDataUrl).toBeUndefined()
    }
  })

  it('prefers Cursor.exe over the cursor.cmd CLI shim', async () => {
    if (process.platform !== 'win32') return
    const localAppData = 'C:\\Users\\tester\\AppData\\Local'
    process.env.LOCALAPPDATA = localAppData
    const cursorExecutable = `${localAppData}\\Programs\\cursor\\Cursor.exe`
    const cursorCmd = `${localAppData}\\Programs\\cursor\\resources\\app\\bin\\cursor.cmd`
    setupWhereCommand({
      cursor: cursorCmd,
      'cursor.cmd': cursorCmd
    })
    accessMock.mockImplementation(async (targetPath: string) => {
      if (targetPath === cursorExecutable) return
      if (targetPath === cursorCmd) return
      if (targetPath === 'C:\\Windows\\explorer.exe') return
      throw new Error('missing')
    })
    getFileIconMock.mockImplementation(async (targetPath: string) => ({
      isEmpty: () => false,
      toDataURL: () => `data:image/png;base64,${targetPath}`
    }))

    const result = await detectEditors()

    const cursor = result.find((entry) => entry.id === 'cursor')
    expect(cursor?.iconDataUrl).toBe(`data:image/png;base64,${cursorExecutable}`)
    expect(getFileIconMock).toHaveBeenCalledWith(cursorExecutable, { size: 'normal' })
  })

  it('uses shell:true when launching a .cmd command', async () => {
    if (process.platform !== 'win32') return
    const localAppData = 'C:\\Users\\tester\\AppData\\Local'
    process.env.LOCALAPPDATA = localAppData
    const cursorCmd = `${localAppData}\\Programs\\cursor\\resources\\app\\bin\\cursor.cmd`
    setupWhereCommand({
      cursor: cursorCmd,
      'cursor.cmd': cursorCmd
    })
    accessMock.mockImplementation(async (targetPath: string) => {
      if (targetPath === cursorCmd) return
      if (targetPath === 'C:\\Windows\\explorer.exe') return
      throw new Error('missing')
    })
    getFileIconMock.mockResolvedValue({
      isEmpty: () => true,
      toDataURL: () => ''
    })
    statMock.mockImplementation(async (targetPath: string) => ({
      isDirectory: () => targetPath === 'F:\\workspace with spaces'
    }))

    await launchEditor('cursor', 'F:\\workspace with spaces')

    expect(spawnMock).toHaveBeenCalledTimes(1)
    const [command, args, options] = spawnMock.mock.calls[0]
    expect(command).toBe(`"${cursorCmd}"`)
    expect(args).toEqual(['"F:\\workspace with spaces"'])
    expect(options).toMatchObject({
      shell: true,
      windowsVerbatimArguments: true,
      cwd: 'F:\\workspace with spaces',
      detached: true,
      stdio: 'ignore'
    })
  })

  it('uses the parent directory as cwd when launching a file target', async () => {
    const cursorExecutable = process.platform === 'win32' ? 'C:\\Tools\\Cursor.exe' : '/usr/bin/cursor'
    const filePath = process.platform === 'win32'
      ? 'F:\\workspace\\README.md'
      : '/workspace/README.md'
    const parentPath = process.platform === 'win32'
      ? 'F:\\workspace'
      : '/workspace'
    setupWhereCommand({
      cursor: cursorExecutable,
      'cursor.cmd': cursorExecutable,
      'Cursor.exe': cursorExecutable
    })
    accessMock.mockImplementation(async (targetPath: string) => {
      if (targetPath === cursorExecutable) return
      if (process.platform === 'win32' && targetPath === `${process.env.SystemRoot ?? 'C:\\Windows'}\\explorer.exe`) return
      throw new Error('missing')
    })
    statMock.mockImplementation(async (targetPath: string) => ({
      isDirectory: () => targetPath === parentPath
    }))
    getFileIconMock.mockResolvedValue({
      isEmpty: () => true,
      toDataURL: () => ''
    })

    await launchEditor('cursor', filePath)

    expect(spawnMock).toHaveBeenCalledTimes(1)
    const [_command, args, options] = spawnMock.mock.calls[0]
    expect(args).toEqual([filePath])
    expect(options).toMatchObject({ cwd: parentPath })
  })

  it('rejects when the spawned editor emits an error', async () => {
    const cursorExecutable = process.platform === 'win32' ? 'C:\\Tools\\Cursor.exe' : '/usr/bin/cursor'
    const filePath = process.platform === 'win32'
      ? 'F:\\workspace\\README.md'
      : '/workspace/README.md'
    setupWhereCommand({
      cursor: cursorExecutable,
      'cursor.cmd': cursorExecutable,
      'Cursor.exe': cursorExecutable
    })
    accessMock.mockImplementation(async (targetPath: string) => {
      if (targetPath === cursorExecutable) return
      if (process.platform === 'win32' && targetPath === 'C:\\Windows\\explorer.exe') return
      throw new Error('missing')
    })
    statMock.mockResolvedValue({ isDirectory: () => false })
    getFileIconMock.mockResolvedValue({
      isEmpty: () => true,
      toDataURL: () => ''
    })
    spawnMock.mockImplementationOnce(() => {
      const child = new EventEmitter() as EventEmitter & { unref: () => void }
      child.unref = vi.fn()
      queueMicrotask(() => child.emit('error', new Error('spawn failed')))
      return child
    })

    await expect(launchEditor('cursor', filePath)).rejects.toThrow('spawn failed')
  })

  it('opens file targets directly with the default OS handler for explorer', async () => {
    const filePath = process.platform === 'win32'
      ? 'F:\\workspace\\README.md'
      : '/workspace/README.md'
    statMock.mockResolvedValue({ isDirectory: () => false })

    await launchEditor('explorer', filePath)

    expect(shell.openPath).toHaveBeenCalledWith(filePath)
    expect(spawnMock).not.toHaveBeenCalled()
  })

  it('prefers git-bash.exe icon when where git-bash resolves to cmd shim', async () => {
    if (process.platform !== 'win32') return
    const originalProgramFiles = process.env.ProgramFiles
    const programFiles = 'C:\\Program Files'
    const gitBashExe = `${programFiles}\\Git\\git-bash.exe`
    const gitBashCmd = `${programFiles}\\Git\\cmd\\git-bash.cmd`
    process.env.ProgramFiles = programFiles
    setupWhereCommand({
      'git-bash': gitBashCmd
    })
    accessMock.mockImplementation(async (targetPath: string) => {
      if (targetPath === gitBashExe) return
      if (targetPath === gitBashCmd) return
      if (targetPath === 'C:\\Windows\\explorer.exe') return
      throw new Error('missing')
    })
    getFileIconMock.mockImplementation(async (targetPath: string) => ({
      isEmpty: () => false,
      toDataURL: () => `data:image/png;base64,${targetPath}`
    }))

    try {
      const result = await detectEditors()

      const gitBash = result.find((entry) => entry.id === 'git-bash')
      expect(gitBash?.iconDataUrl).toBe(`data:image/png;base64,${gitBashExe}`)
      expect(getFileIconMock).toHaveBeenCalledWith(gitBashExe, { size: 'normal' })
    } finally {
      process.env.ProgramFiles = originalProgramFiles
    }
  })
})
