import { _electron as electron } from 'playwright'
import { execFileSync } from 'child_process'
import { dirname, join } from 'path'
import { fileURLToPath } from 'url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = dirname(__filename)
const DESKTOP_DIR = join(__dirname, '..')
const WORKSPACE = 'F:\\dotcraft'
const PID_EXIT_TIMEOUT_MS = 8_000

function ts(): string {
  return new Date().toISOString().slice(11, 23)
}

function log(message: string): void {
  console.log(`[${ts()}] ${message}`)
}

function isPidAlive(pid: number): boolean {
  if (process.platform === 'win32') {
    try {
      execFileSync(
        'powershell',
        ['-NoProfile', '-Command', `Get-Process -Id ${pid} | Out-Null`],
        { stdio: 'ignore' }
      )
      return true
    } catch {
      return false
    }
  }

  try {
    execFileSync('ps', ['-p', String(pid)], { stdio: 'ignore' })
    return true
  } catch {
    return false
  }
}

async function waitForPidExit(pid: number, timeoutMs: number): Promise<boolean> {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if (!isPidAlive(pid)) return true
    await new Promise((resolve) => setTimeout(resolve, 200))
  }
  return !isPidAlive(pid)
}

async function run(): Promise<void> {
  log('Launching Desktop for terminal smoke test...')
  const app = await electron.launch({
    args: [join(DESKTOP_DIR, 'out/main/index.js'), '--workspace', WORKSPACE],
    cwd: DESKTOP_DIR,
    timeout: 30_000
  })

  try {
    const page = await app.firstWindow()
    await page.waitForSelector('#root', { timeout: 15_000 })

    const result = await page.evaluate(async () => {
      const workspacePath = await window.api.window.getWorkspacePath()
      const tabId = `e2e-terminal-${Date.now()}`
      const marker = `DOTCRAFT_TERMINAL_SMOKE_${Date.now()}`
      const chunks: string[] = []
      const unsubData = window.api.workspace.viewer.terminal.onData((event) => {
        if (event.tabId !== tabId) return
        chunks.push(event.data)
      })
      const unsubExit = window.api.workspace.viewer.terminal.onExit(() => {})
      try {
        const created = await window.api.workspace.viewer.terminal.create({
          tabId,
          threadId: 'e2e-thread',
          workspacePath,
          cols: 80,
          rows: 24
        })
        await window.api.workspace.viewer.terminal.write({
          tabId,
          data: `echo ${marker}\r`
        })

        const deadline = Date.now() + 8_000
        while (Date.now() < deadline) {
          if (chunks.join('').includes(marker)) break
          await new Promise((resolve) => setTimeout(resolve, 50))
        }
        const output = chunks.join('')
        const matched = output.includes(marker)
        await window.api.workspace.viewer.terminal.dispose({ tabId })
        return { pid: created.pid, marker, matched, outputSample: output.slice(-500) }
      } finally {
        unsubData()
        unsubExit()
      }
    })

    if (!result.matched) {
      throw new Error(`Terminal did not echo marker. Sample output: ${result.outputSample}`)
    }
    log(`Echo check passed (pid=${result.pid}). Waiting for process exit...`)
    const exited = await waitForPidExit(result.pid, PID_EXIT_TIMEOUT_MS)
    if (!exited) {
      throw new Error(`Terminal process ${result.pid} is still alive after dispose.`)
    }
    log('Terminal process exited after tab dispose.')
    log('Terminal smoke test PASSED.')
  } finally {
    await app.close()
  }
}

run().catch((error) => {
  log(`Terminal smoke test FAILED: ${String(error)}`)
  process.exit(1)
})
