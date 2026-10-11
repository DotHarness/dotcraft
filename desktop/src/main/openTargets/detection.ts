import * as path from 'path'
import type { OpenTargetIconKey, OpenTargetLocation } from '../../shared/openTargets'

export interface LaunchPath {
  path: string
  directory: string
  isDirectory: boolean
}

export type BuiltInOpener = 'fileManager' | 'systemDefault'

export interface BuiltInOpenTarget {
  id: string
  label: string
  labelKey?: string
  iconKey: OpenTargetIconKey
  command: string
  iconPath?: string
  hidden?: boolean
  opener?: BuiltInOpener
  args: (target: LaunchPath, location: OpenTargetLocation | null) => string[]
}

export interface DetectionHost {
  platform: NodeJS.Platform
  env: Record<string, string | undefined>
  exists(filePath: string): Promise<boolean>
  listDirectory(directory: string): Promise<string[]>
  findWithVsWhere(vswhere: string, pattern: string): Promise<string | null>
  hasWslDistribution(): Promise<boolean>
  gitBashFromRegistry(): Promise<string | null>
}

export const vscodeFamilyArgs = (target: LaunchPath, location: OpenTargetLocation | null): string[] =>
  location ? ['--goto', `${target.path}:${location.line}:${location.column}`] : [target.path]

export const jetbrainsArgs = (target: LaunchPath, location: OpenTargetLocation | null): string[] =>
  location
    ? ['--line', String(location.line), '--column', String(location.column), target.path]
    : [target.path]

export const positionSuffixArgs = (target: LaunchPath, location: OpenTargetLocation | null): string[] =>
  [location ? `${target.path}:${location.line}:${location.column}` : target.path]

const pathArgs = (target: LaunchPath): string[] => [target.path]
const directoryArgs = (target: LaunchPath): string[] => [target.directory]
const windowsTerminalArgs = (target: LaunchPath): string[] => ['-d', target.directory.replaceAll(';', '\\;')]

export interface CommandLookup {
  which(command: string): Promise<string | null>
  installed(candidates: string[][]): Promise<string | null>
  exists(filePath: string): Promise<boolean>
}

export function createCommandLookup(host: Pick<DetectionHost, 'platform' | 'env' | 'exists'>): CommandLookup {
  const separator = host.platform === 'win32' ? ';' : ':'
  const pathValue = host.env.PATH ?? host.env.Path ?? ''
  const directories = pathValue.split(separator).map((entry) => entry.trim()).filter(Boolean)
  const extensions = host.platform === 'win32'
    ? (host.env.PATHEXT ?? '.COM;.EXE;.BAT;.CMD').split(';').map((ext) => ext.trim().toLowerCase()).filter(Boolean)
    : []
  const localAppData = host.env.LOCALAPPDATA ?? ''
  const installRoots = [
    localAppData ? path.join(localAppData, 'Programs') : null,
    host.env.ProgramFiles ?? null,
    host.env['ProgramFiles(x86)'] ?? null
  ].filter((root): root is string => !!root)

  async function firstExisting(candidates: string[]): Promise<string | null> {
    for (const candidate of candidates) {
      if (await host.exists(candidate)) return candidate
    }
    return null
  }

  return {
    exists: (filePath) => host.exists(filePath),
    async which(command) {
      const names = path.extname(command) || extensions.length === 0
        ? [command]
        : extensions.map((ext) => command + ext)
      for (const directory of directories) {
        const found = await firstExisting(names.map((name) => path.join(directory, name)))
        if (found) return found
      }
      return null
    },
    installed(candidates) {
      return firstExisting(installRoots.flatMap((root) => candidates.map((parts) => path.join(root, ...parts))))
    }
  }
}

async function siblingExecutable(lookup: CommandLookup, found: string, executable: string): Promise<string | null> {
  const directory = path.dirname(found)
  if (path.basename(directory).toLowerCase() === 'bin') {
    const besideBin = path.join(path.dirname(directory), executable)
    if (await lookup.exists(besideBin)) return besideBin
  }
  const beside = path.join(directory, executable)
  return (await lookup.exists(beside)) ? beside : null
}

async function appFromCliShim(lookup: CommandLookup, found: string, executable: string): Promise<string> {
  if (path.basename(found).toLowerCase() === executable.toLowerCase()) return found
  const parts = path.normalize(found).split(path.sep)
  const tail = parts.slice(-4, -1).map((part) => part.toLowerCase())
  if (tail.join('/') === 'resources/app/bin') {
    const app = path.join(parts.slice(0, -4).join(path.sep), executable)
    if (await lookup.exists(app)) return app
  }
  return found
}

