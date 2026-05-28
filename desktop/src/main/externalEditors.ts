import { access, stat } from 'fs/promises'
import * as path from 'path'
import { execFile, spawn, type ChildProcess, type SpawnOptions } from 'child_process'
import { app, shell } from 'electron'

export type EditorId =
  | 'explorer'
  | 'vs'
  | 'cursor'
  | 'vscode'
  | 'rider'
  | 'webstorm'
  | 'idea'
  | 'github-desktop'
  | 'git-bash'
  | 'terminal'

export interface EditorInfo {
  id: EditorId
  labelKey: string
  iconKey: string
  iconDataUrl?: string
}

interface EditorDescriptor extends EditorInfo {
  command: string
  args: (targetPath: string, cwd: string) => string[]
}

interface ResolvedLaunchTarget {
  targetPath: string
  cwd: string
}

let cachedEditors: EditorDescriptor[] | null = null

async function extractIconDataUrl(executable: string): Promise<string | undefined> {
  try {
    const image = await app.getFileIcon(executable, { size: 'normal' })
    if (image.isEmpty()) return undefined
    return image.toDataURL()
  } catch {
    return undefined
  }
}

function getIconExecutable(entry: EditorDescriptor): string | undefined {
  if (entry.id === 'explorer') {
    if (process.platform !== 'win32') return undefined
    const systemRoot = process.env.SystemRoot ?? 'C:\\Windows'
    return path.join(systemRoot, 'explorer.exe')
  }
  if (!path.isAbsolute(entry.command)) return undefined
  if (/\.exe$/i.test(entry.command)) return entry.command
  if (entry.id === 'cursor') {
    const commandPath = path.normalize(entry.command)
    const marker = `${path.sep}programs${path.sep}cursor${path.sep}`
    const markerIndex = commandPath.toLowerCase().indexOf(marker)
    if (markerIndex >= 0) {
      const baseDir = commandPath.slice(0, markerIndex + marker.length - 1)
      return path.join(baseDir, 'Cursor.exe')
    }
  }
  if (entry.id === 'vscode') {
    const commandPath = path.normalize(entry.command)
    const marker = `${path.sep}programs${path.sep}microsoft vs code${path.sep}`
    const markerIndex = commandPath.toLowerCase().indexOf(marker)
    if (markerIndex >= 0) {
      const baseDir = commandPath.slice(0, markerIndex + marker.length - 1)
      return path.join(baseDir, 'Code.exe')
    }
  }
  if (entry.id === 'git-bash') {
    const commandPath = path.normalize(entry.command)
    const gitDirMarker = `${path.sep}git${path.sep}`
    const gitDirMarkerIndex = commandPath.toLowerCase().indexOf(gitDirMarker)
    if (gitDirMarkerIndex >= 0) {
      const baseDir = commandPath.slice(0, gitDirMarkerIndex + gitDirMarker.length - 1)
      return path.join(baseDir, 'git-bash.exe')
    }
  }
  return undefined
}

async function fileExists(targetPath: string | undefined): Promise<boolean> {
  if (!targetPath) return false
  try {
    await access(targetPath)
    return true
  } catch {
    return false
  }
}

async function firstExistingPath(
  candidates: Array<string | null | undefined>
): Promise<string | null> {
  for (const candidate of candidates) {
    if (!candidate || candidate.trim().length === 0) continue
    if (await fileExists(candidate)) return candidate
  }
  return null
}

async function whereCommand(command: string): Promise<string | null> {
  return new Promise((resolve) => {
    const whereBinary = process.platform === 'win32' ? 'where.exe' : 'which'
    execFile(whereBinary, [command], { windowsHide: true }, (error, stdout) => {
      if (error) {
        resolve(null)
        return
      }
      const first = stdout
        .split(/\r?\n/)
        .map((item) => item.trim())
        .find((item) => item.length > 0)
      resolve(first ?? null)
    })
  })
}

async function findVisualStudio(): Promise<string | null> {
  if (process.platform !== 'win32') return null
  const installer =
    process.env['ProgramFiles(x86)'] != null
      ? path.join(process.env['ProgramFiles(x86)'], 'Microsoft Visual Studio', 'Installer', 'vswhere.exe')
      : null
  if (!(await fileExists(installer ?? undefined))) return null
  return new Promise((resolve) => {
    execFile(
      installer as string,
      ['-latest', '-products', '*', '-requires', 'Microsoft.Component.MSBuild', '-property', 'productPath'],
      { windowsHide: true },
      (error, stdout) => {
        if (error) {
          resolve(null)
          return
        }
        const candidate = stdout.trim()
        resolve(candidate.length > 0 ? candidate : null)
      }
    )
  })
}

