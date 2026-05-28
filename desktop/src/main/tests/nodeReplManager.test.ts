import { afterEach, describe, expect, it, vi } from 'vitest'
import { NodeReplManager } from '../nodeReplManager'

vi.mock('electron', () => ({
  app: { getAppPath: () => process.cwd() },
  BrowserWindow: vi.fn()
}))

function createFakeBrowserManager() {
  const images: Array<{ mediaType: string; dataBase64: string }> = []
  const logs: string[] = []
  const pendingActions: Array<() => void> = []
  const browser = {
    nameSession: vi.fn(async (name: string) => ({ ok: true, name })),
    tabs: {
      describeApi: () => ['selected()', 'new(url?)', 'finalize({ keep })']
    },
    describeApi: () => ['nameSession(name)', 'tabs.finalize({ keep })']
  }
  return {
    prepareNodeRepl: vi.fn(() => ({
      agent: {
        hang: vi.fn(() => new Promise((resolve) => {
          pendingActions.push(() => resolve('late'))
        })),
        chromeCommandTimeout: vi.fn(async () => {
          throw new Error('Chrome bridge request timed out: tab.evaluate')
        }),
        browser,
        browsers: {
          list: vi.fn(async () => [{ id: 'iab', name: 'DotCraft Browser', type: 'iab' }]),
          get: vi.fn(async (id: string) => {
            if (id === 'iab') return browser
            throw new Error(`Browser not found: ${id}`)
          }),
          describeApi: () => ['list()', 'get("iab")']
        }
      },
      display: vi.fn(async (imageLike: { mediaType?: string; dataBase64?: string }) => {
        images.push({
          mediaType: imageLike.mediaType ?? 'image/png',
          dataBase64: imageLike.dataBase64 ?? ''
        })
      }),
      collect: () => ({ images: [...images], logs: [...logs] })
    })),
    abortEvaluation: vi.fn(() => {
      logs.push('Browser evaluation aborted.\nRecent browser operations:\ncua.click status=active tab=tab-1 url=http://127.0.0.1:5173/ elapsedMs=1000 timeoutMs=10000')
      return { ok: true }
    }),
    reset: vi.fn(() => ({ ok: true })),
    releasePending: () => {
      while (pendingActions.length) pendingActions.shift()?.()
    }
  }
}

