import { app } from 'electron'
import { execFile } from 'child_process'
import { existsSync, promises as fsPromises, readFileSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import net from 'net'

export interface ChromeOpenRequest {
  url?: string
}

export interface ChromeSetupCheckStatus {
  ok: boolean
  code: string
  message: string
  action?: string
  safeDetails?: Record<string, string | number | boolean>
}

export interface ChromeSetupStatus {
  extension: ChromeSetupCheckStatus
  nativeHost: ChromeSetupCheckStatus
  chromeRunning: ChromeSetupCheckStatus
  installedBrowsers: ChromeSetupCheckStatus
  backend: ChromeSetupCheckStatus
  bridge: ChromeSetupCheckStatus
}

export function resolveBundledChromePluginRoot(): string {
  if (app.isPackaged) {
    return join(process.resourcesPath, 'plugins', 'dotcraft-bundled', 'plugins', 'chrome')
  }
  return join(app.getAppPath(), 'resources', 'plugins', 'dotcraft-bundled', 'plugins', 'chrome')
}

export function resolveChromePluginRoot(workspacePath?: string): string {
  const workspace = workspacePath?.trim()
  if (workspace) {
    const installed = join(workspace, '.craft', 'plugins', 'chrome')
    if (existsSync(installed)) return installed

    const bundled = resolveBundledChromePluginRoot()
    if (existsSync(bundled)) return bundled

    return installed
  }

  return resolveBundledChromePluginRoot()
}

function chromeScriptPath(workspacePath: string | undefined, scriptName: string): string {
  return join(resolveChromePluginRoot(workspacePath), 'scripts', scriptName)
}

function chromeExtensionMetadataPath(workspacePath?: string): string {
  return chromeScriptPath(workspacePath, 'extension-id.json')
}

function readChromeExtensionId(workspacePath?: string): string | null {
  try {
    const metadata = JSON.parse(readFileSync(chromeExtensionMetadataPath(workspacePath), 'utf8')) as { extensionId?: unknown }
    const extensionId = typeof metadata.extensionId === 'string' ? metadata.extensionId.trim() : ''
    return /^[a-z]{32}$/i.test(extensionId) ? extensionId : null
  } catch {
    return null
  }
}

export function resolveChromeExtensionManagementUrl(workspacePath?: string): string {
  const extensionId = readChromeExtensionId(workspacePath)
  return extensionId ? `chrome://extensions/?id=${encodeURIComponent(extensionId)}` : 'chrome://extensions'
}

function resolveNodeRuntime(): { command: string; env: NodeJS.ProcessEnv } {
  if (app.isPackaged) {
    return {
      command: process.execPath,
      env: { ...process.env, ELECTRON_RUN_AS_NODE: '1' }
    }
  }
  return { command: 'node', env: { ...process.env } }
}

export function runChromeSetupScript(
  workspacePath: string | undefined,
  scriptName: string,
  args: string[]
): Promise<unknown> {
  return new Promise((resolve) => {
    const scriptPath = chromeScriptPath(workspacePath, scriptName)
    if (!existsSync(scriptPath)) {
      resolve({ ok: false, script: scriptPath, error: 'Script not found.' })
      return
    }

    const runtime = resolveNodeRuntime()
    execFile(runtime.command, [scriptPath, ...args], { timeout: 10_000, env: runtime.env }, (error, stdout, stderr) => {
      const text = String(stdout || '').trim()
      let parsed: unknown = text
      if (text) {
        try {
          parsed = JSON.parse(text)
        } catch {
          parsed = text
        }
      }
      if (error) {
        resolve({
          ok: false,
          script: scriptPath,
          error: error.message,
          stderr: String(stderr || '').trim(),
          result: parsed
        })
        return
      }
      resolve(parsed || { ok: true, script: scriptPath })
    })
  })
}

export async function checkChromeSetup(workspacePath?: string): Promise<ChromeSetupStatus> {
  const [extensionRaw, nativeHostRaw, chromeRunningRaw, installedBrowsersRaw, backendRaw] = await Promise.all([
    runChromeSetupScript(workspacePath, 'check-extension-installed.js', ['--json']),
    runChromeSetupScript(workspacePath, 'check-native-host-manifest.js', ['--json']),
    runChromeSetupScript(workspacePath, 'chrome-is-running.js', ['--check', '--json']),
    runChromeSetupScript(workspacePath, 'installed-browsers.js', ['--check', '--json']),
    checkChromeBridge()
  ])
  const backend = normalizeSetupCheck('backend', backendRaw)
  return {
    extension: normalizeSetupCheck('extension', extensionRaw),
    nativeHost: normalizeSetupCheck('nativeHost', nativeHostRaw),
    chromeRunning: normalizeSetupCheck('chromeRunning', chromeRunningRaw),
    installedBrowsers: normalizeSetupCheck('installedBrowsers', installedBrowsersRaw),
    backend,
    bridge: backend
  }
}

export function checkChromeBridge(): Promise<unknown> {
  return checkChromeBackendDiscovery()
}

async function chromePipeCandidates(): Promise<string[]> {
  if (process.platform === 'win32') {
    try {
      const names = await fsPromises.readdir('\\\\.\\pipe\\')
      return names.filter((name) => name.startsWith('dotcraft-chrome-')).map((name) => `\\\\.\\pipe\\${name}`)
    } catch {
      return []
    }
  }
  try {
    const names = await fsPromises.readdir(tmpdir())
    return names
      .filter((name) => name.startsWith('dotcraft-chrome-') && name.endsWith('.sock'))
      .map((name) => join(tmpdir(), name))
  } catch {
    return []
  }
}

function encodeFrame(message: unknown): Buffer {
  const body = Buffer.from(JSON.stringify(message), 'utf8')
  const header = Buffer.alloc(4)
  header.writeUInt32LE(body.length, 0)
  return Buffer.concat([header, body])
}

function decodeFirstFrame(buffer: Buffer): unknown | undefined {
  if (buffer.length < 4) return undefined
  const length = buffer.readUInt32LE(0)
  if (buffer.length < length + 4) return undefined
  return JSON.parse(buffer.subarray(4, 4 + length).toString('utf8'))
}

async function checkChromeBackendDiscovery(): Promise<unknown> {
  const candidates = await chromePipeCandidates()
  if (candidates.length === 0) {
    return {
      ok: false,
      code: 'backendDisconnected',
      candidateCount: 0,
      error: 'Chrome backend native pipe was not discovered.',
      action: 'clickExtensionRefresh'
    }
  }

  const failures: string[] = []
  for (const pipePath of candidates) {
    const result = await probeChromePipe(pipePath)
    if ((result as { ok?: boolean }).ok === true) {
      return { ...(result as Record<string, unknown>), candidateCount: candidates.length, code: 'backendConnected' }
    }
    failures.push(normalizeBridgeError((result as { error?: unknown }).error))
  }
  return {
    ok: false,
    code: 'backendDisconnected',
    candidateCount: candidates.length,
    error: failures[0] ?? 'Chrome backend is not connected.',
    action: 'clickExtensionRefresh'
  }
}

function probeChromePipe(pipePath: string): Promise<unknown> {
  return new Promise((resolve) => {
    const socket = net.createConnection({ path: pipePath })
    let buffer = Buffer.alloc(0)
    let settled = false
    let timer: ReturnType<typeof setTimeout>
    const finish = (payload: unknown): void => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      socket.destroy()
      resolve(payload)
    }
    timer = setTimeout(() => {
      finish({ ok: false, error: 'Chrome backend did not respond.' })
    }, 1_000)

    socket.on('connect', () => {
      socket.write(encodeFrame({ id: 1, kind: 'command', method: 'getInfo', params: {}, timeoutMs: 1000 }))
    })
    socket.on('data', (chunk) => {
      buffer = Buffer.concat([buffer, Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk)])
      try {
        const parsed = decodeFirstFrame(buffer) as { ok?: boolean; result?: { protocolVersion?: number; backendId?: string }; error?: unknown } | undefined
        if (!parsed) return
        finish(parsed.ok === true && parsed.result?.protocolVersion === 3
          ? { ok: true, backendId: parsed.result.backendId, protocolVersion: parsed.result.protocolVersion }
          : { ok: false, error: normalizeBridgeError(parsed.error) })
      } catch (error) {
        finish({ ok: false, error: normalizeBridgeError(error) })
      }
    })
    socket.on('error', (error) => {
      finish({ ok: false, error: normalizeBridgeError(error) })
    })
    socket.on('close', () => {
      finish({ ok: false, error: 'Chrome backend closed before responding.' })
    })
  })
}