async function findGitBashFromRegistry(): Promise<string | null> {
  if (process.platform !== 'win32') return null
  return new Promise((resolve) => {
    execFile(
      'reg',
      ['query', 'HKLM\\SOFTWARE\\GitForWindows', '/v', 'InstallPath'],
      { windowsHide: true },
      async (error, stdout) => {
        if (error) {
          resolve(null)
          return
        }
        const lines = stdout.split(/\r?\n/).map((line) => line.trim())
        const installLine = lines.find((line) => line.startsWith('InstallPath'))
        if (!installLine) {
          resolve(null)
          return
        }
        const match = installLine.match(/InstallPath\s+REG_SZ\s+(.+)$/)
        const installPath = match?.[1]?.trim()
        const candidate = installPath ? path.join(installPath, 'git-bash.exe') : null
        resolve((await fileExists(candidate ?? undefined)) ? candidate : null)
      }
    )
  })
}

async function detectWindowsEditors(): Promise<EditorDescriptor[]> {
  const result: EditorDescriptor[] = []
  const localAppData = process.env.LOCALAPPDATA ?? ''
  const programFiles = process.env.ProgramFiles ?? ''

  const explorer: EditorDescriptor = {
    id: 'explorer',
    labelKey: 'editors.explorer',
    iconKey: 'explorer',
    command: 'explorer.exe',
    args: (targetPath) => ['/e,', targetPath]
  }
  result.push(explorer)

  const cursorCommand = await firstExistingPath([
    path.join(localAppData, 'Programs', 'cursor', 'Cursor.exe'),
    programFiles ? path.join(programFiles, 'cursor', 'Cursor.exe') : null,
    await whereCommand('Cursor.exe'),
    await whereCommand('cursor'),
    await whereCommand('cursor.cmd')
  ])
  if (cursorCommand) {
    result.push({
      id: 'cursor',
      labelKey: 'editors.cursor',
      iconKey: 'editor-generic',
      command: cursorCommand,
      args: (targetPath) => [targetPath]
    })
  }

  const vscodeCommand = await firstExistingPath([
    path.join(localAppData, 'Programs', 'Microsoft VS Code', 'Code.exe'),
    programFiles ? path.join(programFiles, 'Microsoft VS Code', 'Code.exe') : null,
    await whereCommand('Code.exe'),
    await whereCommand('code'),
    await whereCommand('code.cmd')
  ])
  if (vscodeCommand) {
    result.push({
      id: 'vscode',
      labelKey: 'editors.vscode',
      iconKey: 'editor-generic',
      command: vscodeCommand,
      args: (targetPath) => [targetPath]
    })
  }

  const visualStudio = await findVisualStudio()
  if (visualStudio && await fileExists(visualStudio)) {
    result.push({
      id: 'vs',
      labelKey: 'editors.vs',
      iconKey: 'editor-generic',
      command: visualStudio,
      args: (targetPath) => [targetPath]
    })
  }

  const toolboxScripts = path.join(localAppData, 'JetBrains', 'Toolbox', 'scripts')
  const jetbrainsEntries: Array<{ id: EditorId; labelKey: string; candidates: string[] }> = [
    {
      id: 'rider',
      labelKey: 'editors.rider',
      candidates: ['rider64.exe', 'rider.exe', path.join(toolboxScripts, 'rider64.exe')]
    },
    {
      id: 'webstorm',
      labelKey: 'editors.webstorm',
      candidates: ['webstorm64.exe', 'webstorm.exe', path.join(toolboxScripts, 'webstorm64.exe')]
    },
    {
      id: 'idea',
      labelKey: 'editors.idea',
      candidates: ['idea64.exe', 'idea.exe', path.join(toolboxScripts, 'idea64.exe')]
    }
  ]
  for (const entry of jetbrainsEntries) {
    let found: string | null = null
    for (const candidate of entry.candidates) {
      const commandPath = candidate.endsWith('.exe')
        ? ((await whereCommand(candidate)) ?? path.join(programFiles, 'JetBrains', candidate))
        : candidate
      if (await fileExists(commandPath)) {
        found = commandPath
        break
      }
    }
    if (found) {
      result.push({
        id: entry.id,
        labelKey: entry.labelKey,
        iconKey: 'editor-generic',
        command: found,
        args: (targetPath) => [targetPath]
      })
    }
  }

  const githubDesktop = path.join(localAppData, 'GitHubDesktop', 'GitHubDesktop.exe')
  if (await fileExists(githubDesktop)) {
    result.push({
      id: 'github-desktop',
      labelKey: 'editors.githubDesktop',
      iconKey: 'editor-generic',
      command: githubDesktop,
      args: (_targetPath, cwd) => [cwd]
    })
  }

  const gitBashPathFromRegistry = await findGitBashFromRegistry()
  const gitBashCandidates = await firstExistingPath([
    await whereCommand('git-bash.exe'),
    programFiles ? path.join(programFiles, 'Git', 'git-bash.exe') : null,
    gitBashPathFromRegistry,
    await whereCommand('git-bash'),
    await whereCommand('git-bash.cmd')
  ])
  if (gitBashCandidates) {
    result.push({
      id: 'git-bash',
      labelKey: 'editors.gitBash',
      iconKey: 'terminal',
      command: gitBashCandidates,
      args: (_targetPath, cwd) => ['--cd=' + cwd]
    })
  }

  return result
}