describe('NodeReplManager', () => {
  const managers: NodeReplManager[] = []
  const createManager = (browserManager: ReturnType<typeof createFakeBrowserManager>) => {
    const manager = new NodeReplManager(browserManager as never)
    managers.push(manager)
    return manager
  }

  afterEach(async () => {
    await Promise.all(managers.map((manager) => manager.disposeAllForTests()))
    managers.length = 0
  })

  it('persists explicit globalThis variables across evaluations', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    await manager.evaluate(owner, { threadId: 'thread-1', code: 'globalThis.count = 1' })
    const result = await manager.evaluate(owner, { threadId: 'thread-1', code: 'globalThis.count += 1' })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('2')
    manager.reset('thread-1')
  })

  it('keeps cell-local const declarations from poisoning later evaluations', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const first = await manager.evaluate(owner, { threadId: 'thread-1', code: 'const snapshot = 1; return snapshot' })
    const second = await manager.evaluate(owner, { threadId: 'thread-1', code: 'const snapshot = 2; return snapshot' })

    expect(first.error).toBeUndefined()
    expect(first.resultText).toBe('1')
    expect(second.error).toBeUndefined()
    expect(second.resultText).toBe('2')
    manager.reset('thread-1')
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
        return "done"
      `
    })

    expect(result.resultText).toBe('done')
    expect(result.logs).toEqual(['hello 42'])
    expect(result.images).toEqual([{ mediaType: 'image/png', dataBase64: 'AQID' }])
    manager.reset('thread-1')
  })

  it('loads browser-client.mjs and initializes IAB globals', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
        const initialized = await setupBrowserRuntime({ globals: globalThis, backend: "iab" })
        await agent.browser.nameSession("docs")
        return initialized
      `
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toContain('"backend": "iab"')
    manager.reset('thread-1')
  })

  it('supports browser bootstrap and nodeRepl.emitImage', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
        await setupBrowserRuntime({ globals: globalThis })
        await nodeRepl.emitImage({ mediaType: "image/png", dataBase64: "BAUG" })
        return typeof agent.browsers.get
      `
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('function')
    expect(result.images).toEqual([{ mediaType: 'image/png', dataBase64: 'BAUG' }])
    manager.reset('thread-1')
  })

  it('exposes browser agent.browsers and the compatibility agent.browser alias', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
        await setupBrowserRuntime({ globals: globalThis, backend: "iab" })
        const browser = await agent.browsers.get("iab")
        return JSON.stringify({
          list: await agent.browsers.list(),
          sameAlias: browser.describeApi().join(",") === agent.browser.describeApi().join(","),
          tabsApi: browser.tabs.describeApi()
        })
      `
    })

    expect(result.error).toBeUndefined()
    const payload = JSON.parse(result.resultText ?? '{}')
    expect(payload.list).toEqual([{ id: 'iab', name: 'DotCraft Browser', type: 'iab' }])
    expect(payload.sameAlias).toBe(true)
    expect(payload.tabsApi).toContain('finalize({ keep })')
    manager.reset('thread-1')
  })

  it('loads chrome browser-client.mjs and can delegate non-extension backends to IAB globals', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.chromeBrowserClientPath)
        return await setupBrowserRuntime({ globals: globalThis, backend: "iab" })
      `
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toContain('"backend": "iab"')
    manager.reset('thread-1')
  })

  it('registers the Chrome extension backend when IAB globals already exist', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        let chromeBackendReady = false
        try {
          chromeBackendReady = (await agent.browsers.list()).some((item) => item?.id === "extension")
        } catch {
          chromeBackendReady = false
        }
        if (!chromeBackendReady) {
          const { setupBrowserRuntime } = await import(dotcraft.chromeBrowserClientPath)
          await setupBrowserRuntime({ globals: globalThis, backend: "extension" })
        }
        return JSON.stringify(await agent.browsers.list())
      `
    })

    expect(result.error).toBeUndefined()
    expect(JSON.parse(result.resultText ?? '[]')).toEqual([
      { id: 'iab', name: 'DotCraft Browser', type: 'iab' },
      { id: 'extension', name: 'DotCraft Chrome', type: 'extension' }
    ])
    manager.reset('thread-1')
  })

  it('exposes safe DotCraft Chrome paths without exposing process or require', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      workspacePath: process.cwd(),
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
    expect(payload.workspacePath).toBe(process.cwd())
    expect(payload.chromePluginRoot).toContain('chrome')
    expect(payload.chromeScriptsPath).toContain('scripts')
    expect(payload.hasCheckSetup).toBe('function')
    expect(payload.requireType).toBe('undefined')
    expect(payload.processType).toBe('undefined')
    manager.reset('thread-1')
  })

  it('provides URL in the REPL VM context', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'new URL("http://127.0.0.1:5173/docs?q=1").hostname'
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('127.0.0.1')
    manager.reset('thread-1')
  })

  it('disallows string code generation in the REPL VM context', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const evalResult = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'eval("1")',
      timeoutMs: 5_000
    })
    const functionResult = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'new Function("return 1")()',
      timeoutMs: 5_000
    })

    expect(evalResult.error).toContain('EvalError: Code generation from strings disallowed')
    expect(functionResult.error).toContain('EvalError: Code generation from strings disallowed')
    manager.reset('thread-1')
  })

  it('disallows WebAssembly code generation in the REPL VM context', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'await WebAssembly.compile(new Uint8Array([0,97,115,109,1,0,0,0]))',
      timeoutMs: 5_000
    })

    expect(result.error).toContain('CompileError')
    expect(result.error).toContain('Wasm code generation disallowed')
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
      code: 'await agent.hang()',
      timeoutMs: 1
    })
    const timedOut = await pending
    browserManager.releasePending()

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
        await agent.hang()
      `,
      timeoutMs: 1
    })
    browserManager.releasePending()

    expect(timedOut.error).toContain('timed out')
    expect(timedOut.logs.join('\n')).toContain('chrome-cancel eval-timeout true')
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
      code: 'await agent.chromeCommandTimeout()',
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
      code: 'await agent.hang()',
      timeoutMs: 120_000
    })
    const cancel = manager.cancel('thread-1', 'eval-1')
    const cancelled = await pending
    browserManager.releasePending()

    expect(cancel).toEqual({ ok: true })
    expect(cancelled.error).toContain('cancelled')
    expect(browserManager.abortEvaluation).toHaveBeenCalledWith('thread-1', 'eval-1')
    const result = await manager.evaluate(owner, { threadId: 'thread-1', code: '1 + 1' })
    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('2')
    manager.reset('thread-1')
  })

})
