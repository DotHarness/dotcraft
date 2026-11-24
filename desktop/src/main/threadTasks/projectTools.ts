import * as path from 'node:path'
import type { DesktopToolConfirmOutcome } from '../../shared/desktopToolConfirm'
import type { DynamicToolCallParams, DynamicToolCallResult } from '../desktopRuntimeThreadTools'
import {
  LocalProjectOperationError,
  validateExistingProjectFolders,
  type CreatedLocalProject,
  type NewProjectFolderResult
} from '../localProjectOperations'
import type { ThreadTaskConnection } from './childThreads'
import type { DesktopThreadTasks } from './threadTaskService'

export const CREATE_PROJECT_TOOL_NAME = 'CreateProject'

export const CREATE_PROJECT_DESCRIPTION =
  'Create a local DotCraft project only when the user explicitly asks for a new project. Pass existing local absolute folder paths in `folders` (with optional `primaryFolder`), or omit `folders` to create a new folder under Documents and initialize a Git repository. Desktop asks the user to confirm before creating anything and does not switch the active chat or foreground project. Returns the new project id, name, and folders.'

export const CREATE_PROJECT_PROPERTIES = {
  name: {
    type: 'string',
    description: 'Name for the new project. Required when folders is omitted. Optional when folders is provided, defaulting to the primary folder name.'
  },
  folders: {
    type: 'array',
    minItems: 1,
    items: { type: 'string' },
    description: 'Optional existing local absolute folder paths to attach. Omit to create a new local project folder with a Git repository.'
  },
  primaryFolder: {
    type: 'string',
    description: 'Optional primary folder for a multi-folder project. Must match one of the folders in folders and becomes the default working directory, defaulting to the first folder in folders.'
  }
}

export interface DesktopProjectCreatorHost {
  refreshProjects(): Promise<void>
  existingProjectId(projectPath: string): string | undefined
  isDefaultChat?(folder: string): boolean
  planNewFolder(name: string): string
  createNewFolder(name: string, preferredPath: string): Promise<NewProjectFolderResult>
  confirmCreation(request: {
    threadId?: string
    turnId?: string
    name: string
    folders: string[]
    createsNewFolder: boolean
  }): Promise<DesktopToolConfirmOutcome>
  registerProject(params: {
    primaryFolder: string
    secondaryFolders: string[]
    name?: string
  }): Promise<CreatedLocalProject>
}

export async function createProjectTool(
  connection: ThreadTaskConnection,
  tasks: DesktopThreadTasks | undefined,
  host: DesktopProjectCreatorHost | undefined,
  args: Record<string, unknown>,
  call: Pick<DynamicToolCallParams, 'threadId' | 'turnId'>
): Promise<DynamicToolCallResult> {
  if (connection.remote || !host) {
    return fail('TargetUnsupported', 'Creating local projects is only available from local Desktop chats.')
  }

  const parsed = parseCreateProjectArguments(args)
  if (!parsed.ok) return parsed.error

  const threadId = call.threadId?.trim()
  if (!tasks || !threadId || !(await tasks.canSeeCard(connection, threadId, call.turnId))) {
    return fail(
      'TargetUnsupported',
      'Creating a project requires a direct request from someone at the Desktop composer, so no project was created.'
    )
  }

  try {
    if (parsed.value.folders) {
      return await createFromExistingFolders(host, parsed.value.name, parsed.value.folders, parsed.value.primaryFolder, threadId, call.turnId)
    }
    return await createFromScratch(host, parsed.value.name!, threadId, call.turnId)
  } catch (error) {
    if (error instanceof LocalProjectOperationError) {
      return fail(error.code, error.message)
    }
    throw error
  }
}

async function createFromExistingFolders(
  host: DesktopProjectCreatorHost,
  name: string | undefined,
  folders: string[],
  primaryFolder: string | undefined,
  threadId: string,
  turnId: string | undefined
): Promise<DynamicToolCallResult> {
  const plan = await validateExistingProjectFolders(folders, primaryFolder, host.isDefaultChat)
  await host.refreshProjects()
  const existingBefore = host.existingProjectId(plan.primaryFolder)
  if (existingBefore) return alreadyExistsResult(plan.primaryFolder, existingBefore)

  const displayName = name || path.basename(plan.primaryFolder)
  const outcome = await host.confirmCreation({
    threadId,
    turnId,
    name: displayName,
    folders: [plan.primaryFolder, ...plan.secondaryFolders],
    createsNewFolder: false
  })
  if (outcome !== 'confirmed') return canceledResult(outcome)

  await host.refreshProjects()
  const existingAfter = host.existingProjectId(plan.primaryFolder)
  if (existingAfter) return alreadyExistsResult(plan.primaryFolder, existingAfter)

  const project = await host.registerProject({
    primaryFolder: plan.primaryFolder,
    secondaryFolders: plan.secondaryFolders,
    name
  })
  const extra = project.secondaryFolders.length > 0
    ? ` with ${project.secondaryFolders.length} additional folder${project.secondaryFolders.length === 1 ? '' : 's'}`
    : ''
  return ok(
    `Created project "${project.name}" (id: ${project.id}) at ${project.primaryFolder}${extra}.`,
    { project, createdNewFolder: false }
  )
}

