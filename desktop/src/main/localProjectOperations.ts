import { app } from 'electron'
import { existsSync, promises as fs } from 'node:fs'
import * as path from 'node:path'
import { normalizeWorkspaceProjectKey, sameWorkspaceProjectKey } from '../shared/workspaceProjectKey'
import { isDefaultChatWorkspace } from './defaultChatWorkspace'
import { runGitCommand } from './gitCommand'
import { sanitizeProjectFolderName } from './ipcBridgePaths'
import { saveLocalProjectDetails, type AppSettings } from './settings'

export type LocalProjectOperationErrorCode =
  | 'InvalidArguments'
  | 'TargetUnsupported'
  | 'ProjectAlreadyExists'

export class LocalProjectOperationError extends Error {
  constructor(
    readonly code: LocalProjectOperationErrorCode,
    message: string
  ) {
    super(message)
  }
}

export interface NewProjectFolderResult {
  path: string
  gitInitialized: boolean
}

export interface LocalProjectFolderPlan {
  primaryFolder: string
  secondaryFolders: string[]
}

export interface LocalProjectRegistrationDeps {
  openInHub(projectPath: string): Promise<void>
  settings: AppSettings
  persistSettings(settings: AppSettings): void
  broadcastProjects(): void
}

export interface CreatedLocalProject {
  id: string
  name: string
  path: string
  primaryFolder: string
  secondaryFolders: string[]
}

export function planNewLocalProjectFolder(name: string, baseDir?: string): string {
  const root = baseDir ?? app.getPath('documents')
  const folderName = sanitizeProjectFolderName(name)
  let target = path.join(root, folderName)
  for (let suffix = 2; existsSync(target); suffix += 1) {
    target = path.join(root, `${folderName} ${suffix}`)
  }
  return target
}

export async function createNewLocalProjectFolder(
  name: string,
  options: {
    baseDir?: string
    preferredPath?: string
    initGit?: (dir: string) => Promise<void>
  } = {}
): Promise<NewProjectFolderResult> {
  const root = options.baseDir ?? app.getPath('documents')
  const folderName = sanitizeProjectFolderName(name)
  await fs.mkdir(root, { recursive: true })

  let target = options.preferredPath?.trim()
    ? path.resolve(options.preferredPath.trim())
    : planNewLocalProjectFolder(name, root)
  let suffix = 2

  for (;;) {
    try {
      await fs.mkdir(target, { recursive: false })
      break
    } catch (error) {
      if (!isAlreadyExistsError(error)) throw error
      target = path.join(root, `${folderName} ${suffix}`)
      suffix += 1
    }
  }

  let gitInitialized = true
  try {
    if (options.initGit) await options.initGit(target)
    else await runGitCommand(target, ['init'])
  } catch {
    gitInitialized = false
  }

  return { path: target, gitInitialized }
}

export async function validateExistingProjectFolders(
  folders: readonly string[],
  primaryFolder?: string,
  isDefaultChat: (folder: string) => boolean = isDefaultChatWorkspace
): Promise<LocalProjectFolderPlan> {
  const normalized: string[] = []
  for (const raw of folders) {
    const resolved = await validateAbsoluteDirectory(raw, 'folders')
    if (!normalized.some((existing) => sameWorkspaceProjectKey(existing, resolved))) {
      normalized.push(resolved)
    }
  }

  let ordered = normalized
  if (primaryFolder !== undefined) {
    const resolvedPrimary = await validateAbsoluteDirectory(primaryFolder, 'primaryFolder')
    const match = normalized.find((candidate) => sameWorkspaceProjectKey(candidate, resolvedPrimary))
    if (!match) {
      throw new LocalProjectOperationError(
        'InvalidArguments',
        'primaryFolder must match one of the paths provided in folders.'
      )
    }
    ordered = [match, ...normalized.filter((candidate) => !sameWorkspaceProjectKey(candidate, match))]
  }

  const [primary, ...secondaryFolders] = ordered
  if (isDefaultChat(primary)) {
    throw new LocalProjectOperationError(
      'TargetUnsupported',
      'The default Chats workspace cannot be added as a project.'
    )
  }

  return { primaryFolder: primary, secondaryFolders }
}

export async function registerLocalProject(
  deps: LocalProjectRegistrationDeps,
  params: {
    projectPath?: string
    primaryFolder: string
    secondaryFolders: string[]
    name?: string
  }
): Promise<CreatedLocalProject> {
  const primaryFolder = path.resolve(params.primaryFolder.trim())
  const projectPath = params.projectPath?.trim()
    ? path.resolve(params.projectPath.trim())
    : primaryFolder
  const secondaryFolders = params.secondaryFolders.map((folder) => path.resolve(folder.trim()))
  const customName = params.name?.trim() || undefined

  await deps.openInHub(projectPath)
  saveLocalProjectDetails(deps.settings, {
    projectPath,
    primaryFolder,
    secondaryFolders,
    name: customName
  })
  deps.persistSettings(deps.settings)
  deps.broadcastProjects()

  return {
    id: normalizeWorkspaceProjectKey(projectPath),
    name: customName || path.basename(primaryFolder),
    path: projectPath,
    primaryFolder,
    secondaryFolders
  }
}

async function validateAbsoluteDirectory(value: string, field: string): Promise<string> {
  const trimmed = value.trim()
  if (!trimmed || trimmed.includes('\0')) {
    throw new LocalProjectOperationError('InvalidArguments', `${field} must contain valid absolute folder paths.`)
  }
  const isWindowsAbsolutePath = /^[A-Za-z]:[\\/]/.test(trimmed)
  if (!isWindowsAbsolutePath && /^[A-Za-z][A-Za-z0-9+.-]*:/.test(trimmed)) {
    throw new LocalProjectOperationError('InvalidArguments', `Folder path must be an absolute filesystem path: ${trimmed}`)
  }
  if (!path.isAbsolute(trimmed)) {
    throw new LocalProjectOperationError('InvalidArguments', `Folder path must be absolute: ${trimmed}`)
  }
  const resolved = path.resolve(trimmed)
  let isDirectory = false
  try {
    isDirectory = (await fs.stat(resolved)).isDirectory()
  } catch {
    isDirectory = false
  }
  if (!isDirectory) {
    throw new LocalProjectOperationError('InvalidArguments', `Folder does not exist or is not a directory: ${trimmed}`)
  }
  return resolved
}

function isAlreadyExistsError(error: unknown): boolean {
  return error != null && typeof error === 'object' && 'code' in error && (error as { code?: unknown }).code === 'EEXIST'
}
