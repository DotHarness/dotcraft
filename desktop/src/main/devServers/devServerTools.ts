import { isAbsolute, resolve } from 'node:path'
import { z } from 'zod'
import { devServerFolderKey, type DevServerScope } from '../../shared/devServers'
import { historyJsonResult, invalidHistoryArguments } from '../desktopReadThread'
import type { AppServerRequestClient, DynamicToolCallParams, DynamicToolCallResult } from '../desktopRuntimeThreadTools'
import { devServerManager } from './devServerManager'
import { loadLaunchFile } from './launchFile'

const MAX_LOG_LINES = 200
const MAX_LOG_CHARS = 16_000
const LAUNCH_FORMAT = `{"configurations":[{"name":"<name>","runtimeExecutable":"<command>","runtimeArgs":["<arg>"],"port":<port>}]}`
const ERROR_LINE = /\b(?:errors?|failed|exception|fatal|panic|traceback|uncaught)\b/i
const nonEmpty = z.string().trim().min(1)
const startArguments = z.strictObject({
  name: nonEmpty.describe('Name in .craft/launch.json.'),
  folder: nonEmpty.optional().describe("Absolute folder from ListServers. Defaults to the chat's working folder.")
})
const stopArguments = z.strictObject({ serverId: nonEmpty })
const listArguments = z.strictObject({})
const logArguments = stopArguments.extend({
  lines: z.number().int().min(1).max(MAX_LOG_LINES).default(50)
    .describe('Maximum lines to return (default 50).'),
  search: z.string().trim().min(1).max(256).optional().describe('Filter by text (case-insensitive).'),
  errorsOnly: z.boolean().optional().describe('Show only error lines.')
})

export const devServerToolDefinitions = [
  {
    name: 'StartServer',
    description: `Start or reuse a named dev server from .craft/launch.json and open its preview. If the file is missing, create it using this format: ${LAUNCH_FORMAT}`,
    inputSchema: z.toJSONSchema(startArguments, { io: 'input' }),
    approval: { kind: 'remoteResource' as const, targetArgument: 'name', operation: 'Start local dev server' }
  },
  {
    name: 'StopServer',
    description: 'Stop a dev server in this chat.',
    inputSchema: z.toJSONSchema(stopArguments, { io: 'input' })
  },
  {
    name: 'ListServers',
    description: "List this chat's dev servers and their IDs, and find launch configurations by folder.",
    inputSchema: z.toJSONSchema(listArguments, { io: 'input' })
  },
  {
    name: 'ReadServerLogs',
    description: 'Read recent dev server output, newest last.',
    inputSchema: z.toJSONSchema(logArguments, { io: 'input' })
  }
]

export const devServerToolNames = new Set(devServerToolDefinitions.map(tool => tool.name))

interface ThreadHeader {
  id?: string
  effectiveWorkspacePath?: string | null
  runtimeWorkspaceRoots?: string[] | null
  configuration?: { mode?: string | null } | null
  status?: string | null
}

type OpenPreview = (preview: { threadId: string; name: string; url: string }) => void