async function detectUnixEditors(): Promise<EditorDescriptor[]> {
  const result: EditorDescriptor[] = [
    {
      id: 'explorer',
      labelKey: 'editors.explorer',
      iconKey: 'explorer',
      command: '',
      args: () => []
    }
  ]
  const candidates: Array<{ id: EditorId; labelKey: string; command: string }> = [
    { id: 'cursor', labelKey: 'editors.cursor', command: 'cursor' },
    { id: 'vscode', labelKey: 'editors.vscode', command: 'code' },
    { id: 'idea', labelKey: 'editors.idea', command: 'idea' }
  ]
  for (const candidate of candidates) {
    const found = await whereCommand(candidate.command)
    if (!found) continue
    result.push({
      id: candidate.id,
      labelKey: candidate.labelKey,
      iconKey: 'editor-generic',
      command: found,
      args: (targetPath) => [targetPath]
    })
  }
  return result
}

export async function detectEditors(): Promise<EditorInfo[]> {
  if (cachedEditors === null) {
    const detectedEditors = process.platform === 'win32'
      ? await detectWindowsEditors()
      : await detectUnixEditors()
    cachedEditors = await Promise.all(
      detectedEditors.map(async (entry) => {
        const iconExecutable = getIconExecutable(entry)
        const iconDataUrl = iconExecutable ? await extractIconDataUrl(iconExecutable) : undefined
        return {
          ...entry,
          iconDataUrl
        }
      })
    )
  }
  return cachedEditors.map((entry) => ({
    id: entry.id,
    labelKey: entry.labelKey,
    iconKey: entry.iconKey,
    iconDataUrl: entry.iconDataUrl
  }))
}

function getCachedEditorById(editorId: EditorId): EditorDescriptor | null {
  if (!cachedEditors) return null
  return cachedEditors.find((entry) => entry.id === editorId) ?? null
}

export async function launchEditor(editorId: EditorId, targetPath: string): Promise<void> {
  const target = await resolveLaunchTarget(targetPath)
  if (editorId === 'explorer') {
    await shell.openPath(target.targetPath)
    return
  }
  if (!cachedEditors) {
    await detectEditors()
  }
  const descriptor = getCachedEditorById(editorId)
  if (!descriptor) {
    throw new Error(`Unsupported editor id: ${editorId}`)
  }
  const spawnArgs = descriptor.args(target.targetPath, target.cwd)
  const isWindowsShellScript =
    process.platform === 'win32' && /\.(cmd|bat)$/i.test(descriptor.command)
  await spawnDetached(
    isWindowsShellScript ? `"${descriptor.command}"` : descriptor.command,
    isWindowsShellScript ? spawnArgs.map(quoteWinArg) : spawnArgs,
    isWindowsShellScript
      ? {
        detached: true,
        stdio: 'ignore',
        cwd: target.cwd,
        shell: true,
        windowsVerbatimArguments: true
      }
      : {
        detached: true,
        stdio: 'ignore',
        cwd: target.cwd
      }
  )
}

async function resolveLaunchTarget(targetPath: string): Promise<ResolvedLaunchTarget> {
  const resolved = path.resolve(targetPath)
  try {
    const targetStat = await stat(resolved)
    if (targetStat.isDirectory()) {
      return { targetPath: resolved, cwd: resolved }
    }
    return { targetPath: resolved, cwd: path.dirname(resolved) }
  } catch {
    return { targetPath: resolved, cwd: path.dirname(resolved) }
  }
}

function spawnDetached(command: string, args: string[], options: SpawnOptions): Promise<void> {
  return new Promise((resolve, reject) => {
    let child: ChildProcess
    try {
      child = spawn(command, args, options)
    } catch (error) {
      reject(error)
      return
    }

    let settled = false
    const settle = (callback: () => void): void => {
      if (settled) return
      settled = true
      callback()
    }

    child.once('error', (error) => settle(() => reject(error)))
    child.once('spawn', () => settle(resolve))
    child.unref()
  })
}

function quoteWinArg(arg: string): string {
  if (arg.length === 0) return '""'
  if (!/[\s"]/.test(arg)) return arg
  return `"${arg.replace(/"/g, '\\"')}"`
}

export function clearEditorDetectionCache(): void {
  cachedEditors = null
}