async function createFromScratch(
  host: DesktopProjectCreatorHost,
  name: string,
  threadId: string,
  turnId: string | undefined
): Promise<DynamicToolCallResult> {
  const previewPath = host.planNewFolder(name)
  const outcome = await host.confirmCreation({
    threadId,
    turnId,
    name,
    folders: [previewPath],
    createsNewFolder: true
  })
  if (outcome !== 'confirmed') return canceledResult(outcome)

  const { path: createdPath, gitInitialized } = await host.createNewFolder(name, previewPath)
  const project = await host.registerProject({
    primaryFolder: createdPath,
    secondaryFolders: [],
    name
  })
  const gitNote = gitInitialized ? ' Initialized a Git repository.' : ' Git was not available to initialize a repository.'
  return ok(
    `Created project "${project.name}" (id: ${project.id}) at ${project.primaryFolder}.${gitNote}`,
    { project, createdNewFolder: true, gitInitialized }
  )
}

interface ParsedCreateProjectArgs {
  name?: string
  folders?: string[]
  primaryFolder?: string
}

function parseCreateProjectArguments(
  args: Record<string, unknown>
): { ok: true; value: ParsedCreateProjectArgs } | { ok: false; error: DynamicToolCallResult } {
  let name: string | undefined
  if ('name' in args && args.name != null) {
    if (typeof args.name !== 'string' || args.name.trim() === '') {
      return { ok: false, error: fail('InvalidArguments', 'name must be a non-empty string when provided.') }
    }
    name = args.name.trim()
  }

  let folders: string[] | undefined
  if ('folders' in args && args.folders != null) {
    if (!Array.isArray(args.folders) || args.folders.length === 0) {
      return {
        ok: false,
        error: fail('InvalidArguments', 'Omit folders to create a new project folder, or provide at least one existing absolute folder path.')
      }
    }
    const items: string[] = []
    for (const entry of args.folders) {
      if (typeof entry !== 'string' || entry.trim() === '') {
        return { ok: false, error: fail('InvalidArguments', 'folders must contain non-empty absolute folder paths.') }
      }
      items.push(entry.trim())
    }
    folders = items
  }

  let primaryFolder: string | undefined
  if ('primaryFolder' in args && args.primaryFolder != null) {
    if (typeof args.primaryFolder !== 'string' || args.primaryFolder.trim() === '') {
      return { ok: false, error: fail('InvalidArguments', 'primaryFolder must be a non-empty string when provided.') }
    }
    if (!folders) {
      return { ok: false, error: fail('InvalidArguments', 'primaryFolder can only be used together with folders.') }
    }
    primaryFolder = args.primaryFolder.trim()
  }

  if (!folders && !name) {
    return { ok: false, error: fail('InvalidArguments', 'name is required when folders is omitted.') }
  }

  return { ok: true, value: { name, folders, primaryFolder } }
}

function alreadyExistsResult(primaryFolder: string, existingId: string): DynamicToolCallResult {
  return fail(
    'ProjectAlreadyExists',
    `A project already exists for "${primaryFolder}" (id: ${existingId}). Use ListProjects to inspect existing projects.`
  )
}

function canceledResult(outcome: Exclude<DesktopToolConfirmOutcome, 'confirmed'>): DynamicToolCallResult {
  const message = outcome === 'abandoned'
    ? 'Project creation was canceled before the user confirmed. Do not create it again unless they ask.'
    : 'The user canceled creating the project. Do not create it again unless they ask.'
  return fail('ProjectCreationCanceled', message)
}

function ok(text: string, structuredContent?: unknown): DynamicToolCallResult {
  return {
    success: true,
    contentItems: [{ type: 'text', text }],
    structuredContent
  }
}

function fail(errorCode: string, errorMessage: string): DynamicToolCallResult {
  return { success: false, errorCode, errorMessage }
}
