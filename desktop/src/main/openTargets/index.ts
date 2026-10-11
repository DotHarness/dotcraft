import { access, readdir, readFile } from 'fs/promises'
import { homedir } from 'os'
import * as path from 'path'
import { execFile } from 'child_process'
import { app, nativeImage, shell } from 'electron'
import { parseJsonConfig } from '../../shared/jsonConfig'
import type { OpenInTargetRequest, OpenTargetInfo, OpenTargetLocation } from '../../shared/openTargets'
import { readCustomOpenTargets, type CustomOpenTarget } from './customTargets'
import {
  createCommandLookup,
  detectUnixTargets,
  detectWindowsTargets,
  isExecutableFile,
  type BuiltInOpenTarget,
  type DetectionHost
} from './detection'
import { composeBuiltInLaunch, composeCustomLaunch, resolveLaunchPath, runLaunchCommand } from './launch'

interface DetectedBuiltIn {
  target: BuiltInOpenTarget
  iconDataUrl?: string
}

let builtInTargets: Promise<DetectedBuiltIn[]> | null = null

function execFileText(command: string, args: string[]): Promise<string | null> {
  return new Promise((resolve) => {
    execFile(command, args, { windowsHide: true }, (error, stdout) => {
      resolve(error ? null : String(stdout))
    })
  })
}

const productionHost: DetectionHost = {
  platform: process.platform,
  env: process.env,
  async exists(filePath) {
    try {
      await access(filePath)
      return true
    } catch {
      return false
    }
  },
  isExecutable: isExecutableFile,
  async listDirectory(directory) {
    try {
      return await readdir(directory)
    } catch {
      return []
    }
  },
  async findWithVsWhere(vswhere, pattern) {
    const stdout = await execFileText(vswhere, ['-products', '*', '-all', '-find', pattern, '-utf8', '-nologo', '-prerelease'])
    return stdout?.split(/\r?\n/).map((line) => line.trim()).find(Boolean) ?? null
  },
  async hasWslDistribution() {
    const stdout = await execFileText('reg', ['query', 'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Lxss'])
    return stdout != null && /\\Lxss\\\{/i.test(stdout)
  },
  async gitBashFromRegistry() {
    const stdout = await execFileText('reg', ['query', 'HKLM\\SOFTWARE\\GitForWindows', '/v', 'InstallPath'])
    const installPath = stdout?.match(/InstallPath\s+REG_SZ\s+(.+)$/m)?.[1]?.trim()
    if (!installPath) return null
    const candidate = path.join(installPath, 'git-bash.exe')
    return (await productionHost.exists(candidate)) ? candidate : null
  }
}

async function fileIconDataUrl(executable: string): Promise<string | undefined> {
  try {
    const image = await app.getFileIcon(executable, { size: 'normal' })
    return image.isEmpty() ? undefined : image.toDataURL()
  } catch {
    return undefined
  }
}

function builtInIconSource(target: BuiltInOpenTarget): string | undefined {
  if (target.iconPath) return target.iconPath
  return path.isAbsolute(target.command) && /\.exe$/i.test(target.command) ? target.command : undefined
}

function detectBuiltInTargets(): Promise<DetectedBuiltIn[]> {
  builtInTargets ??= (async () => {
    const targets = process.platform === 'win32'
      ? await detectWindowsTargets(productionHost)
      : await detectUnixTargets(productionHost)
    return Promise.all(targets.map(async (target) => {
      const iconSource = builtInIconSource(target)
      return { target, iconDataUrl: iconSource ? await fileIconDataUrl(iconSource) : undefined }
    }))
  })().catch((error: unknown) => {
    builtInTargets = null
    throw error
  })
  return builtInTargets
}

async function readUserConfig(): Promise<Record<string, unknown> | null> {
  try {
    const raw = await readFile(path.join(homedir(), '.craft', 'config.json'), 'utf8')
    return parseJsonConfig<Record<string, unknown> | null>(raw, null)
  } catch {
    return null
  }
}

async function readCustomTargets(): Promise<CustomOpenTarget[]> {
  return readCustomOpenTargets(await readUserConfig(), createCommandLookup(productionHost), console.warn)
}

function customIconDataUrl(icon: string | undefined): string | undefined {
  if (!icon) return undefined
  if (icon.startsWith('data:image/')) return icon
  const image = nativeImage.createFromPath(icon)
  return image.isEmpty() ? undefined : image.toDataURL()
}

export async function listOpenTargets(): Promise<OpenTargetInfo[]> {
  const [builtIns, customs] = await Promise.all([detectBuiltInTargets(), readCustomTargets()])
  return [
    ...builtIns.map(({ target, iconDataUrl }): OpenTargetInfo => ({
      id: target.id,
      label: target.label,
      ...(target.labelKey ? { labelKey: target.labelKey } : {}),
      iconKey: target.iconKey,
      ...(iconDataUrl ? { iconDataUrl } : {}),
      ...(target.hidden ? { hidden: true } : {})
    })),
    ...customs.map((target): OpenTargetInfo => {
      const iconDataUrl = customIconDataUrl(target.icon)
      return { id: target.id, label: target.label, iconKey: 'editor', ...(iconDataUrl ? { iconDataUrl } : {}) }
    })
  ]
}

function requestLocation(request: OpenInTargetRequest): OpenTargetLocation | null {
  if (!Number.isInteger(request.line) || (request.line ?? 0) < 1) return null
  const column = Number.isInteger(request.column) && (request.column ?? 0) >= 1 ? request.column! : 1
  return { line: request.line!, column }
}

export async function openInTarget(request: OpenInTargetRequest): Promise<void> {
  const launchPath = await resolveLaunchPath(request.path)
  const location = requestLocation(request)
  const builtIn = (await detectBuiltInTargets()).find(({ target }) => target.id === request.targetId)?.target
  if (builtIn?.opener === 'fileManager') {
    if (launchPath.isDirectory) {
      const error = await shell.openPath(launchPath.path)
      if (error) throw new Error(error)
    } else {
      shell.showItemInFolder(launchPath.path)
    }
    return
  }
  if (builtIn?.opener === 'systemDefault') {
    const error = await shell.openPath(launchPath.path)
    if (error) throw new Error(error)
    return
  }
  if (builtIn) {
    await runLaunchCommand(composeBuiltInLaunch(builtIn, launchPath, location), launchPath.directory)
    return
  }
  const custom = (await readCustomTargets()).find((target) => target.id === request.targetId)
  if (!custom) throw new Error(`Open target is not available: ${request.targetId}`)
  await runLaunchCommand(composeCustomLaunch(custom, launchPath, location), launchPath.directory)
}

export function clearOpenTargetCache(): void {
  builtInTargets = null
}