async function firstOnPath(lookup: CommandLookup, commands: string[]): Promise<string | null> {
  for (const command of commands) {
    const found = await lookup.which(command)
    if (found) return found
  }
  return null
}

const VISUAL_STUDIO_YEARS = ['2022', '2019', '2017']
const VISUAL_STUDIO_EDITIONS = ['Community', 'Professional', 'Enterprise', 'Preview', 'BuildTools']

interface JetBrainsProduct {
  id: string
  label: string
  pathCommands: string[]
  folderPrefixes: string[]
  executables: string[]
  fallbacks?: string[][]
}

const JETBRAINS_PRODUCTS: JetBrainsProduct[] = [
  {
    id: 'androidStudio',
    label: 'Android Studio',
    pathCommands: ['studio64.exe', 'studio.exe', 'studio'],
    folderPrefixes: ['android studio'],
    executables: ['studio64.exe', 'studio.exe'],
    fallbacks: [['Android', 'Android Studio', 'bin', 'studio64.exe'], ['Android', 'Android Studio', 'bin', 'studio.exe']]
  },
  {
    id: 'intellij',
    label: 'IntelliJ IDEA',
    pathCommands: ['idea64.exe', 'idea.exe', 'idea'],
    folderPrefixes: ['intellij idea', 'idea'],
    executables: ['idea64.exe', 'idea.exe']
  },
  {
    id: 'rider',
    label: 'Rider',
    pathCommands: ['rider64.exe', 'rider.exe', 'rider'],
    folderPrefixes: ['rider'],
    executables: ['rider64.exe', 'rider.exe']
  },
  {
    id: 'pycharm',
    label: 'PyCharm',
    pathCommands: ['pycharm64.exe', 'pycharm.exe', 'pycharm'],
    folderPrefixes: ['pycharm'],
    executables: ['pycharm64.exe', 'pycharm.exe']
  },
  {
    id: 'webstorm',
    label: 'WebStorm',
    pathCommands: ['webstorm64.exe', 'webstorm.exe', 'webstorm'],
    folderPrefixes: ['webstorm'],
    executables: ['webstorm64.exe', 'webstorm.exe']
  },
  {
    id: 'phpstorm',
    label: 'PhpStorm',
    pathCommands: ['phpstorm64.exe', 'phpstorm.exe', 'phpstorm'],
    folderPrefixes: ['phpstorm'],
    executables: ['phpstorm64.exe', 'phpstorm.exe']
  }
]

async function findJetBrainsProduct(host: DetectionHost, lookup: CommandLookup, product: JetBrainsProduct): Promise<string | null> {
  const onPath = await firstOnPath(lookup, product.pathCommands)
  if (onPath) return onPath
  const roots = [host.env.ProgramFiles, host.env['ProgramFiles(x86)']]
    .filter((root): root is string => !!root)
    .map((root) => path.join(root, 'JetBrains'))
  for (const root of roots) {
    const folders = (await host.listDirectory(root))
      .filter((name) => product.folderPrefixes.some((prefix) => name.toLowerCase().startsWith(prefix)))
      .sort((left, right) => right.localeCompare(left, undefined, { numeric: true }))
    for (const folder of folders) {
      for (const executable of product.executables) {
        const candidate = path.join(root, folder, 'bin', executable)
        if (await lookup.exists(candidate)) return candidate
      }
    }
  }
  return product.fallbacks ? lookup.installed(product.fallbacks) : null
}

async function findVisualStudio(host: DetectionHost, lookup: CommandLookup): Promise<string | null> {
  const onPath = await firstOnPath(lookup, ['devenv.exe', 'devenv'])
  if (onPath) return onPath
  const installed = await lookup.installed(VISUAL_STUDIO_YEARS.flatMap((year) =>
    VISUAL_STUDIO_EDITIONS.map((edition) => ['Microsoft Visual Studio', year, edition, 'Common7', 'IDE', 'devenv.exe'])))
  if (installed) return installed
  for (const root of [host.env['ProgramFiles(x86)'], host.env.ProgramFiles]) {
    if (!root) continue
    const vswhere = path.join(root, 'Microsoft Visual Studio', 'Installer', 'vswhere.exe')
    if (!(await lookup.exists(vswhere))) continue
    const found = await host.findWithVsWhere(vswhere, '**\\Common7\\IDE\\devenv.exe')
    if (found && await lookup.exists(found)) return found
  }
  return null
}

