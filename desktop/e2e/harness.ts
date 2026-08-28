/**
 * Reusable Electron automation harness for DotCraft Desktop.
 *
 * Wraps Playwright's Electron driver with helpers that drive the app through the
 * renderer automation bridge (window.__DOTCRAFT_E2E / __DOTCRAFT_STORES) so callers
 * can open any surface, read store state, and capture screenshots without depending
 * on localized button text.
 *
 * Used by e2e/shot.ts (one-shot page screenshots) and available for bespoke scripts.
 */

import { _electron as electron, type ElectronApplication, type Page } from 'playwright'
import { execFileSync } from 'child_process'
import { existsSync, mkdirSync } from 'fs'
import { dirname, isAbsolute, join, resolve } from 'path'
import { fileURLToPath } from 'url'

const __dirname = dirname(fileURLToPath(import.meta.url))

export const DESKTOP_DIR = join(__dirname, '..')
export const REPO_ROOT = resolve(DESKTOP_DIR, '..')
export const MAIN_ENTRY = join(DESKTOP_DIR, 'out', 'main', 'index.js')
export const SCREENSHOTS_DIR = join(__dirname, 'screenshots')

export type MainView = 'conversation' | 'skills' | 'automations' | 'settings' | 'channels' | 'teams'
export type SettingsTab =
  | 'general'
  | 'personalization'
  | 'dreams'
  | 'connection'
  | 'llmService'
  | 'browserUse'
  | 'computerControl'
  | 'usage'
  | 'archivedThreads'
  | 'mcp'
  | 'subAgents'
export type AutomationsTab = 'tasks' | 'cron'
export type DetailTab = 'changes' | 'plan'

/** Read-only store names exposed by window.__DOTCRAFT_STORES. */
export type StoreName =
  | 'ui'
  | 'thread'
  | 'conversation'
  | 'connection'
  | 'automations'
  | 'cron'
  | 'plugin'
  | 'skills'
  | 'modelCatalog'
  | 'providers'
  | 'mcp'
  | 'subAgent'

export interface LaunchOptions {
  /** Workspace folder to open. Defaults to $DOTCRAFT_E2E_WORKSPACE then the repo root. */
  workspace?: string
  /** When to rebuild renderer/main: 'if-missing' (default), 'always', or 'never'. */
  build?: 'if-missing' | 'always' | 'never'
  /** Connect to an external AppServer over WebSocket instead of spawning one. */
  remote?: string
  /** Launch timeout in ms (default 30s). */
  launchTimeoutMs?: number
  /** Extra CLI args forwarded to the Electron main process. */
  extraArgs?: string[]
}

export interface ThreadSummaryLite {
  id: string
  name: string | null
  status: string
}

export interface Harness {
  app: ElectronApplication
  page: Page
  /** Resolve when the AppServer connection reports 'connected'. */
  waitForConnected(timeoutMs?: number): Promise<void>
  /** Switch the primary surface and wait for it to mount (best-effort). */
  gotoView(view: MainView, options?: { waitTimeoutMs?: number }): Promise<void>
  /** Open Settings on a specific section. */
  gotoSettings(tab: SettingsTab, options?: { waitTimeoutMs?: number }): Promise<void>
  /** Open Automations on a specific tab. */
  gotoAutomations(tab: AutomationsTab, options?: { waitTimeoutMs?: number }): Promise<void>
  /** Reveal the detail panel and switch to a system tab. */
  setDetailTab(tab: DetailTab): Promise<void>
  /** Open a thread by id in the conversation surface. */
  openThread(threadId: string, options?: { waitTimeoutMs?: number }): Promise<void>
  /** Snapshot a store's getState() output, or null if the bridge is missing. */
  readStore<T = Record<string, unknown>>(name: StoreName): Promise<T | null>
  /** Lightweight thread list for navigation targeting. */
  listThreads(): Promise<ThreadSummaryLite[]>
  /** Capture a PNG under SCREENSHOTS_DIR (or an absolute path) and return its path. */
  screenshot(nameOrPath: string, options?: { fullPage?: boolean }): Promise<string>
  /** Close the app. */
  close(): Promise<void>
}

function log(message: string): void {
  console.log(`[harness] ${message}`)
}

export function resolveWorkspace(workspace?: string): string {
  const candidate = workspace ?? process.env.DOTCRAFT_E2E_WORKSPACE ?? REPO_ROOT
  return isAbsolute(candidate) ? candidate : resolve(process.cwd(), candidate)
}

