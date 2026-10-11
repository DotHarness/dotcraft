import { mkdtemp, readFile, rm } from 'fs/promises'
import { tmpdir } from 'os'
import * as path from 'path'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { readCustomOpenTargets } from '../openTargets/customTargets'
import {
  createCommandLookup,
  detectWindowsTargets,
  type DetectionHost
} from '../openTargets/detection'
import { composeBuiltInLaunch, composeCustomLaunch, runLaunchCommand } from '../openTargets/launch'

const LOCAL = 'C:\\Users\\dev\\AppData\\Local'
const PF = 'C:\\Program Files'
const PF86 = 'C:\\Program Files (x86)'

function windowsHost(
  files: string[],
  options: { path?: string[]; directories?: Record<string, string[]>; vswhere?: string; wsl?: boolean } = {}
): DetectionHost {
  const known = new Set(files.map((file) => file.toLowerCase()))
  return {
    platform: 'win32',
    env: {
      LOCALAPPDATA: LOCAL,
      ProgramFiles: PF,
      'ProgramFiles(x86)': PF86,
      SystemRoot: 'C:\\Windows',
      PATH: (options.path ?? []).join(';'),
      PATHEXT: '.COM;.EXE;.BAT;.CMD'
    },
    exists: async (filePath) => known.has(filePath.toLowerCase()),
    listDirectory: async (directory) => options.directories?.[directory] ?? [],
    findWithVsWhere: async () => options.vswhere ?? null,
    hasWslDistribution: async () => options.wsl === true,
    gitBashFromRegistry: async () => null
  }
}

const file = { path: 'F:\\repo\\src\\app.ts', directory: 'F:\\repo\\src', isDirectory: false }
const at = { line: 12, column: 5 }

describe('Windows open target detection', () => {
  it.runIf(process.platform === 'win32')('lists only installed apps in the product order with File Explorer and a hidden default app', async () => {
    const targets = await detectWindowsTargets(windowsHost([
      `${LOCAL}\\Programs\\Microsoft VS Code\\bin\\code.cmd`,
      `${LOCAL}\\Programs\\Microsoft VS Code\\Code.exe`,
      `${PF}\\Zed\\Zed.exe`,
      `${PF}\\JetBrains\\Rider 2024.3\\bin\\rider64.exe`,
      'C:\\Windows\\explorer.exe'
    ], {
      path: [`${LOCAL}\\Programs\\Microsoft VS Code\\bin`],
      directories: { [`${PF}\\JetBrains`]: ['Rider 2024.3'] }
    }))

    expect(targets.map((target) => [target.id, target.hidden === true])).toEqual([
      ['vscode', false],
      ['zed', false],
      ['fileManager', false],
      ['rider', false],
      ['systemDefault', true]
    ])
    expect(targets[0]!.command).toBe(`${LOCAL}\\Programs\\Microsoft VS Code\\Code.exe`)
  })

  it.runIf(process.platform === 'win32')('finds the newest versioned JetBrains folder, Visual Studio through the installer, and the Cursor app behind its CLI shim', async () => {
    const vswhere = `${PF86}\\Microsoft Visual Studio\\Installer\\vswhere.exe`
    const devenv = 'D:\\VS\\Preview\\Common7\\IDE\\devenv.exe'
    const cursorRoot = `${LOCAL}\\Programs\\cursor`
    const targets = await detectWindowsTargets(windowsHost([
      vswhere,
      devenv,
      `${cursorRoot}\\resources\\app\\bin\\cursor.cmd`,
      `${cursorRoot}\\Cursor.exe`,
      `${PF}\\JetBrains\\IntelliJ IDEA 2023.1\\bin\\idea64.exe`,
      `${PF}\\JetBrains\\IntelliJ IDEA 2024.2\\bin\\idea64.exe`
    ], {
      path: [`${cursorRoot}\\resources\\app\\bin`],
      directories: { [`${PF}\\JetBrains`]: ['IntelliJ IDEA 2023.1', 'IntelliJ IDEA 2024.2'] },
      vswhere: devenv
    }))

    const byId = new Map(targets.map((target) => [target.id, target.command]))
    expect(byId.get('visualStudio')).toBe(devenv)
    expect(byId.get('cursor')).toBe(`${cursorRoot}\\Cursor.exe`)
    expect(byId.get('intellij')).toBe(`${PF}\\JetBrains\\IntelliJ IDEA 2024.2\\bin\\idea64.exe`)
  })

  it.runIf(process.platform === 'win32')('offers WSL only when a distribution is installed and Windows Terminal is present', async () => {
    const terminal = `${LOCAL}\\Microsoft\\WindowsApps\\wt.exe`
    const withoutDistro = await detectWindowsTargets(windowsHost([terminal]))
    const withDistro = await detectWindowsTargets(windowsHost([terminal], { wsl: true }))
    const withoutTerminal = await detectWindowsTargets(windowsHost([], { wsl: true }))

    expect(withoutDistro.map((target) => target.id)).toContain('windowsTerminal')
    expect(withoutDistro.map((target) => target.id)).not.toContain('wsl')
    expect(withoutTerminal.map((target) => target.id)).not.toContain('wsl')
    const wsl = withDistro.find((target) => target.id === 'wsl')!
    expect(composeBuiltInLaunch(wsl, file, null)).toEqual({ command: terminal, args: ['-d', 'F:\\repo\\src', 'wsl.exe'] })
  })
})