function normalizeBridgeError(error: unknown): string {
  if (error && typeof error === 'object' && 'message' in error && typeof error.message === 'string') {
    return error.message
  }
  return typeof error === 'string' ? error : 'Chrome backend is not connected.'
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value != null && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null
}

function numberField(record: Record<string, unknown> | null, key: string): number | undefined {
  const value = record?.[key]
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined
}

function stringField(record: Record<string, unknown> | null, key: string): string | undefined {
  const value = record?.[key]
  return typeof value === 'string' && value.trim() ? value.trim() : undefined
}

function booleanField(record: Record<string, unknown> | null, key: string): boolean | undefined {
  const value = record?.[key]
  return typeof value === 'boolean' ? value : undefined
}

function setupSafeDetails(kind: string, record: Record<string, unknown> | null): ChromeSetupCheckStatus['safeDetails'] | undefined {
  const details: Record<string, string | number | boolean> = {}
  if (kind === 'backend') {
    const protocolVersion = numberField(record, 'protocolVersion')
    const candidateCount = numberField(record, 'candidateCount')
    const backendId = stringField(record, 'backendId')
    if (protocolVersion != null) details.protocolVersion = protocolVersion
    if (candidateCount != null) details.candidateCount = candidateCount
    if (backendId) details.backendId = backendId
  }
  if (kind === 'chromeRunning') {
    const processCount = numberField(record, 'processCount')
    if (processCount != null) details.processCount = processCount
  }
  if (kind === 'installedBrowsers') {
    const browsers = record?.browsers
    if (Array.isArray(browsers)) details.browserCount = browsers.length
  }
  if (kind === 'nativeHost') {
    for (const key of ['exists', 'hostExists', 'wrapperValid']) {
      const value = booleanField(record, key)
      if (value != null) details[key] = value
    }
  }
  return Object.keys(details).length > 0 ? details : undefined
}