export function buildRenderer(): void {
  log('Building (electron-vite build)...')
  execFileSync('npx', ['electron-vite', 'build'], {
    cwd: DESKTOP_DIR,
    stdio: 'inherit',
    shell: true
  })
}

function ensureBuild(mode: NonNullable<LaunchOptions['build']>): void {
  if (mode === 'never') return
  if (mode === 'always' || !existsSync(MAIN_ENTRY)) {
    buildRenderer()
  }
}

export async function launchDesktop(options: LaunchOptions = {}): Promise<Harness> {
  ensureBuild(options.build ?? 'if-missing')

  const workspace = resolveWorkspace(options.workspace)
  const args = [MAIN_ENTRY, '--workspace', workspace]
  if (options.remote) args.push('--remote', options.remote)
  if (options.extraArgs?.length) args.push(...options.extraArgs)

  log(`Launching Electron (workspace=${workspace}${options.remote ? `, remote=${options.remote}` : ''})...`)
  const app = await electron.launch({
    args,
    cwd: DESKTOP_DIR,
    timeout: options.launchTimeoutMs ?? 30_000
  })

  const page = await app.firstWindow()
  await page.waitForSelector('#root', { timeout: 15_000 })

  const harness: Harness = {
    app,
    page,

    async waitForConnected(timeoutMs = 30_000) {
      await page.waitForFunction(
        () => window.__DOTCRAFT_E2E?.connectionStatus?.() === 'connected',
        undefined,
        { timeout: timeoutMs }
      )
    },

    async gotoView(view, opts) {
      await page.evaluate((v) => window.__DOTCRAFT_E2E?.setMainView(v as never), view)
      await waitForSurface(page, view, opts?.waitTimeoutMs)
    },

    async gotoSettings(tab, opts) {
      await page.evaluate((t) => window.__DOTCRAFT_E2E?.setSettingsTab(t as never), tab)
      await waitForSurface(page, 'settings', opts?.waitTimeoutMs)
    },

    async gotoAutomations(tab, opts) {
      await page.evaluate((t) => window.__DOTCRAFT_E2E?.setAutomationsTab(t as never), tab)
      await waitForSurface(page, 'automations', opts?.waitTimeoutMs)
    },

    async setDetailTab(tab) {
      await page.evaluate((t) => window.__DOTCRAFT_E2E?.setDetailTab(t as never), tab)
    },

    async openThread(threadId, opts) {
      await page.evaluate((id) => window.__DOTCRAFT_E2E?.openThread(id), threadId)
      await waitForSurface(page, 'conversation', opts?.waitTimeoutMs)
    },

    async readStore<T = Record<string, unknown>>(name: StoreName) {
      return page.evaluate((n) => {
        const stores = window.__DOTCRAFT_STORES
        if (!stores) return null
        const getter = stores[n as keyof typeof stores]
        return getter ? (getter() as unknown) : null
      }, name) as Promise<T | null>
    },

    async listThreads() {
      const result = await page.evaluate(() => window.__DOTCRAFT_E2E?.listThreads?.() ?? [])
      return result as ThreadSummaryLite[]
    },

    async screenshot(nameOrPath, opts) {
      if (!existsSync(SCREENSHOTS_DIR)) mkdirSync(SCREENSHOTS_DIR, { recursive: true })
      const path = isAbsolute(nameOrPath)
        ? nameOrPath
        : join(SCREENSHOTS_DIR, nameOrPath.endsWith('.png') ? nameOrPath : `${nameOrPath}.png`)
      await page.screenshot({ path, fullPage: opts?.fullPage ?? false })
      log(`Screenshot -> ${path}`)
      return path
    },

    async close() {
      try {
        await app.close()
      } catch {
        // Ignore close races.
      }
    }
  }

  return harness
}

async function waitForSurface(page: Page, view: MainView, timeoutMs = 5_000): Promise<void> {
  try {
    await page.waitForSelector(`[data-testid="view-${view}"]`, { timeout: timeoutMs })
  } catch {
    // The requested view may be gated/redirected (e.g. teams unavailable). Continue
    // so callers still capture whatever surface actually rendered.
    log(`view-${view} did not mount within ${timeoutMs}ms (may be gated/redirected)`)
  }
}
