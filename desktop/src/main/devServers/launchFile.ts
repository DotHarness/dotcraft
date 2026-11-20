import { readFile, realpath } from 'node:fs/promises'
import { isAbsolute, join, relative, resolve } from 'node:path'
import {
  LAUNCH_FILE_RELATIVE_PATH,
  type LaunchConfigurationSummary,
  type LaunchFileState,
  type SkippedLaunchConfiguration
} from '../../shared/devServers'

export interface CommandConfiguration {
  readonly kind: 'command'
  readonly name: string
  readonly command: string
  readonly args: readonly string[]
  readonly cwd: string
  readonly port: number
  readonly autoPort: boolean | undefined
  readonly env: Readonly<Record<string, string>>
}

export interface AttachConfiguration {
  readonly kind: 'attach'
  readonly name: string
  readonly url: string
  readonly port?: number
}

export type LaunchConfiguration = CommandConfiguration | AttachConfiguration

export type ParsedLaunchFile =
  | { readonly status: 'missing' }
  | { readonly status: 'invalid'; readonly reason: string }
  | {
    readonly status: 'ok'
    readonly autoVerify: boolean
    readonly configurations: readonly LaunchConfiguration[]
    readonly skipped: readonly SkippedLaunchConfiguration[]
  }

export interface LoadedLaunchFile {
  readonly path: string
  readonly parsed: ParsedLaunchFile
}

const PORT_FLAGS = new Set(['--port', '-p'])

export function launchFilePath(folder: string): string {
  return join(folder, ...LAUNCH_FILE_RELATIVE_PATH.split('/'))
}

function within(parent: string, child: string): boolean {
  const path = relative(parent, child)
  return path === '' || (!path.startsWith('..') && !isAbsolute(path))
}

export async function loadLaunchFile(folder: string): Promise<LoadedLaunchFile> {
  const path = launchFilePath(folder)
  let text: string
  try {
    const [realFolder, realFile] = await Promise.all([realpath(folder), realpath(path)])
    if (!within(realFolder, realFile)) {
      return { path, parsed: { status: 'invalid', reason: 'The launch file resolves outside the workspace.' } }
    }
    text = await readFile(realFile, 'utf8')
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') return { path, parsed: { status: 'missing' } }
    return { path, parsed: { status: 'invalid', reason: `The launch file could not be read: ${(error as Error).message}` } }
  }
  return { path, parsed: parseLaunchFile(text, folder) }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function stringArray(value: unknown): string[] | null {
  return Array.isArray(value) && value.every((entry) => typeof entry === 'string') ? value : null
}

function validPort(value: unknown): number | null {
  const port = typeof value === 'string' && /^\d+$/.test(value.trim()) ? Number(value) : value
  return typeof port === 'number' && Number.isInteger(port) && port >= 1 && port <= 65535 ? port : null
}

export function portFromArgs(args: readonly string[]): number | null {
  for (let index = 0; index < args.length; index += 1) {
    const arg = args[index]
    if (PORT_FLAGS.has(arg)) {
      const port = validPort(args[index + 1])
      if (port !== null) return port
    }
    const joined = /^--port=(\d+)$/.exec(arg)
    if (joined) return validPort(joined[1])
  }
  return null
}

/** Points every port argument at `port`, or returns null when there is none. */
export function retargetPortArgs(args: readonly string[], port: number): string[] | null {
  let changed = false
  const next = args.map((arg, index) => {
    if (index > 0 && PORT_FLAGS.has(args[index - 1]) && validPort(arg) !== null) {
      changed = true
      return String(port)
    }
    if (/^--port=\d+$/.test(arg)) {
      changed = true
      return `--port=${port}`
    }
    return arg
  })
  return changed ? next : null
}

function parseConfiguration(
  raw: unknown,
  index: number,
  folder: string
): LaunchConfiguration | SkippedLaunchConfiguration {
  const fallbackName = isRecord(raw) && typeof raw.name === 'string' && raw.name.trim()
    ? raw.name.trim()
    : `#${index + 1}`
  const skip = (reason: string): SkippedLaunchConfiguration => ({ name: fallbackName, reason })
  if (!isRecord(raw)) return skip('A configuration must be an object.')
  if (typeof raw.name !== 'string' || !raw.name.trim()) return skip('"name" is required.')
  const name = raw.name.trim()

  if (raw.autoPort !== undefined && typeof raw.autoPort !== 'boolean') {
    return skip(`"autoPort" must be the JSON boolean true or false, not ${JSON.stringify(raw.autoPort)}.`)
  }
  const runtimeArgs = raw.runtimeArgs === undefined ? [] : stringArray(raw.runtimeArgs)
  if (runtimeArgs === null) return skip('"runtimeArgs" must be an array of strings.')
  const programArgs = raw.args === undefined ? [] : stringArray(raw.args)
  if (programArgs === null) return skip('"args" must be an array of strings.')
  if (raw.env !== undefined && (!isRecord(raw.env) || !Object.values(raw.env).every((value) => typeof value === 'string'))) {
    return skip('"env" must map names to strings.')
  }
  const env = (raw.env ?? {}) as Record<string, string>
  const runtimeExecutable = typeof raw.runtimeExecutable === 'string' ? raw.runtimeExecutable.trim() : ''
  const program = typeof raw.program === 'string' ? raw.program.trim() : ''

  if (!runtimeExecutable && !program) {
    if (typeof raw.url !== 'string' || !raw.url.trim()) return skip('Add "runtimeExecutable" or "program" to start the server, or "url" to open one.')
    let url: URL
    try {
      url = new URL(raw.url.trim())
    } catch {
      return skip('"url" is not a valid address.')
    }
    if (url.protocol !== 'http:' && url.protocol !== 'https:') return skip('"url" must be an http or https address.')
    const local = ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)
    if (local && (url.pathname !== '/' || url.search || url.hash)) {
      return skip('A local "url" must be an origin without a path or query.')
    }
    const port = url.port ? Number(url.port) : undefined
    return { kind: 'attach', name, url: local ? url.origin : url.href, ...(port === undefined ? {} : { port }) }
  }

  const command = runtimeExecutable || 'node'
  const args = runtimeExecutable
    ? [...runtimeArgs, ...(program ? [program] : []), ...programArgs]
    : [program, ...programArgs]

  const relativeCwd = raw.cwd === undefined ? '.' : raw.cwd
  if (typeof relativeCwd !== 'string' || isAbsolute(relativeCwd)) return skip('"cwd" must be a path relative to the workspace.')
  const cwd = resolve(folder, relativeCwd)
  if (!within(resolve(folder), cwd)) return skip('"cwd" must stay inside the workspace.')

  const port = raw.port === undefined
    ? validPort(env.PORT) ?? portFromArgs(args)
    : validPort(raw.port)
  if (port === null) {
    return skip(raw.port === undefined
      ? 'Add "port", or set it through env.PORT or a --port argument.'
      : '"port" must be a whole number from 1 to 65535.')
  }
  return {
    kind: 'command',
    name,
    command,
    args,
    cwd,
    port,
    autoPort: raw.autoPort as boolean | undefined,
    env
  }
}