async function findWindowsTerminal(host: DetectionHost, lookup: CommandLookup): Promise<string | null> {
  const onPath = await lookup.which('wt.exe')
  if (onPath) return onPath
  const localAppData = host.env.LOCALAPPDATA
  if (!localAppData) return null
  const alias = path.join(localAppData, 'Microsoft', 'WindowsApps', 'wt.exe')
  return (await lookup.exists(alias)) ? alias : null
}

function editor(id: string, label: string, command: string, args: BuiltInOpenTarget['args']): BuiltInOpenTarget {
  return { id, label, iconKey: 'editor', command, args }
}

function terminal(id: string, label: string, command: string, args: BuiltInOpenTarget['args'], iconPath?: string): BuiltInOpenTarget {
  return { id, label, iconKey: 'terminal', command, args, ...(iconPath ? { iconPath } : {}) }
}

function fileManagerTarget(command: string): BuiltInOpenTarget {
  return {
    id: 'fileManager',
    label: 'File Explorer',
    labelKey: 'openTargets.fileManager',
    iconKey: 'fileManager',
    command,
    opener: 'fileManager',
    args: pathArgs
  }
}

const SYSTEM_DEFAULT_TARGET: BuiltInOpenTarget = {
  id: 'systemDefault',
  label: 'Default app',
  labelKey: 'conversation.reference.defaultApp',
  iconKey: 'fileManager',
  command: '',
  hidden: true,
  opener: 'systemDefault',
  args: pathArgs
}