function normalizeSetupCheck(kind: string, value: unknown): ChromeSetupCheckStatus {
  const record = asRecord(value)
  const ok = record?.ok === true
  const code = stringField(record, 'code')

  if (kind === 'installedBrowsers') {
    return {
      ok,
      code: code ?? (ok ? 'chromeInstalled' : 'chromeMissing'),
      message: ok ? 'Google Chrome is installed.' : 'Google Chrome was not found.',
      action: ok ? undefined : 'installChrome',
      safeDetails: setupSafeDetails(kind, record)
    }
  }
  if (kind === 'extension') {
    return {
      ok,
      code: code ?? (ok ? 'extensionReady' : 'extensionNotReady'),
      message: ok ? 'DotCraft Chrome extension is ready.' : 'DotCraft Chrome extension is not ready.',
      action: ok ? undefined : 'openExtensions'
    }
  }
  if (kind === 'nativeHost') {
    const message = stringField(record, 'message')
    return {
      ok,
      code: code ?? (ok ? 'nativeHostReady' : 'nativeHostMissing'),
      message: message ?? (ok ? 'Chrome Native Host is installed.' : 'Chrome Native Host needs to be installed or repaired.'),
      action: ok ? undefined : 'repairNativeHost',
      safeDetails: setupSafeDetails(kind, record)
    }
  }
  if (kind === 'chromeRunning') {
    return {
      ok,
      code: code ?? (ok ? 'chromeRunning' : 'chromeNotRunning'),
      message: ok ? 'Chrome is running.' : 'Chrome is not running.',
      action: ok ? undefined : 'openChrome',
      safeDetails: setupSafeDetails(kind, record)
    }
  }
  return {
    ok,
    code: code ?? (ok ? 'backendConnected' : 'backendDisconnected'),
    message: ok ? 'Chrome backend is connected.' : 'Chrome backend is disconnected.',
    action: ok ? undefined : 'clickExtensionRefresh',
    safeDetails: setupSafeDetails('backend', record)
  }
}

export async function installChromeNativeHost(workspacePath?: string): Promise<unknown> {
  return runChromeSetupScript(workspacePath, 'installManifest.mjs', [])
}

export async function openChromeWindow(
  workspacePath: string | undefined,
  request: ChromeOpenRequest = {}
): Promise<unknown> {
  const url = normalizeChromeOpenUrl(request.url, workspacePath)
  return runChromeSetupScript(workspacePath, 'open-chrome-window.js', ['--json', url])
}

function normalizeChromeOpenUrl(value?: string, workspacePath?: string): string {
  const trimmed = value?.trim()
  if (!trimmed) return 'about:blank'
  if (trimmed === 'about:blank') return trimmed
  if (/^https?:\/\//i.test(trimmed)) return trimmed
  if (/^chrome:\/\/extensions\/?$/i.test(trimmed)) return resolveChromeExtensionManagementUrl(workspacePath)
  if (/^chrome:\/\/extensions(?:\/|\?|$)/i.test(trimmed)) return trimmed
  return 'about:blank'
}