export function parseLaunchFile(text: string, folder: string): ParsedLaunchFile {
  let raw: unknown
  try {
    raw = JSON.parse(text)
  } catch (error) {
    return { status: 'invalid', reason: `The launch file is not valid JSON: ${(error as Error).message}` }
  }
  if (!isRecord(raw)) return { status: 'invalid', reason: 'The launch file must be a JSON object.' }
  if (!Array.isArray(raw.configurations) || raw.configurations.length === 0) {
    return { status: 'invalid', reason: 'The launch file has no "configurations".' }
  }
  const configurations: LaunchConfiguration[] = []
  const skipped: SkippedLaunchConfiguration[] = []
  const names = new Set<string>()
  raw.configurations.forEach((entry, index) => {
    const parsed = parseConfiguration(entry, index, folder)
    if (!('kind' in parsed)) {
      skipped.push(parsed)
      return
    }
    const key = parsed.name.toLowerCase()
    if (names.has(key)) {
      skipped.push({ name: parsed.name, reason: 'Another configuration already uses this name.' })
      return
    }
    names.add(key)
    configurations.push(parsed)
  })
  if (configurations.length === 0) {
    const details = skipped.map((entry) => `${entry.name}: ${entry.reason}`).join(' ')
    return { status: 'invalid', reason: `No usable configuration (${details}).` }
  }
  return { status: 'ok', autoVerify: raw.autoVerify !== false, configurations, skipped }
}

export function findConfiguration(
  configurations: readonly LaunchConfiguration[],
  name: string
): LaunchConfiguration | undefined {
  const key = name.trim().toLowerCase()
  return configurations.find((configuration) => configuration.name.toLowerCase() === key)
}

export function launchFileState(loaded: LoadedLaunchFile): LaunchFileState {
  const { path, parsed } = loaded
  if (parsed.status === 'missing') return { status: 'missing', path }
  if (parsed.status === 'invalid') return { status: 'invalid', path, reason: parsed.reason }
  const configurations: LaunchConfigurationSummary[] = parsed.configurations.map((configuration) =>
    configuration.kind === 'attach'
      ? { name: configuration.name, url: configuration.url, ...(configuration.port === undefined ? {} : { port: configuration.port }) }
      : { name: configuration.name, port: configuration.port })
  return { status: 'ok', path, configurations, skipped: parsed.skipped }
}
