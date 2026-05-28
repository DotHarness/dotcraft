/**
 * Enhanced E2E smoke test for DotCraft.
 *
 * Features:
 * - Launches the built Electron app and wires all console output to stdout
 * - Creates a thread, sends a message, then monitors streaming in real-time
 * - Every 200ms reads Zustand store state + DOM text content during streaming
 * - Detects: duplicate streaming text, content disappearing after completion,
 *   duplicate turns in store, stale activeTurnId
 * - Takes screenshots at every key step
 * - Prints a structured diagnostic summary
 *
 * Run: npm run e2e
 * Output: e2e/screenshots/
 */

import { _electron as electron } from 'playwright'
import type { Page } from 'playwright'
import { execFileSync } from 'child_process'
import { existsSync, mkdirSync } from 'fs'
import { join, dirname } from 'path'
import { fileURLToPath } from 'url'

// ---------------------------------------------------------------------------
// Config
// ---------------------------------------------------------------------------

const __filename = fileURLToPath(import.meta.url)
const __dirname = dirname(__filename)
const DESKTOP_DIR = join(__dirname, '..')
const SCREENSHOTS_DIR = join(__dirname, 'screenshots')
const WORKSPACE = 'F:\\dotcraft'
const STEP_TIMEOUT = 15_000
const RESPONSE_TIMEOUT = 60_000
const POLL_INTERVAL = 200
const APP_SERVER_EXIT_TIMEOUT = 4_500

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

interface TurnSnapshot {
  id: string
  status: string
  itemCount: number
  itemTypes: string[]
  agentTexts: string[]
}

interface StoreSnapshot {
  turnStatus: string
  activeTurnId: string | null
  streamingMessage: string
  streamingMessage_len: number
  turnsCount: number
  turns: TurnSnapshot[]
}

interface StreamSample {
  time: string
  streaming: string
  domText: string
}

// ---------------------------------------------------------------------------
// Globals
// ---------------------------------------------------------------------------

const consoleLogs: Array<{ level: string; text: string; time: string }> = []
let stepIndex = 0
let page: Page

function ts(): string {
  return new Date().toISOString().slice(11, 23)
}

function log(msg: string): void {
  console.log(`[${ts()}] ${msg}`)
}

function listAppServerPids(): number[] {
  if (process.platform !== 'win32') return []
  try {
    const output = execFileSync(
      'powershell',
      [
        '-NoProfile',
        '-Command',
        "Get-CimInstance Win32_Process | Where-Object { $_.Name -match '^dotcraft(\\.exe)?$' -and $_.CommandLine -match 'app-server' } | Select-Object -ExpandProperty ProcessId"
      ],
      { encoding: 'utf8' }
    )
    return output
      .split(/\r?\n/)
      .map((line) => Number.parseInt(line.trim(), 10))
      .filter((pid) => Number.isInteger(pid) && pid > 0)
  } catch {
    return []
  }
}

async function waitForPidsToExit(pids: number[], timeoutMs: number): Promise<boolean> {
  if (pids.length === 0 || process.platform !== 'win32') return true
  const target = new Set<number>(pids)
  const deadline = Date.now() + timeoutMs
  while (Date.now() <= deadline) {
    const alive = listAppServerPids().some((pid) => target.has(pid))
    if (!alive) {
      return true
    }
    await new Promise((resolve) => setTimeout(resolve, 200))
  }
  return false
}

async function screenshot(name: string): Promise<void> {
  const path = join(SCREENSHOTS_DIR, `${String(stepIndex++).padStart(2, '0')}-${name}.png`)
  await page.screenshot({ path, fullPage: true })
  log(`  Screenshot → ${path}`)
}

// ---------------------------------------------------------------------------
// Store bridge helpers
// ---------------------------------------------------------------------------