describe('open target launch arguments', () => {
  it.runIf(process.platform === 'win32')('passes a position in each editor family format and opens other targets at the path or folder', async () => {
    const targets = await detectWindowsTargets(windowsHost([
      `${PF}\\Microsoft VS Code\\Code.exe`,
      `${PF}\\JetBrains\\PyCharm 2024.1\\bin\\pycharm64.exe`,
      `${PF}\\Sublime Text\\sublime_text.exe`,
      `${PF}\\Microsoft Visual Studio\\2022\\Community\\Common7\\IDE\\devenv.exe`,
      `${LOCAL}\\GitHubDesktop\\GitHubDesktop.exe`
    ], { directories: { [`${PF}\\JetBrains`]: ['PyCharm 2024.1'] } }))
    const launch = (id: string, location: typeof at | null) =>
      composeBuiltInLaunch(targets.find((target) => target.id === id)!, file, location).args

    expect(launch('vscode', at)).toEqual(['--goto', 'F:\\repo\\src\\app.ts:12:5'])
    expect(launch('vscode', null)).toEqual(['F:\\repo\\src\\app.ts'])
    expect(launch('pycharm', at)).toEqual(['--line', '12', '--column', '5', 'F:\\repo\\src\\app.ts'])
    expect(launch('sublimeText', at)).toEqual(['F:\\repo\\src\\app.ts:12:5'])
    expect(launch('visualStudio', at)).toEqual(['F:\\repo\\src\\app.ts'])
    expect(launch('githubDesktop', at)).toEqual(['F:\\repo\\src'])
  })

  it('composes custom handler input for path, JSON argument, and JSON on standard input', () => {
    const base = { id: 'custom:review', label: 'Review', command: 'reviewer', args: ['--open'] }
    const payload = JSON.stringify({ target: 'custom:review', path: file.path, location: at })

    expect(composeCustomLaunch({ ...base, input: 'path' }, file, at))
      .toEqual({ command: 'reviewer', args: ['--open', file.path] })
    expect(composeCustomLaunch({ ...base, input: 'jsonArgument' }, file, at))
      .toEqual({ command: 'reviewer', args: ['--open', payload] })
    expect(composeCustomLaunch({ ...base, input: 'jsonStdin' }, file, null))
      .toEqual({
        command: 'reviewer',
        args: ['--open'],
        stdin: JSON.stringify({ target: 'custom:review', path: file.path, location: null })
      })
  })
})

describe('custom open targets', () => {
  const tool = process.platform === 'win32' ? 'C:\\Tools\\review.exe' : '/opt/tools/review'
  const lookup = createCommandLookup({
    platform: process.platform,
    env: { PATH: process.platform === 'win32' ? 'C:\\Bin' : '/usr/local/bin', PATHEXT: '.EXE;.CMD' },
    exists: async (filePath) => [tool, process.platform === 'win32' ? 'C:\\Bin\\lens.exe' : '/usr/local/bin/lens'].includes(filePath)
  })

  it('reads valid handlers from the Desktop section and skips invalid ones with a log line', async () => {
    const warn = vi.fn()
    const targets = await readCustomOpenTargets({
      desktop: {
        customFileHandlers: {
          review: { Label: 'Review', Command: tool, Args: ['--open'], Input: 'jsonStdin', Icon: 'data:image/png;base64,AAA' },
          lens: { label: 'Lens', command: 'lens' },
          '-bad': { Label: 'Bad id', Command: tool },
          missing: { Label: 'Missing', Command: process.platform === 'win32' ? 'C:\\Tools\\gone.exe' : '/opt/gone' },
          'bad-input': { Label: 'Input', Command: tool, Input: 'socket' },
          'bad-args': { Label: 'Args', Command: tool, Args: [1] },
          nolabel: { Command: tool }
        }
      }
    }, lookup, warn)

    expect(targets).toEqual([
      { id: 'custom:review', label: 'Review', icon: 'data:image/png;base64,AAA', command: tool, args: ['--open'], input: 'jsonStdin' },
      {
        id: 'custom:lens',
        label: 'Lens',
        command: process.platform === 'win32' ? 'C:\\Bin\\lens.exe' : '/usr/local/bin/lens',
        args: [],
        input: 'path'
      }
    ])
    expect(warn).toHaveBeenCalledTimes(5)
  })

  it('returns nothing when the configuration has no handlers', async () => {
    await expect(readCustomOpenTargets(null, lookup, vi.fn())).resolves.toEqual([])
    await expect(readCustomOpenTargets({ Desktop: {} }, lookup, vi.fn())).resolves.toEqual([])
  })
})

describe('custom handler process input', () => {
  let directory: string | null = null

  afterEach(async () => {
    if (directory) await rm(directory, { recursive: true, force: true })
    directory = null
  })

  it('writes the JSON payload to the handler standard input', async () => {
    directory = await mkdtemp(path.join(tmpdir(), 'dotcraft-open-target-'))
    const output = path.join(directory, 'stdin.json')
    const script = `let d='';process.stdin.on('data',c=>d+=c).on('end',()=>require('fs').writeFileSync(${JSON.stringify(output)},d))`
    const launch = composeCustomLaunch(
      { id: 'custom:capture', label: 'Capture', command: process.execPath, args: ['-e', script], input: 'jsonStdin' },
      { path: output, directory, isDirectory: false },
      { line: 3, column: 1 }
    )

    await runLaunchCommand(launch, directory)

    await vi.waitFor(async () => {
      expect(JSON.parse(await readFile(output, 'utf8'))).toEqual({
        target: 'custom:capture',
        path: output,
        location: { line: 3, column: 1 }
      })
    }, { timeout: 10_000, interval: 100 })
  })
})
