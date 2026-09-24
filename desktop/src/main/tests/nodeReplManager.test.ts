import nodeProcess from 'node:process'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createFakeBrowserManager } from './helpers/replBrowserFixture'
import { createReplWorkerFixture } from './helpers/replWorkerFixture'
import { afterAll } from 'vitest'
import { NodeReplManager } from '../nodeReplManager'

const { mockedAppPath } = vi.hoisted(() => ({
  mockedAppPath: process.cwd()
}))

vi.mock('electron', () => ({
  app: { getAppPath: () => mockedAppPath },
  BrowserWindow: vi.fn()
}))

const workerFixture = createReplWorkerFixture()
afterAll(() => workerFixture.dispose())

describe('NodeReplManager', () => {
  const initialCwd = nodeProcess.cwd()
  const managers: NodeReplManager[] = []
  const browserManagers: Array<ReturnType<typeof createFakeBrowserManager>> = []
  const createManager = (browserManager: ReturnType<typeof createFakeBrowserManager>) => {
    const manager = new NodeReplManager(browserManager as never, workerFixture.fork)
    managers.push(manager)
    browserManagers.push(browserManager)
    return manager
  }

  afterEach(async () => {
    if (nodeProcess.cwd() !== initialCwd) nodeProcess.chdir(initialCwd)
    await Promise.all(managers.map((manager) => manager.disposeAll()))
    await Promise.all(browserManagers.map((manager) => manager.closeBackendForTests()))
    managers.length = 0
    browserManagers.length = 0
  })

  it('serializes concurrent evaluations for the same thread', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const first = manager.evaluate(owner, {
      threadId: 'thread-queue',
      evaluationId: 'eval-first',
      code: `
        await new Promise((resolve) => setTimeout(resolve, 25))
        globalThis.queueOrder = ["first"]
        "first"
      `
    })
    const second = manager.evaluate(owner, {
      threadId: 'thread-queue',
      evaluationId: 'eval-second',
      code: `
        globalThis.queueOrder.push("second")
        JSON.stringify(globalThis.queueOrder)
      `
    })

    const [firstResult, secondResult] = await Promise.all([first, second])

    expect(firstResult.error).toBeUndefined()
    expect(firstResult.resultText).toBe('first')
    expect(secondResult.error).toBeUndefined()
    expect(JSON.parse(secondResult.resultText ?? '[]')).toEqual(['first', 'second'])
    manager.reset('thread-queue')
  })

  it('cancels a queued evaluation before it starts', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const first = manager.evaluate(owner, {
      threadId: 'thread-queue-cancel',
      evaluationId: 'eval-first',
      code: `
        await new Promise((resolve) => setTimeout(resolve, 25))
        "first"
      `
    })
    const second = manager.evaluate(owner, {
      threadId: 'thread-queue-cancel',
      evaluationId: 'eval-second',
      code: '"second"'
    })
    const cancel = manager.cancel('thread-queue-cancel', 'eval-second')

    const [firstResult, secondResult] = await Promise.all([first, second])

    expect(cancel).toEqual({ ok: true })
    expect(firstResult.error).toBeUndefined()
    expect(firstResult.resultText).toBe('first')
    expect(secondResult.error).toContain('cancelled before it started')
    expect(browserManager.abortEvaluation).not.toHaveBeenCalledWith('thread-queue-cancel', 'eval-second')
    manager.reset('thread-queue-cancel')
  })

  it('returns console logs and displayed images', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        console.log("hello", 42)
        await display({ mediaType: "image/png", dataBase64: "AQID" })
        "done"
      `
    })

    expect(result.resultText).toBe('done')
    expect(result.logs).toEqual(['hello 42'])
    expect(result.images).toEqual([{ mediaType: 'image/png', dataBase64: 'AQID' }])
    manager.reset('thread-1')
  })

  it.runIf(process.platform === 'win32')('routes dotcraft.computer calls with approval and a paused deadline', async () => {
    const browserManager = createFakeBrowserManager()
    const computerUse = {
      handleHostCall: vi.fn(async (method: string, args: unknown, context: {
        turnId?: string
        pauseTimeout(): () => void
        requestApproval(app: { id: string; displayName: string }): Promise<boolean>
        emitImage(image: { mediaType: string; dataBase64: string }): Promise<void>
      }) => {
        const resume = context.pauseTimeout()
        await new Promise((resolve) => setTimeout(resolve, 1500))
        resume()
        const approved = await context.requestApproval({ id: 'C:\\Apps\\App.exe', displayName: 'App' })
        await context.emitImage({ mediaType: 'image/png', dataBase64: 'AQID' })
        return { method, args, approved, turnId: context.turnId }
      })
    }
    const manager = new NodeReplManager(browserManager as never, workerFixture.fork, computerUse as never)
    managers.push(manager)
    browserManagers.push(browserManager)
    const requestApproval = vi.fn(async () => true)

    const result = await manager.evaluate({} as Electron.BrowserWindow, {
      threadId: 'thread-computer',
      turnId: 'turn-computer',
      evaluationId: 'eval-computer',
      timeoutMs: 1000,
      requestApproval,
      code: 'JSON.stringify(await dotcraft.computer.list_windows({ probe: 1 }))'
    })

    expect(result.error).toBeUndefined()
    expect(JSON.parse(result.resultText ?? '{}')).toEqual({
      method: 'list_windows', args: { probe: 1 }, approved: true, turnId: 'turn-computer'
    })
    expect(requestApproval).toHaveBeenCalledWith({
      evaluationId: 'eval-computer',
      approvalType: 'computerUse',
      operation: 'use',
      target: 'C:\\Apps\\App.exe',
      targetLabel: 'App'
    })
    expect(result.images).toEqual([{ mediaType: 'image/png', dataBase64: 'AQID' }])
    manager.reset('thread-computer')
  })

  it('does not expose browser agent globals before browser-client setup', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-fresh-browser-client',
      code: `
        JSON.stringify({
          agentType: typeof agent,
          browserClientPath: typeof dotcraft.browserClientPath,
          browserSession: typeof dotcraft.browserSession,
          nativePipe: typeof nodeRepl.nativePipe.createConnection,
          emitImage: typeof nodeRepl.emitImage
        })
      `
    })

    expect(result.error).toBeUndefined()
    const payload = JSON.parse(result.resultText ?? '{}')
    expect(payload).toEqual({
      agentType: 'undefined',
      browserClientPath: 'string',
      browserSession: 'object',
      nativePipe: 'function',
      emitImage: 'function'
    })
    manager.reset('thread-fresh-browser-client')
  })

  it('exposes DotCraft paths and native Node process capabilities', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      workspacePath: nodeProcess.cwd(),
      code: `JSON.stringify({
        workspacePath: dotcraft.workspacePath,
        chromePluginRoot: dotcraft.chromePluginRoot,
        chromeScriptsPath: dotcraft.chromeScriptsPath,
        hasCheckSetup: typeof dotcraft.chrome.checkSetup,
        requireType: typeof require,
        processType: typeof process
      })`
    })

    expect(result.error).toBeUndefined()
    const payload = JSON.parse(result.resultText ?? '{}')
    expect(payload.workspacePath).toBe(nodeProcess.cwd())
    expect(payload.chromePluginRoot).toContain('chrome')
    expect(payload.chromeScriptsPath).toContain('scripts')
    expect(payload.hasCheckSetup).toBe('function')
    expect(payload.requireType).toBe('function')
    expect(payload.processType).toBe('object')
    manager.reset('thread-1')
  })

  it('exposes browser-use compatible nodeRepl host fields', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `JSON.stringify({
        availableBackends: nodeRepl.env.BROWSER_USE_AVAILABLE_BACKENDS,
        ambientNetworkDisabled: nodeRepl.env.BROWSER_USE_DISABLE_AMBIENT_NETWORK,
        securityMode: nodeRepl.env.BROWSER_USE_SECURITY_MODE,
        createConnection: typeof nodeRepl.nativePipe.createConnection,
        createElicitation: typeof nodeRepl.createElicitation,
        fetch: typeof nodeRepl.fetch,
        requestMeta: nodeRepl.requestMeta["x-dotcraft-turn-metadata"].session_id,
        requestMetaKeys: Object.keys(nodeRepl.requestMeta),
        tmpDir: typeof nodeRepl.tmpDir
      })`
    })

    expect(result.error).toBeUndefined()
    expect(JSON.parse(result.resultText ?? '{}')).toEqual({
      availableBackends: 'iab',
      ambientNetworkDisabled: '1',
      securityMode: 'disabled-for-local-testing',
      createConnection: 'function',
      createElicitation: 'function',
      fetch: 'function',
      requestMeta: 'thread-1',
      requestMetaKeys: ['x-dotcraft-turn-metadata'],
      tmpDir: 'string'
    })
    manager.reset('thread-1')
  })

  it('routes Browser Use file-transfer elicitation through the browser manager', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `JSON.stringify(await nodeRepl.createElicitation({
        message: "Allow download?",
        meta: { file_transfer: "download", origin: "http://localhost:5173" }
      }))`
    })

    expect(result.error).toBeUndefined()
    expect(JSON.parse(result.resultText ?? '{}')).toMatchObject({ action: 'accept' })
    expect(browserManager.handleBrowserUseElicitation).toHaveBeenCalledWith('thread-1', expect.objectContaining({
      meta: expect.objectContaining({ file_transfer: 'download' })
    }))
    manager.reset('thread-1')
  })

  it('rejects native pipe connections outside the DotCraft browser-use namespace', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        let failure
        try {
          await nodeRepl.nativePipe.createConnection("not-a-dotcraft-browser-use-pipe")
          "connected"
        } catch (error) {
          failure = error.message
        }
        failure
      `
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toContain('Refusing to connect to a non-DotCraft browser-use native pipe.')
    manager.reset('thread-1')
  })

  it('resets the REPL and browser runtime for a thread', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    await manager.evaluate(owner, { threadId: 'thread-1', code: 'globalThis.count = 1' })
    const reset = manager.reset('thread-1')
    const result = await manager.evaluate(owner, { threadId: 'thread-1', code: 'typeof globalThis.count' })

    expect(reset.ok).toBe(true)
    expect(browserManager.reset).toHaveBeenCalledWith('thread-1')
    expect(result.resultText).toBe('undefined')
    manager.reset('thread-1')
  })

  it('returns JavaScript runtime errors instead of waiting for tool timeout', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const thrown = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'throw new Error("boom")',
      timeoutMs: 5_000
    })
    const rejected = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'await Promise.reject(new Error("nope"))',
      timeoutMs: 5_000
    })
    const typeError = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'await globalThis.missing.url()',
      timeoutMs: 5_000
    })

    expect(thrown.error).toContain('Error: boom')
    expect(rejected.error).toContain('Error: nope')
    expect(typeError.error).toContain('TypeError')
    manager.reset('thread-1')
  })

  it('passes evaluation id and abort signal into the browser runtime', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      evaluationId: 'eval-1',
      code: '1 + 1'
    })

    expect(result.error).toBeUndefined()
    expect(browserManager.prepareNodeRepl).toHaveBeenCalledWith(owner, expect.objectContaining({
      threadId: 'thread-1',
      evaluationId: 'eval-1',
      browserSession: expect.objectContaining({
        protocolVersion: 1,
        sessionId: 'thread-1',
        threadId: 'thread-1',
        turnId: 'eval-1',
        evaluationId: 'eval-1'
      }),
      signal: expect.any(AbortSignal)
    }))
    manager.reset('thread-1')
  })

  it('injects browser session metadata into dotcraft globals', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const first = await manager.evaluate(owner, {
      threadId: 'thread-1',
      turnId: 'turn-1',
      evaluationId: 'eval-1',
      code: 'JSON.stringify(dotcraft.browserSession)'
    })
    const second = await manager.evaluate(owner, {
      threadId: 'thread-1',
      turnId: 'turn-1',
      evaluationId: 'eval-2',
      code: 'JSON.stringify(dotcraft.browserSession)'
    })

    expect(JSON.parse(first.resultText ?? '{}')).toMatchObject({
      protocolVersion: 1,
      sessionId: 'thread-1',
      threadId: 'thread-1',
      turnId: 'turn-1',
      evaluationId: 'eval-1'
    })
    expect(JSON.parse(second.resultText ?? '{}')).toMatchObject({
      sessionId: 'thread-1',
      turnId: 'turn-1',
      evaluationId: 'eval-2'
    })
    manager.reset('thread-1')
  })

  it('resets the REPL runtime after timeout so the next evaluation is fresh', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    await manager.evaluate(owner, { threadId: 'thread-1', code: 'globalThis.count = 1' })
    const pending = manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
        const agent = await setupBrowserRuntime({ backend: "iab" })
        await new Promise(() => {})
      `,
      timeoutMs: 1
    })
    const timedOut = await pending

    expect(timedOut.error).toContain('timed out')
    expect(timedOut.logs.join('\n')).toContain('Recent browser operations')
    expect(browserManager.abortEvaluation).toHaveBeenCalledWith('thread-1', expect.stringMatching(/^node-repl-/))
    const result = await manager.evaluate(owner, { threadId: 'thread-1', code: 'typeof globalThis.count' })
    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('undefined')
    manager.reset('thread-1')
  })

  it('calls the Chrome cancel hook before resetting after an outer timeout', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const timedOut = await manager.evaluate(owner, {
      threadId: 'thread-1',
      evaluationId: 'eval-timeout',
      code: `
        __dotcraftSetChromeCancelHook(async (evaluationId, reason) => {
          console.warn("chrome-cancel", evaluationId, reason.includes("timed out"))
        })
        const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
        const agent = await setupBrowserRuntime({ backend: "iab" })
        await new Promise(() => {})
      `,
      timeoutMs: 1
    })

    expect(timedOut.error).toContain('timed out')
    expect(browserManager.abortEvaluation).toHaveBeenCalledWith('thread-1', 'eval-timeout')
    const next = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'typeof globalThis.count'
    })
    expect(next.resultText).toBe('undefined')
    manager.reset('thread-1')
  })

  it('keeps REPL globals after a browser command timeout', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    await manager.evaluate(owner, { threadId: 'thread-1', code: 'globalThis.count = 1' })
    const failed = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
        const agent = await setupBrowserRuntime({ backend: "iab" })
        const browser = await agent.browsers.get("iab")
        const tab = await browser.tabs.new()
        await tab.playwright.evaluate(() => window.__dotcraftChromeCommandTimeoutSentinel, undefined, { timeoutMs: 1000 })
      `,
      timeoutMs: 5_000
    })
    const result = await manager.evaluate(owner, { threadId: 'thread-1', code: 'globalThis.count' })

    expect(failed.error).toContain('Chrome bridge request timed out: tab.evaluate')
    expect(browserManager.abortEvaluation).not.toHaveBeenCalled()
    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('1')
    manager.reset('thread-1')
  })

  it('cancels an active evaluation and allows a later evaluation to run', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const pending = manager.evaluate(owner, {
      threadId: 'thread-1',
      evaluationId: 'eval-1',
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
        const agent = await setupBrowserRuntime({ backend: "iab" })
        await new Promise(() => {})
      `,
      timeoutMs: 120_000
    })
    await new Promise((resolve) => setTimeout(resolve, 0))
    const cancel = manager.cancel('thread-1', 'eval-1')
    const cancelled = await pending

    expect(cancel).toEqual({ ok: true })
    expect(cancelled.error).toContain('cancelled')
    expect(browserManager.abortEvaluation).toHaveBeenCalledWith('thread-1', 'eval-1')
    const result = await manager.evaluate(owner, { threadId: 'thread-1', code: '1 + 1' })
    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('2')
    manager.reset('thread-1')
  })

})