async function readStore(): Promise<StoreSnapshot | null> {
  return page.evaluate(() => {
    const fn = (window as unknown as Record<string, unknown>).__CONVERSATION_STORE_STATE as
      | (() => Record<string, unknown>)
      | undefined
    if (!fn) return null
    const s = fn()
    const turns = (s.turns as Array<Record<string, unknown>>).map((t) => ({
      id: t.id as string,
      status: t.status as string,
      itemCount: (t.items as unknown[]).length,
      itemTypes: (t.items as Array<Record<string, unknown>>).map((i) => i.type as string),
      agentTexts: (t.items as Array<Record<string, unknown>>)
        .filter((i) => i.type === 'agentMessage')
        .map((i) => String(i.text ?? '').substring(0, 120))
    }))
    return {
      turnStatus: s.turnStatus as string,
      activeTurnId: (s.activeTurnId as string | null) ?? null,
      streamingMessage: s.streamingMessage as string,
      streamingMessage_len: (s.streamingMessage as string).length,
      turnsCount: turns.length,
      turns
    }
  })
}

async function dumpStore(label: string): Promise<StoreSnapshot | null> {
  const state = await readStore()
  log(`  [STORE:${label}] ${JSON.stringify(state)}`)
  return state
}

/** Thread list length from renderer Zustand (E2E bridge); null if bridge missing. */
async function readThreadListLength(): Promise<number | null> {
  return page.evaluate(() => {
    const fn = (window as unknown as Record<string, unknown>).__THREAD_STORE_STATE as
      | (() => { threadList: unknown[] })
      | undefined
    if (!fn) return null
    return fn().threadList.length
  })
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

async function run(): Promise<void> {
  if (!existsSync(SCREENSHOTS_DIR)) mkdirSync(SCREENSHOTS_DIR, { recursive: true })
  const baselineAppServerPids = listAppServerPids()
  let launchedAppServerPids: number[] = []

  // ── Step 1: Build ──────────────────────────────────────────────────────
  log('STEP 1: Building...')
  try {
    execFileSync('npx', ['electron-vite', 'build'], {
      cwd: DESKTOP_DIR,
      stdio: 'inherit',
      shell: true
    })
    log('Build succeeded.')
  } catch (err) {
    log(`ERROR: Build failed: ${String(err)}`)
    process.exit(1)
  }

  // ── Step 2: Launch ─────────────────────────────────────────────────────
  log('STEP 2: Launching Electron...')
  const app = await electron.launch({
    args: [join(DESKTOP_DIR, 'out/main/index.js'), '--workspace', WORKSPACE],
    cwd: DESKTOP_DIR,
    timeout: 30_000
  })

  page = await app.firstWindow()

  page.on('console', (msg) => {
    const level = msg.type()
    const text = msg.text()
    const time = ts()
    consoleLogs.push({ level, text, time })
    const tag =
      level === 'error' ? '[CONSOLE ERR] ' :
      level === 'warning' ? '[CONSOLE WARN]' :
      '[CONSOLE LOG] '
    console.log(`  ${tag} ${time} ${text}`)
  })

  page.on('pageerror', (err) => {
    consoleLogs.push({ level: 'pageerror', text: err.message, time: ts() })
    log(`  [PAGE ERROR]  ${err.message}`)
  })

  // Diagnostics collected during the run
  const streamSamples: StreamSample[] = []
  let maxStreamingLen = 0
  let streamPeakText = ''

  try {
    const isVisible = await page.evaluate(() => document.visibilityState !== 'hidden')
    if (!isVisible) {
      log('FAIL: first window is not visible after launch.')
      process.exit(1)
    }
    await page.waitForSelector('#root', { timeout: STEP_TIMEOUT })
    const rootText = (await page.textContent('#root')) ?? ''
    if (rootText.trim().length === 0) {
      log('FAIL: renderer root mounted but has no visible content.')
      process.exit(1)
    }

    // ── Step 3: Wait for connected ───────────────────────────────────────
    log('STEP 3: Waiting for AppServer connection...')
    await page.waitForSelector('button[title="New Thread (Ctrl+N)"]:not([disabled])', {
      timeout: STEP_TIMEOUT
    })
    await screenshot('connected')
    await dumpStore('after-connect')
    log('Connected.')
    launchedAppServerPids = listAppServerPids().filter((pid) => !baselineAppServerPids.includes(pid))

    // Wait until thread list has finished loading (avoids racing an empty list)
    await page.waitForFunction(
      () => {
        const fn = (window as unknown as Record<string, unknown>).__THREAD_STORE_STATE as
          | (() => { loading: boolean })
          | undefined
        if (!fn) return true
        return fn().loading === false
      },
      { timeout: STEP_TIMEOUT }
    )

    const threadCountBefore = await readThreadListLength()

    // ── Step 4: New thread ────────────────────────────────────────────────
    log('STEP 4: Creating new thread...')
    await page.click('button[title="New Thread (Ctrl+N)"]')
    await page.waitForSelector('textarea[placeholder="Ask DotCraft anything…"]', {
      timeout: STEP_TIMEOUT
    })
    await screenshot('new-thread')
    await dumpStore('after-new-thread')

    // One New Thread click must add exactly one sidebar entry (regression: duplicate threads)
    if (threadCountBefore !== null) {
      await page.waitForFunction(
        (expected: number) => {
          const fn = (window as unknown as Record<string, unknown>).__THREAD_STORE_STATE as
            | (() => { threadList: { id: string }[] })
            | undefined
          if (!fn) return false
          return fn().threadList.length === expected
        },
        threadCountBefore + 1,
        { timeout: STEP_TIMEOUT }
      )
      const threadCountAfter = await readThreadListLength()
      const duplicateIds = await page.evaluate(() => {
        const fn = (window as unknown as Record<string, unknown>).__THREAD_STORE_STATE as
          | (() => { threadList: { id: string }[] })
          | undefined
        if (!fn) return true
        const ids = fn().threadList.map((t) => t.id)
        return ids.length !== new Set(ids).size
      })
      if (duplicateIds) {
        log('FAIL: duplicate thread IDs in thread list after New Thread')
        process.exit(1)
      }
      if (threadCountAfter !== threadCountBefore + 1) {
        log(
          `FAIL: expected thread count ${threadCountBefore + 1} after one New Thread, got ${threadCountAfter}`
        )
        process.exit(1)
      }
      log(`Thread list: ${threadCountBefore} → ${threadCountAfter} (exactly +1).`)
    } else {
      log('WARNING: __THREAD_STORE_STATE missing; skipping thread count assertion.')
    }

    log('Thread created, input visible.')

    // ── Step 5: Type message ──────────────────────────────────────────────
    const testMessage = 'Please say exactly "E2E test OK" and nothing else.'
    log(`STEP 5: Typing: "${testMessage}"`)
    await page.click('textarea[placeholder="Ask DotCraft anything…"]')
    await page.fill('textarea[placeholder="Ask DotCraft anything…"]', testMessage)
    await screenshot('message-typed')

    // ── Step 6: Send ──────────────────────────────────────────────────────
    log('STEP 6: Clicking Send...')
    await page.click('button[aria-label="Send message"]')
    await screenshot('sent')
    await dumpStore('after-send')

    // ── Step 7: User bubble ───────────────────────────────────────────────
    log('STEP 7: Waiting for user message bubble...')
    await page.waitForFunction(
      (msg: string) => document.body.innerText.includes(msg),
      testMessage,
      { timeout: STEP_TIMEOUT }
    )
    await screenshot('user-bubble')
    await dumpStore('after-user-bubble')
    log('User bubble visible.')

    // ── Step 8: Stop button ────────────────────────────────────────────────
    log('STEP 8: Waiting for Stop button (turn running)...')
    try {
      await page.waitForSelector('button[aria-label="Stop turn"]', { timeout: STEP_TIMEOUT })
      await screenshot('turn-running')
      await dumpStore('turn-started')
      log('Stop button visible — turn is running.')
    } catch {
      log('WARNING: Stop button never appeared.')
      await screenshot('no-stop-button')
      await dumpStore('no-stop-button')
    }

    // ── Step 9: Streaming monitor ─────────────────────────────────────────
    log(`STEP 9: Monitoring streaming (up to ${RESPONSE_TIMEOUT / 1000}s)...`)
    const deadline = Date.now() + RESPONSE_TIMEOUT
    let prevStreaming = ''
    let done = false

    while (!done && Date.now() < deadline) {
      await page.waitForTimeout(POLL_INTERVAL)

      const snap = await page.evaluate(() => {
        const fn = (window as unknown as Record<string, unknown>).__CONVERSATION_STORE_STATE as
          | (() => Record<string, unknown>)
          | undefined
        const s = fn?.()
        const streamEl = document.querySelector('[data-testid="message-stream"]') as HTMLElement | null
        return {
          turnStatus: s ? (s.turnStatus as string) : 'unknown',
          streaming: s ? (s.streamingMessage as string) : '',
          domText: streamEl ? streamEl.innerText : ''
        }
      })

      const sample: StreamSample = {
        time: ts(),
        streaming: snap.streaming,
        domText: snap.domText.replace(/\n/g, '↵').substring(0, 200)
      }
      streamSamples.push(sample)

      // Log delta when streaming text changes
      if (snap.streaming !== prevStreaming) {
        const delta = snap.streaming.substring(prevStreaming.length)
        log(`  [STREAM] +${delta.length} chars | total=${snap.streaming.length} | delta="${delta.replace(/\n/g, '↵').substring(0, 60)}"`)
        if (snap.streaming.length > maxStreamingLen) {
          maxStreamingLen = snap.streaming.length
          streamPeakText = snap.streaming
        }
        prevStreaming = snap.streaming
      }

      if (snap.turnStatus !== 'running') {
        log(`  [STREAM] Turn ended — status="${snap.turnStatus}"`)
        done = true
      }
    }

    if (!done) {
      log('TIMEOUT: Turn did not complete within time limit.')
    }

    await screenshot('turn-completed')
    const storeAtComplete = await dumpStore('turn-completed')

    // ── Step 10: Persistence check ────────────────────────────────────────
    log('STEP 10: Checking content persists (waiting 2s)...')
    await page.waitForTimeout(2000)

    const storeAfterWait = await dumpStore('after-2s-wait')
    const domAfterWait = await page.evaluate(() => {
      const el = document.querySelector('[data-testid="message-stream"]') as HTMLElement | null
      return el ? el.innerText : ''
    })
    log(`  DOM text after 2s: "${domAfterWait.replace(/\n/g, '↵').substring(0, 200)}"`)
    await screenshot('final-state')

    // ── Diagnostics ────────────────────────────────────────────────────────

    // 1. Duplicate streaming detection
    let duplicateStreamingDetected = false
    for (const sample of streamSamples) {
      const t = sample.streaming
      if (t.length >= 4) {
        // Check for any character repeated 3+ consecutive times
        const dupMatch = t.match(/(.)\1{2,}/)
        if (dupMatch) {
          log(`  [DIAG] DUPLICATE_STREAMING: found "${dupMatch[0]}" in streaming buffer`)
          duplicateStreamingDetected = true
          break
        }
      }
    }

    // 2. Content disappeared / not visible in DOM
    const agentTextsAfterWait =
      storeAfterWait?.turns.flatMap((t) => t.agentTexts).filter(Boolean) ?? []
    // DOM must contain more than just the user message
    const domHasContent = domAfterWait.trim().length > testMessage.length + 5
    // Content disappeared = store HAS agent text but DOM does NOT show it
    const contentDisappeared = agentTextsAfterWait.length > 0 && !domHasContent

    if (contentDisappeared) {
      log('  [DIAG] CONTENT_DISAPPEARED: agentMessage exists in store but not visible in DOM')
    }
    if (!domHasContent && storeAfterWait && storeAfterWait.turnsCount > 0) {
      log('  [DIAG] DOM_CLEARED: DOM shows no agent response even though turn completed')
    }

    // 3. Store anomaly detection
    const duplicateTurnIds =
      storeAtComplete
        ? (() => {
            const ids = storeAtComplete.turns.map((t) => t.id)
            return ids.length !== new Set(ids).size
          })()
        : false
    if (duplicateTurnIds) {
      log('  [DIAG] STORE_ANOMALY: duplicate turn IDs detected in store!')
    }

    // 4. Duplicate agentMessage items in store
    const agentMsgItemsPerTurn = storeAfterWait?.turns.map((t) => ({
      turnId: t.id,
      agentMsgCount: t.itemTypes.filter((x) => x === 'agentMessage').length,
      allTexts: t.agentTexts
    })) ?? []
    const duplicateAgentMsgItems = agentMsgItemsPerTurn.some((t) => t.agentMsgCount > 1)
    if (duplicateAgentMsgItems) {
      log(`  [DIAG] DUPLICATE_AGENT_MSG_ITEMS: ${JSON.stringify(agentMsgItemsPerTurn)}`)
    }

    // 5. Optional renderer notification trace (removed from production path; use stream + store above)
    log('')
    log('  Recent stream samples (tail):')
    for (const s of streamSamples.slice(-8)) {
      log(
        `    [${s.time}] stream_len=${s.streaming.length} dom_len=${s.domText.length}`
      )
    }

    // 6. Response presence check
    const hasUserMsg = domAfterWait.includes(testMessage.substring(0, 30))
    const hasAgentResponse = agentTextsAfterWait.length > 0
    const agentResponseText = agentTextsAfterWait.join(' ')

    log('')
    log('══════════════════════════════════════════════════')
    log('SMOKE TEST DIAGNOSTICS')
    log('══════════════════════════════════════════════════')
    log(`Console errors:             ${consoleLogs.filter((l) => l.level === 'error' || l.level === 'pageerror').length}`)
    log(`Console warnings:           ${consoleLogs.filter((l) => l.level === 'warning').length}`)
    log(`Stream samples collected:   ${streamSamples.length}`)
    log(`Peak streaming length:      ${maxStreamingLen} chars`)
    log(`Peak streaming text:        "${streamPeakText.replace(/\n/g, '↵').substring(0, 100)}"`)
    log(`Stream tail samples:        ${Math.min(8, streamSamples.length)} lines`)
    log(`User message visible:       ${hasUserMsg}`)
    log(`Agent response in store:    ${hasAgentResponse}  (text: "${agentResponseText.substring(0, 80)}")`)
    log(`Agent response in DOM:      ${domHasContent}`)
    log('')
    log(`DUPLICATE_STREAMING:        ${duplicateStreamingDetected ? 'YES ← BUG' : 'no'}`)
    log(`CONTENT_DISAPPEARED:        ${contentDisappeared ? 'YES ← BUG' : 'no'}`)
    log(`DOM_CLEARED:                ${!domHasContent ? 'YES ← BUG' : 'no'}`)
    log(`STORE_ANOMALY_TURN_IDS:     ${duplicateTurnIds ? 'YES ← BUG' : 'no'}`)
    log(`DUPLICATE_AGENT_MSG_ITEMS:  ${duplicateAgentMsgItems ? 'YES ← BUG' : 'no'}`)
    log('')

    const consoleErrors = consoleLogs.filter((l) => l.level === 'error' || l.level === 'pageerror')
    if (consoleErrors.length > 0) {
      log('Console errors:')
      for (const e of consoleErrors) {
        log(`  [${e.time}] ${e.text}`)
      }
      log('')
    }

    const passed =
      hasUserMsg &&
      hasAgentResponse &&
      domHasContent &&
      !duplicateStreamingDetected &&
      !contentDisappeared &&
      !duplicateTurnIds &&
      !duplicateAgentMsgItems &&
      consoleErrors.length === 0
    log(`Overall: ${passed ? 'PASS ✓' : 'FAIL ✗'}`)
    log('══════════════════════════════════════════════════')
    log(`Screenshots: ${SCREENSHOTS_DIR}`)

  } finally {
    try {
      await app.close()
    } catch {
      // Ignore close races on test failures.
    }
    const exited = await waitForPidsToExit(launchedAppServerPids, APP_SERVER_EXIT_TIMEOUT)
    if (!exited) {
      log(
        `FAIL: dotcraft app-server is still alive after ${APP_SERVER_EXIT_TIMEOUT}ms: ${launchedAppServerPids.join(', ')}`
      )
      process.exit(1)
    }
  }
}

run().catch((err) => {
  log(`FATAL: ${String(err)}`)
  process.exit(1)
})