export async function handleDevServerToolCall(
  client: AppServerRequestClient,
  call: DynamicToolCallParams,
  openPreview?: OpenPreview
): Promise<DynamicToolCallResult> {
  const { threadId, tool } = call
  if (!threadId) return fail('InvalidArguments', 'A calling thread is required.')
  if (!tool || !devServerToolNames.has(tool)) return fail('UnsupportedTool', 'Unknown dev server tool.')

  const { thread } = await client.sendRequest<{ thread?: ThreadHeader }>('thread/read', { threadId })
  const defaultFolder = absoluteFolder(thread?.effectiveWorkspacePath)
  if (thread?.id !== threadId || defaultFolder === undefined) {
    return fail('WorkspaceUnavailable', 'The calling thread has no available execution folder.')
  }

  const args = call.arguments ?? {}
  const scope = { threadId, folder: defaultFolder }
  if (tool === 'StartServer' || tool === 'StopServer') {
    if (thread.configuration?.mode?.toLowerCase() !== 'agent') {
      return fail('DevServerRequiresAgentMode', 'Starting and stopping dev servers requires Agent mode.')
    }
    if (thread.status?.toLowerCase() === 'archived') {
      return fail('ThreadArchived', 'An archived thread cannot start or stop dev servers.')
    }
  }

  switch (tool) {
    case 'StartServer': {
      const parsed = startArguments.safeParse(args)
      if (!parsed.success) return invalidHistoryArguments(parsed.error)
      const folders = launchFolders(defaultFolder, thread.runtimeWorkspaceRoots)
      const requested = parsed.data.folder === undefined ? defaultFolder : absoluteFolder(parsed.data.folder)
      const folder = requested === undefined ? undefined : folders.get(folderKey(requested))
      if (folder === undefined) {
        return fail('DevServerFolderNotAllowed', 'Select an absolute launch folder from ListServers for this chat.')
      }
      return startServer({ threadId, folder }, parsed.data.name, openPreview)
    }
    case 'ListServers': {
      const parsed = listArguments.safeParse(args)
      if (!parsed.success) return invalidHistoryArguments(parsed.error)
      const folders = await Promise.all(
        [...launchFolders(defaultFolder, thread.runtimeWorkspaceRoots).values()].map(describeFolder)
      )
      return historyJsonResult({ defaultFolder, folders, servers: devServerManager.servers(scope) })
    }
    case 'StopServer': {
      const parsed = stopArguments.safeParse(args)
      if (!parsed.success) return invalidHistoryArguments(parsed.error)
      const { serverId } = parsed.data
      if (devServerManager.get(serverId)?.threadId !== threadId) return serverNotFound()
      const stopped = await devServerManager.stop(serverId)
      return stopped ? historyJsonResult(stopped) : serverNotFound()
    }
    case 'ReadServerLogs': {
      const parsed = logArguments.safeParse(args)
      if (!parsed.success) return invalidHistoryArguments(parsed.error)
      const { serverId, lines, search, errorsOnly } = parsed.data
      if (devServerManager.get(serverId)?.threadId !== threadId) return serverNotFound()
      const needle = search?.toLowerCase()
      const matching = devServerManager.logs(serverId).filter(line =>
        (!errorsOnly || ERROR_LINE.test(line)) && (needle === undefined || line.toLowerCase().includes(needle))
      )
      const recent: string[] = []
      let chars = 0
      for (let index = matching.length - 1; index >= 0 && recent.length < lines; index--) {
        const line = matching[index]
        chars += line.length + 1
        if (chars > MAX_LOG_CHARS) break
        recent.push(line)
      }
      return historyJsonResult({ serverId, lines: recent.reverse(), truncated: recent.length < matching.length })
    }
    default:
      return fail('UnsupportedTool', 'Unknown dev server tool.')
  }
}

function absoluteFolder(value: unknown): string | undefined {
  return typeof value === 'string' && isAbsolute(value) && !value.includes('\0') ? resolve(value) : undefined
}

function folderKey(folder: string): string {
  return devServerFolderKey(folder, process.platform)
}

function launchFolders(defaultFolder: string, roots: ThreadHeader['runtimeWorkspaceRoots']): Map<string, string> {
  const folders = new Map([[folderKey(defaultFolder), defaultFolder]])
  for (const root of Array.isArray(roots) ? roots : []) {
    const folder = absoluteFolder(root)
    if (folder !== undefined && !folders.has(folderKey(folder))) folders.set(folderKey(folder), folder)
  }
  return folders
}

async function describeFolder(folder: string) {
  const { path, parsed } = await loadLaunchFile(folder)
  const launch = {
    path,
    status: parsed.status,
    ...(parsed.status === 'ok' ? {
      autoVerify: parsed.autoVerify,
      configurations: parsed.configurations.map(configuration => configuration.name)
    } : {})
  }
  return { folder, launch }
}

async function startServer(
  scope: DevServerScope,
  name: string,
  openPreview?: OpenPreview
): Promise<DynamicToolCallResult> {
  const result = await devServerManager.start(scope, name)
  if (!result.ok) {
    if (result.failure.kind === 'launchFile') {
      const { path, parsed } = await loadLaunchFile(scope.folder)
      if (parsed.status === 'missing') {
        return fail('DevServerLaunchFileMissing', `Create ${path} using this format: ${LAUNCH_FORMAT}. Then call StartServer with the configuration name.`)
      }
      if (parsed.status === 'invalid') {
        return fail('DevServerLaunchFileInvalid', `No usable launch configuration in ${path}. Check the JSON and each configuration's name, command or URL, working directory, and port.`)
      }
      return fail('DevServerConfigurationNotFound', `${path}: ${result.failure.message}`)
    }
    return fail('DevServerStartFailed', result.failure.message, result.server)
  }
  if ('attachUrl' in result) {
    openPreview?.({ threadId: scope.threadId, name, url: result.attachUrl })
    return historyJsonResult({ name, folder: scope.folder, url: result.attachUrl, attached: true })
  }
  const { server, reused } = result
  openPreview?.({ threadId: scope.threadId, name: server.name, url: server.url })
  return historyJsonResult({ ...server, reused })
}

function serverNotFound(): DynamicToolCallResult {
  return fail('DevServerNotFound', 'No dev server with that ID belongs to this chat. Use ListServers to find its server ID.')
}

function fail(errorCode: string, errorMessage: string, structuredContent?: unknown): DynamicToolCallResult {
  return { success: false, errorCode, errorMessage, ...(structuredContent === undefined ? {} : { structuredContent }) }
}
