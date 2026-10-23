import nodeProcess from 'node:process'
import { afterAll, afterEach, describe, expect, it, vi } from 'vitest'
import { createFakeBrowserManager } from './helpers/replBrowserFixture'
import { createReplWorkerFixture } from './helpers/replWorkerFixture'
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

const browserSetup = `
  const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
  const agent = await setupBrowserRuntime({ backend: "iab" })
`

describe('NodeReplManager', () => {
  const initialCwd = nodeProcess.cwd()
  const owner = {} as Electron.BrowserWindow
  const managers: NodeReplManager[] = []
  const browserManagers: Array<ReturnType<typeof createFakeBrowserManager>> = []
  const createManager = (browserManager = createFakeBrowserManager(), computerUse?: unknown) => {
    const manager = new NodeReplManager(browserManager as never, workerFixture.fork, computerUse as never)
    managers.push(manager)
    browserManagers.push(browserManager)
    return { manager, browserManager }
  }

  afterEach(async () => {
    if (nodeProcess.cwd() !== initialCwd) nodeProcess.chdir(initialCwd)
    await Promise.all(managers.map((manager) => manager.disposeAll()))
    await Promise.all(browserManagers.map((manager) => manager.closeBackendForTests()))
    managers.length = 0
    browserManagers.length = 0
  })

  it('serializes evaluations for the same thread and cancels queued ones before they start', async () => {
    const { manager, browserManager } = createManager()

    const first = manager.evaluate(owner, {
      threadId: 'thread-queue',
      evaluationId: 'eval-first',
      code: `
        await new Promise((resolve) => setTimeout(resolve, 25))
        globalThis.queueOrder = ["first"]
        "first"
      `
    })
    const cancelled = manager.evaluate(owner, { threadId: 'thread-queue', evaluationId: 'eval-cancelled', code: '"cancelled"' })
    const second = manager.evaluate(owner, {
      threadId: 'thread-queue',
      evaluationId: 'eval-second',
      code: `
        globalThis.queueOrder.push("second")
        JSON.stringify(globalThis.queueOrder)
      `
    })
    const cancel = manager.cancel('thread-queue', 'eval-cancelled')

    const [firstResult, cancelledResult, secondResult] = await Promise.all([first, cancelled, second])

    expect(cancel).toEqual({ ok: true })
    expect(firstResult.resultText).toBe('first')
    expect(cancelledResult.error).toContain('cancelled before it started')
    expect(secondResult.error).toBeUndefined()
    expect(JSON.parse(secondResult.resultText ?? '[]')).toEqual(['first', 'second'])
    expect(browserManager.abortEvaluation).not.toHaveBeenCalledWith('thread-queue', 'eval-cancelled')
  })

  it.runIf(process.platform === 'win32')('routes dotcraft.computer calls with approval and a paused deadline', async () => {
    const computerUse = {
      handleHostCall: vi.fn(async (method: string, args: unknown, context: {
        turnId?: string
        pauseTimeout(): () => void
        requestApproval(app: { id: string; displayName: string }): Promise<boolean>
        emitImage(image: { mediaType: string; dataBase64: string }): Promise<void>
      }) => {
        const resume = context.pauseTimeout()
        await new Promise((resolve) => setTimeout(resolve, 1100))
        resume()
        const approved = await context.requestApproval({ id: 'C:\\Apps\\App.exe', displayName: 'App' })
        await context.emitImage({ mediaType: 'image/png', dataBase64: 'AQID' })
        return { method, args, approved, turnId: context.turnId }
      })
    }
    const { manager } = createManager(undefined, computerUse)
    const requestApproval = vi.fn(async () => true)

    const result = await manager.evaluate(owner, {
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
  })

  it('exposes DotCraft host globals, logs, images, and per-evaluation browser session metadata', async () => {
    const { manager, browserManager } = createManager()

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      turnId: 'turn-1',
      evaluationId: 'eval-1',
      workspacePath: nodeProcess.cwd(),
      code: `
        console.log("hello", 42)
        await display({ mediaType: "image/png", dataBase64: "AQID" })
        JSON.stringify({
          agentType: typeof agent,
          browserClientPath: typeof dotcraft.browserClientPath,
          browserSession: dotcraft.browserSession,
          workspacePath: dotcraft.workspacePath,
          chromePluginRoot: dotcraft.chromePluginRoot,
          chromeScriptsPath: dotcraft.chromeScriptsPath,
          hasCheckSetup: typeof dotcraft.chrome.checkSetup,
          requireType: typeof require,
          processType: typeof process,
          availableBackends: nodeRepl.env.BROWSER_USE_AVAILABLE_BACKENDS,
          ambientNetworkDisabled: nodeRepl.env.BROWSER_USE_DISABLE_AMBIENT_NETWORK,
          securityMode: nodeRepl.env.BROWSER_USE_SECURITY_MODE,
          createConnection: typeof nodeRepl.nativePipe.createConnection,
          createElicitation: typeof nodeRepl.createElicitation,
          emitImage: typeof nodeRepl.emitImage,
          fetch: typeof nodeRepl.fetch,
          requestMeta: nodeRepl.requestMeta,
          tmpDir: typeof nodeRepl.tmpDir
        })
      `
    })
    const second = await manager.evaluate(owner, {
      threadId: 'thread-1',
      turnId: 'turn-1',
      evaluationId: 'eval-2',
      code: 'JSON.stringify(dotcraft.browserSession)'
    })

    expect(result.error).toBeUndefined()
    expect(result.logs).toEqual(['hello 42'])
    expect(result.images).toEqual([{ mediaType: 'image/png', dataBase64: 'AQID' }])
    const payload = JSON.parse(result.resultText ?? '{}')
    expect(payload).toMatchObject({
      agentType: 'undefined',
      browserClientPath: 'string',
      browserSession: { protocolVersion: 1, sessionId: 'thread-1', threadId: 'thread-1', turnId: 'turn-1', evaluationId: 'eval-1' },
      workspacePath: nodeProcess.cwd(),
      hasCheckSetup: 'function',
      requireType: 'function',
      processType: 'object',
      availableBackends: 'iab',
      ambientNetworkDisabled: '1',
      securityMode: 'disabled-for-local-testing',
      createConnection: 'function',
      createElicitation: 'function',
      emitImage: 'function',
      fetch: 'function',
      tmpDir: 'string'
    })
    expect(payload.chromePluginRoot).toContain('chrome')
    expect(payload.chromeScriptsPath).toContain('scripts')
    expect(Object.keys(payload.requestMeta)).toEqual(['x-dotcraft-turn-metadata'])
    expect(payload.requestMeta['x-dotcraft-turn-metadata'].session_id).toBe('thread-1')
    expect(JSON.parse(second.resultText ?? '{}')).toMatchObject({ sessionId: 'thread-1', turnId: 'turn-1', evaluationId: 'eval-2' })
    expect(browserManager.prepareNodeRepl).toHaveBeenCalledWith(owner, expect.objectContaining({
      threadId: 'thread-1',
      evaluationId: 'eval-1',
      browserSession: expect.objectContaining({ sessionId: 'thread-1', evaluationId: 'eval-1' }),
      signal: expect.any(AbortSignal)
    }))
  })

  it('routes file-transfer elicitation through the browser manager and refuses foreign native pipes', async () => {
    const { manager, browserManager } = createManager()

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        let pipeFailure
        try {
          await nodeRepl.nativePipe.createConnection("not-a-dotcraft-browser-use-pipe")
        } catch (error) {
          pipeFailure = error.message
        }
        JSON.stringify({
          elicitation: await nodeRepl.createElicitation({
            message: "Allow download?",
            meta: { file_transfer: "download", origin: "http://localhost:5173" }
          }),
          pipeFailure
        })
      `
    })

    expect(result.error).toBeUndefined()
    const payload = JSON.parse(result.resultText ?? '{}')
    expect(payload.elicitation).toMatchObject({ action: 'accept' })
    expect(payload.pipeFailure).toContain('Refusing to connect to a non-DotCraft browser-use native pipe.')
    expect(browserManager.handleBrowserUseElicitation).toHaveBeenCalledWith('thread-1', expect.objectContaining({
      meta: expect.objectContaining({ file_transfer: 'download' })
    }))
  })

  it('resets the REPL and browser runtime for a thread', async () => {
    const { manager, browserManager } = createManager()

    await manager.evaluate(owner, { threadId: 'thread-1', code: 'globalThis.count = 1' })
    const reset = manager.reset('thread-1')
    const result = await manager.evaluate(owner, { threadId: 'thread-1', code: 'typeof globalThis.count' })

    expect(reset.ok).toBe(true)
    expect(browserManager.reset).toHaveBeenCalledWith('thread-1')
    expect(result.resultText).toBe('undefined')
  })

  it('returns JavaScript runtime errors instead of waiting for tool timeout', async () => {
    const { manager } = createManager()
    const evaluate = (code: string) => manager.evaluate(owner, { threadId: 'thread-1', code, timeoutMs: 5_000 })

    expect((await evaluate('throw new Error("boom")')).error).toContain('Error: boom')
    expect((await evaluate('await Promise.reject(new Error("nope"))')).error).toContain('Error: nope')
    expect((await evaluate('await globalThis.missing.url()')).error).toContain('TypeError')
  })

  it('calls the Chrome cancel hook and resets the runtime after an outer timeout', async () => {
    const { manager, browserManager } = createManager()

    await manager.evaluate(owner, { threadId: 'thread-1', code: 'globalThis.count = 1' })
    const timedOut = await manager.evaluate(owner, {
      threadId: 'thread-1',
      evaluationId: 'eval-timeout',
      code: `
        __dotcraftSetChromeCancelHook(async (evaluationId, reason) => {
          console.warn("chrome-cancel", evaluationId, reason.includes("timed out"))
        })
        ${browserSetup}
        await new Promise(() => {})
      `,
      timeoutMs: 1
    })

    expect(timedOut.error).toContain('timed out')
    expect(timedOut.logs.join('\n')).toContain('Recent browser operations')
    expect(browserManager.abortEvaluation).toHaveBeenCalledWith('thread-1', 'eval-timeout')
    const next = await manager.evaluate(owner, { threadId: 'thread-1', code: 'typeof globalThis.count' })
    expect(next.error).toBeUndefined()
    expect(next.resultText).toBe('undefined')
  })

  it('keeps REPL globals after a browser command timeout', async () => {
    const { manager, browserManager } = createManager()

    await manager.evaluate(owner, { threadId: 'thread-1', code: 'globalThis.count = 1' })
    const failed = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        ${browserSetup}
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
  })

  it('cancels an active evaluation and allows a later evaluation to run', async () => {
    const { manager, browserManager } = createManager()

    const pending = manager.evaluate(owner, {
      threadId: 'thread-1',
      evaluationId: 'eval-1',
      code: `
        ${browserSetup}
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
  })
})