export async function detectWindowsTargets(host: DetectionHost): Promise<BuiltInOpenTarget[]> {
  const lookup = createCommandLookup(host)
  const localAppData = host.env.LOCALAPPDATA ?? ''
  const result: Array<BuiltInOpenTarget | null> = []

  const vscodeLike = async (command: string, executable: string, installDir: string): Promise<string | null> => {
    const onPath = await lookup.which(command)
    const fromPath = onPath
      ? (await siblingExecutable(lookup, onPath, executable))
        ?? (path.basename(onPath).toLowerCase() === executable.toLowerCase() ? onPath : null)
      : null
    return fromPath ?? lookup.installed([[installDir, executable]])
  }

  const vscode = await vscodeLike('code', 'Code.exe', 'Microsoft VS Code')
  result.push(vscode ? editor('vscode', 'VS Code', vscode, vscodeFamilyArgs) : null)

  const insiders = await vscodeLike('code-insiders', 'Code - Insiders.exe', 'Microsoft VS Code Insiders')
  result.push(insiders ? editor('vscodeInsiders', 'VS Code Insiders', insiders, vscodeFamilyArgs) : null)

  const visualStudio = await findVisualStudio(host, lookup)
  result.push(visualStudio ? editor('visualStudio', 'Visual Studio', visualStudio, pathArgs) : null)

  const cursorOnPath = await lookup.which('cursor')
  const cursor = cursorOnPath
    ? await appFromCliShim(lookup, cursorOnPath, 'Cursor.exe')
    : await lookup.installed([['Cursor', 'Cursor.exe']])
  result.push(cursor ? editor('cursor', 'Cursor', cursor, vscodeFamilyArgs) : null)

  const sublimeOnPath = await firstOnPath(lookup, ['subl.exe', 'subl'])
  const sublime = sublimeOnPath
    ? (await siblingExecutable(lookup, sublimeOnPath, 'sublime_text.exe')) ?? sublimeOnPath
    : await lookup.installed([['Sublime Text', 'sublime_text.exe'], ['Sublime Text', 'subl.exe']])
  result.push(sublime ? editor('sublimeText', 'Sublime Text', sublime, positionSuffixArgs) : null)

  const zed = (await firstOnPath(lookup, ['zed.exe', 'zed'])) ?? await lookup.installed([['Zed', 'Zed.exe']])
  result.push(zed ? editor('zed', 'Zed', zed, positionSuffixArgs) : null)

  const antigravityOnPath = await firstOnPath(lookup, ['antigravity.exe', 'antigravity.cmd', 'antigravity'])
  const antigravity = antigravityOnPath
    ? await appFromCliShim(lookup, antigravityOnPath, 'Antigravity.exe')
    : await lookup.installed([
      ['Antigravity', 'Antigravity.exe'],
      ['Antigravity', 'bin', 'antigravity.cmd'],
      ['Antigravity', 'resources', 'app', 'bin', 'antigravity.cmd']
    ])
  result.push(antigravity ? editor('antigravity', 'Antigravity', antigravity, vscodeFamilyArgs) : null)

  const githubOnPath = await firstOnPath(lookup, ['github.exe', 'github'])
  const githubDesktop = githubOnPath
    ? (await siblingExecutable(lookup, githubOnPath, 'GitHubDesktop.exe')) ?? githubOnPath
    : (localAppData && await lookup.exists(path.join(localAppData, 'GitHubDesktop', 'GitHubDesktop.exe'))
      ? path.join(localAppData, 'GitHubDesktop', 'GitHubDesktop.exe')
      : await lookup.installed([['GitHub Desktop', 'GitHubDesktop.exe'], ['GitHubDesktop', 'GitHubDesktop.exe']]))
  result.push(githubDesktop ? editor('githubDesktop', 'GitHub Desktop', githubDesktop, directoryArgs) : null)

  const systemRoot = host.env.SystemRoot ?? host.env.windir
  const explorer = systemRoot ? path.join(systemRoot, 'explorer.exe') : null
  result.push(fileManagerTarget(explorer && await lookup.exists(explorer) ? explorer : 'explorer.exe'))

  const windowsTerminal = await findWindowsTerminal(host, lookup)
  result.push(windowsTerminal ? terminal('windowsTerminal', 'Windows Terminal', windowsTerminal, windowsTerminalArgs) : null)

  const gitBashOnPath = await firstOnPath(lookup, ['git-bash.exe', 'git-bash'])
  const gitBash = gitBashOnPath
    ? (await siblingExecutable(lookup, gitBashOnPath, 'git-bash.exe')) ?? gitBashOnPath
    : (await lookup.installed([['Git', 'git-bash.exe']])) ?? await host.gitBashFromRegistry()
  result.push(gitBash ? terminal('gitBash', 'Git Bash', gitBash, (target) => [`--cd=${target.directory}`]) : null)

  const cmderRoot = host.env.CMDER_ROOT?.trim()
  const cmderFromRoot = cmderRoot ? path.join(cmderRoot, 'Cmder.exe') : null
  const cmder = (cmderFromRoot && await lookup.exists(cmderFromRoot) ? cmderFromRoot : null)
    ?? await firstOnPath(lookup, ['cmder.exe', 'cmder'])
  result.push(cmder ? terminal('cmder', 'Cmder', cmder, (target) => ['/START', target.directory]) : null)

  const wslIcon = systemRoot ? path.join(systemRoot, 'System32', 'wsl.exe') : undefined
  const wsl = windowsTerminal && await host.hasWslDistribution() ? windowsTerminal : null
  result.push(wsl
    ? terminal('wsl', 'WSL', wsl, (target) => [...windowsTerminalArgs(target), 'wsl.exe'], wslIcon)
    : null)

  for (const product of JETBRAINS_PRODUCTS) {
    const found = await findJetBrainsProduct(host, lookup, product)
    result.push(found ? editor(product.id, product.label, found, jetbrainsArgs) : null)
  }

  result.push(SYSTEM_DEFAULT_TARGET)
  return result.filter((entry): entry is BuiltInOpenTarget => entry !== null)
}

export async function detectUnixTargets(host: DetectionHost): Promise<BuiltInOpenTarget[]> {
  const lookup = createCommandLookup(host)
  const result: BuiltInOpenTarget[] = [fileManagerTarget('')]
  const candidates: Array<{ id: string; label: string; command: string; args: BuiltInOpenTarget['args'] }> = [
    { id: 'cursor', label: 'Cursor', command: 'cursor', args: vscodeFamilyArgs },
    { id: 'vscode', label: 'VS Code', command: 'code', args: vscodeFamilyArgs },
    { id: 'intellij', label: 'IntelliJ IDEA', command: 'idea', args: jetbrainsArgs }
  ]
  for (const candidate of candidates) {
    const found = await lookup.which(candidate.command)
    if (found) result.push(editor(candidate.id, candidate.label, found, candidate.args))
  }
  result.push(SYSTEM_DEFAULT_TARGET)
  return result
}
