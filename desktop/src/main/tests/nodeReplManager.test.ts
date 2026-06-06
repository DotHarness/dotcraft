import nodeProcess from 'node:process'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { BrowserUseBackendServer, BrowserUseBackendError } from '../browserUseBackendServer'
import { NodeReplManager } from '../nodeReplManager'

const { mockedAppPath } = vi.hoisted(() => ({
  mockedAppPath: process.cwd()
}))

vi.mock('electron', () => ({
  app: { getAppPath: () => mockedAppPath },
  BrowserWindow: vi.fn()
}))

function createFakeBrowserManager(options: {
  staleDomNodesAfterNavigation?: boolean
  webMcpAvailable?: boolean
  navigationFailure?: { errorDescription: string; validatedURL: string; finalURL: string; errorCode?: number }
} = {}) {
  const images: Array<{ mediaType: string; dataBase64: string }> = []
  const logs: string[] = []
  const cdpCommands: Array<{ method: string; target: unknown; commandParams: unknown }> = []
  const createTabRequests: Array<Record<string, unknown>> = []
  const unhandledCommands: Array<Record<string, unknown>> = []
  const moveMouseCalls: Array<Record<string, unknown>> = []
  const pendingActions: Array<() => void> = []
  const staleDomNodeIds = new Set<number>()
  let nextTabId = 1
  let checkboxChecked = false
  let checkboxToggleArmed = false
  let browserVisible = false
  let viewport = { width: 1280, height: 720 }
  let clipboardText = ''
  let clipboardItems: Array<{
    entries: Array<{ mime_type: string; text?: string; base64?: string }>
    presentation_style?: 'unspecified' | 'inline' | 'attachment'
  }> = []
  const backendTabs: Array<{ id: number; url: string; title: string; loading: boolean; active?: boolean }> = []
  const publicTab = (tab: { id: number; url: string; title: string; loading: boolean; active?: boolean }) => ({
    ...tab,
    id: tab.id,
    tabId: tab.id
  })
  const tabForBackendId = (id: unknown) => backendTabs.find((tab) => tab.id === Number(id))
  let backendServer: BrowserUseBackendServer
  const emitCdpEvent = (tabId: number, method: string, params: Record<string, unknown>) => {
    backendServer.sendNotification('onCDPEvent', {
      source: { tabId },
      method,
      params
    })
  }
  const emitNavigationEvents = (tab: { id: number; url: string }) => {
    emitCdpEvent(tab.id, 'Page.frameNavigated', { frame: { id: 'main-frame', url: tab.url } })
    emitCdpEvent(tab.id, 'Page.domContentEventFired', { timestamp: Date.now() / 1000 })
    emitCdpEvent(tab.id, 'Page.loadEventFired', { timestamp: Date.now() / 1000 })
  }
  const layoutMetrics = () => ({
    cssContentSize: { x: 0, y: 0, width: 1280, height: 720 },
    cssVisualViewport: { pageX: 0, pageY: 0, clientWidth: 1280, clientHeight: 720 },
    contentSize: { x: 0, y: 0, width: 1280, height: 720 }
  })
  const domRoot = () => ({
    root: {
      nodeId: 1,
      backendNodeId: 1,
      nodeType: 1,
      nodeName: 'HTML',
      localName: 'html',
      attributes: [],
      children: [{
        nodeId: 2,
        backendNodeId: 2,
        nodeType: 1,
        nodeName: 'BODY',
        localName: 'body',
        attributes: [],
        children: [{
          nodeId: 3,
          backendNodeId: 42,
          nodeType: 1,
          nodeName: 'BUTTON',
          localName: 'button',
          attributes: ['role', 'button', 'data-testid', 'save'],
          children: [{ nodeId: 4, nodeType: 3, nodeName: '#text', nodeValue: 'Save' }]
        }]
      }]
    }
  })
  const resourceSnapshot = (url: string) => ({
    documents: [{
      documentURL: 0,
      nodes: {
        backendNodeId: [11],
        nodeName: [1],
        attributes: [[2, 3, 4, 5]]
      },
      layout: {
        nodeIndex: [],
        styles: []
      }
    }],
    strings: [url, 'LINK', 'rel', 'stylesheet', 'href', `${url.replace(/\/$/, '')}/site.css`]
  })
  const clipboardPlainText = () => clipboardItems
    .flatMap((item) => item.entries)
    .find((entry) => entry.mime_type === 'text/plain')?.text ?? clipboardText
  const clientLocatorMatches = (expression: string): Array<Record<string, unknown>> => {
    const save = {
      index: 0,
      tagName: 'button',
      tag: 'button',
      role: 'button',
      name: 'Save',
      text: 'Save',
      selector: 'button[data-testid="save"]',
      testId: 'save',
      visible: true,
      enabled: true,
      visibleText: 'Save',
      ariaName: 'Save',
      attributes: { 'data-testid': 'save' },
      boundingBox: { x: 10, y: 20, width: 100, height: 40 }
    }
    const cancel = {
      ...save,
      index: 1,
      name: 'Cancel',
      text: 'Cancel',
      selector: 'button:nth-of-type(2)',
      testId: undefined,
      visibleText: 'Cancel',
      ariaName: 'Cancel',
      attributes: {}
    }
    if (expression.includes('"kind":"and"')) return [save]
    if (expression.includes('"kind":"or"')) return [save, cancel]
    if (expression.includes('"filters"') && expression.includes('"kind":"hasText"') && expression.includes('"Save"')) return [save]
    if (expression.includes('"filters"') && expression.includes('"kind":"hasNotText"') && expression.includes('Cancel')) return [save]
    if (expression.includes('"filters"') && expression.includes('"kind":"visible"') && expression.includes('"value":true')) return [save, cancel]
    if (expression.includes('"filters"') && expression.includes('"kind":"has"')) return [save]
    if (expression.includes('"filters"') && expression.includes('"kind":"hasNot"')) return [cancel]
    if (expression.includes('"frameSelectors"') && expression.includes('"iframe"')) return [save]
    if (expression.includes('"value":"button"') && expression.includes('"kind":"css"')) return [save, cancel]
    if (expression.includes('"kind":"text"') && expression.includes('"value":"Cancel"')) return [cancel]
    if (expression.includes('"kind":"role"') && expression.includes('"name":"Save"')) return [save]
    if (expression.includes('"kind":"placeholder"')) return [{
      ...save,
      tagName: 'input',
      tag: 'input',
      role: 'textbox',
      name: 'Email',
      text: '',
      selector: 'input[placeholder="Email"]',
      attributes: { placeholder: 'Email' }
    }]
    if (expression.includes('"kind":"testId"')) return [save]
    if (expression.includes('"kind":"label"') && expression.includes('"value":"Accept"')) return [{
      ...save,
      tagName: 'input',
      tag: 'input',
      role: 'checkbox',
      name: 'Accept',
      text: '',
      selector: 'input[name="accept"]',
      attributes: { name: 'accept', type: 'checkbox' }
    }]
    if (expression.includes('"kind":"label"')) return [{
      ...save,
      tagName: 'input',
      tag: 'input',
      role: 'textbox',
      name: 'Name',
      text: '',
      selector: 'input[name="name"]',
      attributes: { name: 'name' }
    }]
    return [save]
  }
  const evaluateExpression = (expression: string, tab: { url: string; title: string }) => {
    if (expression.includes('__dotcraftChromeCommandTimeoutSentinel')) {
      throw new Error('Chrome bridge request timed out: tab.evaluate')
    }
    if (expression.includes('fn(arg)') && expression.includes('=> value + 1') && expression.includes(', 41')) return 42
    if (expression.includes('__dotcraftBrowserUseClientLocator')) {
      if (expression.includes('"resolve"')) return clientLocatorMatches(expression)
      if (expression.includes('"getAttribute"')) return 'save'
      if (expression.includes('"textContent"') || expression.includes('"innerText"')) {
        if (expression.includes('"kind":"hasNot"')) return 'Cancel'
        return expression.includes('"index":1') ? 'Cancel' : 'Save'
      }
      if (expression.includes('"isEnabled"')) return true
      if (
        expression.includes('"fill"') ||
        expression.includes('"setChecked"') ||
        expression.includes('"selectOption"')
      ) {
        return true
      }
    }
    if (expression.includes('bodyText') && expression.includes('window.location.href') && expression.includes('document.title')) {
      return { title: tab.title, url: tab.url, bodyText: 'Save Cancel' }
    }
    if (expression.includes('window.location.href') && expression.includes('document.readyState')) {
      return { href: tab.url, readyState: 'complete' }
    }
    if (expression.includes('document.querySelectorAll("svg")')) {
      return [{ markup: '<svg aria-label="Logo"></svg>', name: 'Logo' }]
    }
    if (expression.includes('performance.getEntriesByType("resource")')) {
      return [{ initiatorType: 'css', name: `${tab.url.replace(/\/$/, '')}/site.css` }]
    }
    if (expression.includes('__dotcraftWebMcpAvailabilityProbe')) return options.webMcpAvailable === true
    if (expression.includes('navigator.modelContext') && expression.includes('modelContext.executeTool(tool')) {
      if (options.webMcpAvailable !== true) throw new Error('Capability is not available: webmcp')
      return { tool: 'summarize', ok: true, input: { topic: 'iab' } }
    }
    if (expression.includes('navigator.modelContext') && expression.includes('modelContext.getTools')) {
      if (options.webMcpAvailable !== true) throw new Error('Capability is not available: webmcp')
      return [{
        name: 'summarize',
        title: 'Summarize',
        description: 'Summarize the current page.',
        input_schema: JSON.stringify({ type: 'object', properties: { topic: { type: 'string' } } }),
        annotations: { readOnlyHint: true },
        origin: 'http://localhost:3000',
        pageUrl: tab.url
      }]
    }
    if (expression.includes('incrementalAriaSnapshot')) return '- button "Save"'
    if (expression.includes('document.title') || expression.includes('window.document.title')) return tab.title
    if (expression.includes('document.documentElement.outerHTML')) return '<html><body><button data-testid="save">Save</button><button>Cancel</button></body></html>'
    if (expression.includes('document.body') && expression.includes('innerText')) return 'Save\nCancel'
    if (expression.includes('Object.fromEntries(Array.from') && expression.includes('inner_text')) {
      return [{
        attributes: { 'data-testid': 'save' },
        inner_text: 'Save',
        text_content: 'Save'
      }, {
        attributes: {},
        inner_text: 'Cancel',
        text_content: 'Cancel'
      }]
    }
    if (expression.includes('elementState') && expression.includes('checked')) {
      checkboxToggleArmed = true
      return { checked: checkboxChecked, isRadio: false }
    }
    if (expression.includes('textContent') && expression.includes('map')) return ['Save', 'Cancel']
    if (expression.includes('textContent')) return 'Save'
    if (expression.includes('innerText')) return 'Save'
    if (expression.includes('getAttribute')) return 'save'
    if (expression.includes('elementState') && expression.includes('stateName')) return true
    if (expression.includes('querySelectorAll') && expression.includes('length')) return 2
    if (expression.includes('elementState') && expression.includes('visible')) return true
    if (expression.includes('elementState') && expression.includes('enabled')) return true
    if (expression.includes('querySelectorAll')) return true
    return 'ok'
  }
  const handleExecuteCdp = async (params: Record<string, unknown>) => {
    const target = params.target && typeof params.target === 'object' ? params.target as Record<string, unknown> : {}
    const tab = tabForBackendId(target.tabId ?? target.tab_id ?? params.tabId ?? params.tab_id) ?? backendTabs[0]
    if (!tab) return {}
    const method = String(params.method ?? '')
    const commandParams = params.commandParams && typeof params.commandParams === 'object'
      ? params.commandParams as Record<string, unknown>
      : {}
    cdpCommands.push({ method, target, commandParams })
    switch (method) {
      case 'Runtime.enable':
        emitCdpEvent(tab.id, 'Runtime.consoleAPICalled', {
          type: 'warning',
          args: [{ type: 'string', value: 'reference warning' }]
        })
        return {}
      case 'Runtime.evaluate':
        return { result: { value: evaluateExpression(String(commandParams.expression ?? ''), tab) } }
      case 'Page.getFrameTree':
        return { frameTree: { frame: { id: 'main-frame', url: tab.url } } }
      case 'Page.getLayoutMetrics':
        return layoutMetrics()
      case 'Page.navigate':
        if (options.navigationFailure) {
          throw BrowserUseBackendError.navigationFailed(options.navigationFailure.errorDescription, {
            ...options.navigationFailure,
            isMainFrame: true
          })
        }
        tab.url = String(commandParams.url ?? tab.url)
        if (options.staleDomNodesAfterNavigation) staleDomNodeIds.add(42)
        emitNavigationEvents(tab)
        return { frameId: 'main-frame' }
      case 'Page.captureScreenshot':
        return { data: 'AQID' }
      case 'DOM.scrollIntoViewIfNeeded': {
        const backendNodeId = Number(commandParams.backendNodeId)
        if (staleDomNodeIds.has(backendNodeId)) throw BrowserUseBackendError.nodeStale(backendNodeId)
        return {}
      }
      case 'DOM.getDocument':
        return domRoot()
      case 'DOM.getBoxModel':
        return { model: { border: [10, 20, 110, 20, 110, 60, 10, 60] } }
      case 'DOM.getContentQuads':
        return { quads: [[10, 20, 110, 20, 110, 60, 10, 60]] }
      case 'DOMSnapshot.captureSnapshot':
        return resourceSnapshot(tab.url)
      case 'Page.getResourceTree':
        return { frameTree: { frame: { id: 'main-frame', url: tab.url }, resources: [{ url: `${tab.url.replace(/\/$/, '')}/site.css`, mimeType: 'text/css' }] } }
      case 'Page.getResourceContent':
        return { content: 'body { color: red; }', base64Encoded: false }
      case 'Target.getTargets':
        return { targetInfos: [] }
      case 'Input.dispatchMouseEvent':
        if (commandParams.type === 'mouseReleased' && checkboxToggleArmed) {
          checkboxChecked = !checkboxChecked
          checkboxToggleArmed = false
        }
        return {}
      default:
        return {}
    }
  }
  backendServer = new BrowserUseBackendServer({
    async handleBrowserUseBackendRequest(method, params) {
      if (method === 'ping') return 'pong'
      if (method === 'getInfo') {
        return {
          id: 'iab',
          name: 'DotCraft Browser',
          type: 'iab',
          capabilities: {
            browser: [
              { id: 'visibility', description: 'Show or hide the browser.' },
              { id: 'viewport', description: 'Set or reset the browser viewport.' }
            ],
            tab: [
              { id: 'pageAssets', description: 'List and bundle page assets.' }
            ]
          },
          metadata: { dotcraftSessionId: params.session_id }
        }
      }
      if (method === 'getTabs' || method === 'getUserTabs') return backendTabs.map(publicTab)
      if (method === 'getUserHistory') {
        throw BrowserUseBackendError.unsupportedApi('browser.user.history is not supported by Desktop IAB')
      }
      if (method === 'createTab') {
        createTabRequests.push({ ...params })
        const tab = { id: nextTabId++, url: 'about:blank', title: 'Test Page', loading: false, active: true }
        backendTabs.forEach((item) => { item.active = false })
        backendTabs.push(tab)
        return publicTab(tab)
      }
      if (method === 'claimUserTab') {
        const tab = backendTabs.find((item) => item.id === Number(params.tabId))
        return tab ? publicTab(tab) : null
      }
      if (method === 'finalizeTabs') return { ok: true, kept: [], closed: [], released: [] }
      if (method === 'nameSession') return { ok: true, name: params.name }
      if (method === 'attach' || method === 'detach') return { ok: true }
      if (method === 'executeCdp') return await handleExecuteCdp(params)
      if (method === 'moveMouse') {
        moveMouseCalls.push({ ...params })
        return { ok: true }
      }
      if (method === 'executeUnhandledCommand') {
        unhandledCommands.push({ ...params })
        if (params.type === 'list_tabs') return { tabs: backendTabs.map((tab) => ({ ...tab, id: String(tab.id) })) }
        if (params.type === 'create_tab') {
          const tab = { id: nextTabId++, url: 'about:blank', title: 'Test Page', loading: false, active: true }
          backendTabs.push(tab)
          return { id: String(tab.id) }
        }
        if (params.type === 'browser_visibility_get') return { visible: browserVisible }
        if (params.type === 'browser_visibility_set') {
          browserVisible = params.visible === true
          return {}
        }
        if (params.type === 'browser_viewport_set') {
          viewport = {
            width: Number(params.width),
            height: Number(params.height)
          }
          return {}
        }
        if (params.type === 'browser_viewport_reset') {
          viewport = { width: 1280, height: 720 }
          return {}
        }
        if (params.type === 'tabs_content') {
          const urls = Array.isArray(params.urls) ? params.urls : []
          return {
            results: urls.map((url) => ({
              url: String(url),
              title: 'Test Page',
              content: params.content_type === 'html'
                ? '<html><body><button>Save</button></body></html>'
                : 'Save\nCancel'
            }))
          }
        }
        if (params.type === 'tab_content_export') {
          throw BrowserUseBackendError.unsupportedApi('tab_content_export')
        }
        if (params.type === 'tab_dev_logs') {
          const tab = tabForBackendId(params.tab_id ?? params.tabId) ?? backendTabs[0]
          return {
            logs: [{
              level: 'warn',
              message: 'reference warning',
              timestamp: '2026-06-05T00:00:00.000Z',
              url: tab?.url
            }]
          }
        }
        if (params.type === 'tab_screenshot') return { data: 'AQID' }
        if (params.type === 'playwright_wait_for_load_state') return {}
        if (params.type === 'tab_clipboard_read_text') return { text: clipboardPlainText() }
        if (params.type === 'tab_clipboard_write_text') {
          if (typeof params.text !== 'string') throw BrowserUseBackendError.invalidArgument('tab_clipboard_write_text requires text.')
          clipboardText = params.text
          clipboardItems = [{
            entries: [{ mime_type: 'text/plain', text: clipboardText }],
            presentation_style: 'unspecified'
          }]
          return {}
        }
        if (params.type === 'tab_clipboard_read') {
          return { items: clipboardItems }
        }
        if (params.type === 'tab_clipboard_write') {
          clipboardItems = Array.isArray(params.items)
            ? params.items as typeof clipboardItems
            : []
          clipboardText = clipboardPlainText()
          return {}
        }
        return {}
      }
      throw BrowserUseBackendError.methodNotFound(method)
    }
  })
  const browser = {
    nameSession: vi.fn(async (name: string) => ({ ok: true, name })),
    tabs: {
      content: vi.fn(async (options?: { urls?: unknown[]; contentType?: string; content_type?: string }) => {
        const urls = Array.isArray(options?.urls) ? options.urls : []
        const contentType = options?.content_type ?? options?.contentType ?? 'text'
        return urls.map((url) => ({
          url: String(url),
          title: 'Test Page',
          content: contentType === 'html'
            ? '<html><body><button>Save</button></body></html>'
            : 'Save\nCancel'
        }))
      }),
      describeApi: () => ['selected()', 'new(url?)', 'content({ urls, contentType })', 'finalize({ keep: [{ tab, status: "deliverable"|"handoff" }] })']
    },
    describeApi: () => ['nameSession(name)', 'tabs.content({ urls, contentType })', 'tabs.finalize({ keep: [{ tab, status: "deliverable"|"handoff" }] })']
  }
  return {
    prepareNodeRepl: vi.fn(async () => {
      await backendServer.ensureStarted()
      return {
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
      }
    }),
    abortEvaluation: vi.fn(() => {
      logs.push('Browser evaluation aborted.\nRecent browser operations:\ncua.click status=active tab=tab-1 url=http://127.0.0.1:5173/ elapsedMs=1000 timeoutMs=10000')
      return { ok: true }
    }),
    handleBrowserUseElicitation: vi.fn(async (_threadId: string, request: unknown) => ({
      action: request && typeof request === 'object' && (request as { meta?: { file_transfer?: string } }).meta?.file_transfer === 'download'
        ? 'accept'
        : 'decline',
      meta: { persist: 'session' }
    })),
    reset: vi.fn(() => ({ ok: true })),
    releasePending: () => {
      while (pendingActions.length) pendingActions.shift()?.()
    },
    cdpCommands,
    createTabRequests,
    unhandledCommands,
    moveMouseCalls,
    closeBackendForTests: async () => {
      await backendServer.close()
    }
  }
}

describe('NodeReplManager', () => {
  const initialCwd = nodeProcess.cwd()
  const managers: NodeReplManager[] = []
  const browserManagers: Array<ReturnType<typeof createFakeBrowserManager>> = []
  const createManager = (browserManager: ReturnType<typeof createFakeBrowserManager>) => {
    const manager = new NodeReplManager(browserManager as never)
    managers.push(manager)
    browserManagers.push(browserManager)
    return manager
  }

  afterEach(async () => {
    if (nodeProcess.cwd() !== initialCwd) nodeProcess.chdir(initialCwd)
    await Promise.all(managers.map((manager) => manager.disposeAllForTests()))
    await Promise.all(browserManagers.map((manager) => manager.closeBackendForTests()))
    managers.length = 0
    browserManagers.length = 0
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
        return "first"
      `
    })
    const second = manager.evaluate(owner, {
      threadId: 'thread-queue',
      evaluationId: 'eval-second',
      code: `
        globalThis.queueOrder.push("second")
        return JSON.stringify(globalThis.queueOrder)
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
        return "first"
      `
    })
    const second = manager.evaluate(owner, {
      threadId: 'thread-queue-cancel',
      evaluationId: 'eval-second',
      code: 'return "second"'
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
        return "done"
      `
    })

    expect(result.resultText).toBe('done')
    expect(result.logs).toEqual(['hello 42'])
    expect(result.images).toEqual([{ mediaType: 'image/png', dataBase64: 'AQID' }])
    manager.reset('thread-1')
  })

  it('does not expose browser agent globals before browser-client setup', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-fresh-browser-client',
      code: `
        return JSON.stringify({
          agentType: typeof agent,
          legacySetupType: typeof __dotcraftSetupBrowserRuntime,
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
      legacySetupType: 'undefined',
      browserClientPath: 'string',
      browserSession: 'object',
      nativePipe: 'function',
      emitImage: 'function'
    })
    manager.reset('thread-fresh-browser-client')
  })

  it('loads browser-client.mjs and initializes IAB globals', async () => {
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
          browserNameSession: typeof browser.nameSession,
          tabsContent: typeof browser.tabs.content
        })
      `
    })

    expect(result.error).toBeUndefined()
    const payload = JSON.parse(result.resultText ?? '{}')
    expect(payload.list[0]).toMatchObject({ name: 'DotCraft Browser', type: 'iab' })
    expect(payload.browserNameSession).toBe('function')
    expect(payload.tabsContent).toBe('function')
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

  it('exposes browser agent.browsers and can select the IAB backend', async () => {
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
          browserNameSession: typeof browser.nameSession
        })
      `
    })

    expect(result.error).toBeUndefined()
    const payload = JSON.parse(result.resultText ?? '{}')
    expect(payload.list[0]).toMatchObject({ name: 'DotCraft Browser', type: 'iab' })
    expect(payload.browserNameSession).toBe('function')
    manager.reset('thread-1')
  })

  it('only exposes WebMCP through the browser client when the current page provides page tools', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-webmcp-availability',
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
        await setupBrowserRuntime({ globals: globalThis, backend: "iab" })
        const browser = await agent.browsers.get("iab")
        const tab = await browser.tabs.new("http://localhost:3000/")
        const capabilities = await tab.capabilities.list()
        let webmcpError = ""
        try {
          await tab.capabilities.get("webmcp")
        } catch (error) {
          webmcpError = error && typeof error === "object" && "message" in error ? error.message : String(error)
        }
        return JSON.stringify({
          ids: capabilities.map((capability) => capability.id),
          webmcpError
        })
      `
    })

    expect(result.error).toBeUndefined()
    expect(JSON.parse(result.resultText ?? '{}')).toEqual({
      ids: ['pageAssets'],
      webmcpError: 'Capability is not available: webmcp'
    })
    manager.reset('thread-webmcp-availability')
  })

  it('creates a URL tab through the browser client without a second client-side navigation', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-new-url-single-navigation',
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
        await setupBrowserRuntime({ globals: globalThis, backend: "iab" })
        const browser = await agent.browsers.get("iab")
        const tab = await browser.tabs.new("http://localhost:3000/")
        return await tab.url()
      `
    })

    expect(result.error).toBeUndefined()
    expect(browserManager.createTabRequests).toHaveLength(1)
    expect(browserManager.createTabRequests[0]).toMatchObject({ url: 'http://localhost:3000/' })
    expect(browserManager.cdpCommands.filter((command) => command.method === 'Page.navigate')).toHaveLength(0)
    manager.reset('thread-new-url-single-navigation')
  })

  it('preserves structured backend navigation errors in the browser client', async () => {
    const browserManager = createFakeBrowserManager({
      navigationFailure: {
        errorCode: -100,
        errorDescription: 'ERR_CONNECTION_CLOSED',
        validatedURL: 'https://bad.example/',
        finalURL: 'about:blank'
      }
    })
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-navigation-error-data',
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
        await setupBrowserRuntime({ globals: globalThis, backend: "iab" })
        const browser = await agent.browsers.get("iab")
        const tab = await browser.tabs.new()
        try {
          await tab.goto("https://bad.example/")
          return "resolved"
        } catch (error) {
          return JSON.stringify({
            name: error.name,
            category: error.category,
            code: error.code,
            data: error.data,
            message: error.message
          })
        }
      `
    })

    expect(result.error).toBeUndefined()
    const payload = JSON.parse(result.resultText ?? '{}')
    expect(payload).toMatchObject({
      name: 'BrowserClientError',
      category: 'NavigationFailed',
      code: -32012,
      message: 'NavigationFailed: ERR_CONNECTION_CLOSED',
      data: {
        errorCode: -100,
        errorDescription: 'ERR_CONNECTION_CLOSED',
        validatedURL: 'https://bad.example/',
        finalURL: 'about:blank',
        isMainFrame: true
      }
    })
    manager.reset('thread-navigation-error-data')
  })

  it('lets the DotCraft browser client create, list, select, and finalize IAB tabs', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-reference-tabs',
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
        await setupBrowserRuntime({ globals: globalThis, backend: "iab" })
        const browser = await agent.browsers.get("iab")
        const tab = await browser.tabs.new()
        const selected = await browser.tabs.selected()
        const list = await browser.tabs.list()
        const openTabs = await browser.user.openTabs()
        const missingClaim = await browser.user.claimTab("999")
        const listJson = JSON.stringify({ list })
        const openTabsJson = JSON.stringify({ openTabs })
        const tabJson = JSON.stringify({ tab })
        await browser.tabs.finalize({ keep: [{ tab, status: "handoff" }] })
        return JSON.stringify({
          tabId: tab.id,
          selectedId: selected?.id,
          listIds: list.map((item) => item.id),
          openTabIds: openTabs.map((item) => item.id),
          listJson,
          openTabsJson,
          tabJson,
          listItemGotoType: typeof list[0]?.goto,
          openTabsItemGotoType: typeof openTabs[0]?.goto,
          missingClaimIsNull: missingClaim === null,
          listItemTitle: list[0]?.title,
          listItemUrl: list[0]?.url,
          tabsApi: browser.tabs.describeApi().join(',')
        })
      `
    })

    expect(result.error).toBeUndefined()
    const payload = JSON.parse(result.resultText ?? '{}')
    expect(payload.tabId).toMatch(/^[1-9]\d*$/)
    expect(payload.selectedId).toBe(payload.tabId)
    expect(payload.listIds).toEqual([payload.tabId])
    expect(payload.openTabIds).toEqual([payload.tabId])
    expect(payload.listJson).toContain('"id"')
    expect(payload.openTabsJson).toContain('"id"')
    expect(payload.tabJson).toContain('"id"')
    expect(payload.listItemGotoType).toBe('undefined')
    expect(payload.openTabsItemGotoType).toBe('undefined')
    expect(payload.missingClaimIsNull).toBe(true)
    expect(payload.listItemTitle).toBe('Test Page')
    expect(payload.listItemUrl).toBe('about:blank')
    expect(payload.tabsApi).toContain('finalize({ keep: [{ tab, status: "deliverable"|"handoff" }] })')
    manager.reset('thread-reference-tabs')
  })

  it('returns undefined for selected IAB tab when no active tab exists', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-reference-no-selected',
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
        await setupBrowserRuntime({ globals: globalThis, backend: "iab" })
        const browser = await agent.browsers.get("iab")
        const selected = await browser.tabs.selected()
        const list = await browser.tabs.list()
        return JSON.stringify({
          selectedType: typeof selected,
          listLength: list.length
        })
      `
    })

    expect(result.error).toBeUndefined()
    expect(JSON.parse(result.resultText ?? '{}')).toEqual({
      selectedType: 'undefined',
      listLength: 0
    })
    manager.reset('thread-reference-no-selected')
  })

  it('drives common Playwright, DOM-CUA, and pageAssets APIs through the DotCraft IAB client', async () => {
    const browserManager = createFakeBrowserManager({ webMcpAvailable: true })
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-reference-api',
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
        await setupBrowserRuntime({ globals: globalThis, backend: "iab" })
        const browser = await agent.browsers.get("iab")
        const visibility = await browser.capabilities.get("visibility")
        await visibility.set(true)
        const browserVisible = await visibility.get()
        await browser.capabilities.get("visibility").set(false)
        const browserVisibleAfterDirectSet = await browser.capabilities.get("visibility").get()
        await browser.capabilities.get("visibility").set(true)
        const viewport = await browser.capabilities.get("viewport")
        await viewport.set({ width: 900, height: 640 })
        await browser.capabilities.get("viewport").set({ width: 901, height: 641 })
        await viewport.reset()
        const tab = await browser.tabs.new()
        await tab.goto("http://localhost:3000/")
        await tab.playwright.waitForTimeout(0)
        await tab.playwright.expectNavigation(
          () => tab.goto("http://localhost:3000/after"),
          { url: "http://localhost:3000/after", waitUntil: "load", timeoutMs: 1000 }
        )
        await tab.goto("http://localhost:3000/")
        await tab.playwright.waitForURL("http://localhost:3000/", { timeoutMs: 1000 })
        await tab.playwright.waitForLoadState({ state: "load", timeoutMs: 1000 })
        const tabTitle = await tab.title()
        const tabUrl = await tab.url()
        const tabById = await browser.tabs.get(tab.id)
        const tabByIdTitle = await tabById.title()
        const tabsContent = await browser.tabs.content({
          urls: ["http://localhost:3000/content"],
          contentType: "text",
          timeoutMs: 1000
        })
        let readonlyScroll = ""
        try {
          await tab.playwright.evaluate(() => window.scrollTo(0, 10), undefined, { timeoutMs: 1000 })
        } catch (error) {
          readonlyScroll = error instanceof Error ? error.message : String(error)
        }
        const title = await tab.playwright.evaluate(() => document.title)
        const evalWithArg = await tab.playwright.evaluate((value) => value + 1, 41, { timeoutMs: 1000 })
        const snapshot = await tab.playwright.domSnapshot()
        const locator = tab.playwright.locator("button")
        const count = await locator.count()
        const texts = await locator.allTextContents({ timeoutMs: 1000 })
        const filteredCount = await locator.filter({ hasText: "Save" }).count()
        const filteredNotText = await locator.filter({ hasNotText: /Cancel/ }).allTextContents({ timeoutMs: 1000 })
        const filteredVisibleCount = await locator.filter({ visible: true }).count()
        const hasCount = await tab.playwright.locator("button", { has: tab.playwright.getByTestId("save") }).count()
        const hasNotText = await locator.filter({ hasNot: tab.playwright.getByTestId("save") }).textContent({ timeoutMs: 1000 })
        const andCount = await locator.and(tab.playwright.getByTestId("save")).count()
        const orTexts = await tab.playwright.getByText("Cancel").or(tab.playwright.getByTestId("save")).allTextContents({ timeoutMs: 1000 })
        const allLocators = await locator.all()
        const firstCachedText = await allLocators[0].textContent({ timeoutMs: 1000 })
        const secondCachedText = await allLocators[1].innerText({ timeoutMs: 1000 })
        const actionLocator = tab.playwright.getByRole("button", { name: "Save" })
        const buttonText = await actionLocator.textContent({ timeoutMs: 1000 })
        const buttonInnerText = await actionLocator.innerText({ timeoutMs: 1000 })
        const buttonTestId = await actionLocator.getAttribute("data-testid", { timeoutMs: 1000 })
        const buttonVisible = await actionLocator.isVisible({ timeoutMs: 1000 })
        const buttonEnabled = await actionLocator.isEnabled({ timeoutMs: 1000 })
        const textCount = await tab.playwright.getByText("Cancel").count()
        const placeholderVisible = await tab.playwright.getByPlaceholder("Email").isVisible({ timeoutMs: 1000 })
        const testIdCount = await tab.playwright.getByTestId("save").count()
        const frameCount = await tab.playwright.frameLocator("iframe").locator("button").count()
        await actionLocator.waitFor({ state: "visible", timeoutMs: 1000 })
        await actionLocator.click({ timeoutMs: 1000 })
        await actionLocator.dblclick({ timeoutMs: 1000 })
        const inputLocator = tab.playwright.getByLabel("Name")
        await inputLocator.fill("Ada", { timeoutMs: 1000 })
        await inputLocator.type(" Lovelace", { timeoutMs: 1000 })
        await inputLocator.press("Enter", { timeoutMs: 1000 })
        const checkboxLocator = tab.playwright.getByLabel("Accept")
        await checkboxLocator.check({ timeoutMs: 1000 })
        await checkboxLocator.uncheck({ timeoutMs: 1000 })
        await checkboxLocator.setChecked(true, { timeoutMs: 1000 })
        await tab.playwright.locator("select").selectOption("value-a", { timeoutMs: 1000 })
        const screenshot = await tab.screenshot()
        await tab.clipboard.writeText("reference clipboard")
        const clipboardText = await tab.clipboard.readText()
        await tab.clipboard.write([{ entries: [{ mimeType: "text/plain", text: "rich clipboard" }], presentationStyle: "inline" }])
        const clipboardItems = await tab.clipboard.read()
        const visibleDom = await tab.dom_cua.get_visible_dom()
        await tab.cua.move({ x: 12, y: 18 })
        await tab.cua.click({ x: 12, y: 18 })
        await tab.cua.double_click({ x: 12, y: 18 })
        await tab.cua.drag({ path: [{ x: 12, y: 18 }, { x: 40, y: 45 }] })
        await tab.cua.type({ text: "typed through cua" })
        await tab.cua.keypress({ keys: ["Enter"] })
        await tab.cua.scroll({ x: 12, y: 18, scrollX: 0, scrollY: 80 })
        await tab.dom_cua.scroll({ y: 120 })
        await tab.dom_cua.click({ node_id: "42" })
        await tab.dom_cua.double_click({ node_id: "42" })
        await tab.dom_cua.type({ text: "hello" })
        await tab.dom_cua.keypress({ keys: ["Enter"] })
        await tab.dom_cua.scroll({ node_id: "42", y: 120 })
        const pageAssets = await tab.capabilities.get("pageAssets")
        const inventory = await pageAssets.list()
        const directPageAssets = await tab.capabilities.get("pageAssets")
        const directInventory = await directPageAssets.list()
        const tabCapabilities = await tab.capabilities.list()
        const bundle = await pageAssets.bundle({ inventoryId: inventory.id, kinds: ["stylesheet"] })
        const webmcp = await tab.capabilities.get("webmcp")
        const tools = await webmcp.listTools()
        const toolResult = await tools[0].invoke({ topic: "iab" }, { timeoutMs: 1000 })
        const devLogs = await tab.dev.logs({ limit: 1 })
        return JSON.stringify({
          title,
          evalWithArg,
          tabTitle,
          tabUrl,
          tabsContentApi: typeof browser.tabs.content,
          tabByIdTitle,
          tabsContent: tabsContent[0]?.content,
          readonlyScroll,
          browserVisible,
          browserVisibleAfterDirectSet,
          snapshot,
          count,
          texts,
          filteredCount,
          filteredNotText,
          filteredVisibleCount,
          hasCount,
          hasNotText,
          andCount,
          orTexts,
          firstCachedText,
          secondCachedText,
          buttonText,
          buttonInnerText,
          buttonTestId,
          buttonVisible,
          buttonEnabled,
          textCount,
          placeholderVisible,
          testIdCount,
          frameCount,
          screenshotLength: screenshot.length,
          clipboardText,
          richClipboardText: clipboardItems[0]?.entries[0]?.text,
          richClipboardStyle: clipboardItems[0]?.presentationStyle,
          visibleDom,
          devLogLevel: devLogs[0]?.level,
          devLogMessage: devLogs[0]?.message,
          assetCount: inventory.assets.length,
          directAssetCount: directInventory.assets.length,
          tabCapabilityIds: tabCapabilities.map((capability) => capability.id),
          inlineSvgCount: inventory.inlineSvgs.length,
          bundleDownloaded: bundle.summary.downloadedCount,
          webMcpToolName: tools[0]?.name,
          webMcpInputType: tools[0]?.inputSchema?.type,
          webMcpResultOk: toolResult.ok,
          webMcpResultTopic: toolResult.input?.topic
        })
      `
    })

    expect(result.error).toBeUndefined()
    const payload = JSON.parse(result.resultText ?? '{}')
    expect(payload).toMatchObject({
      title: 'Test Page',
      evalWithArg: 42,
      tabTitle: 'Test Page',
      tabUrl: 'http://localhost:3000/',
      tabsContentApi: 'function',
      tabByIdTitle: 'Test Page',
      tabsContent: 'Save\nCancel',
      readonlyScroll: expect.stringContaining('ReadonlyEvaluateViolation'),
      browserVisible: true,
      browserVisibleAfterDirectSet: false,
      count: 2,
      texts: ['Save', 'Cancel'],
      filteredCount: 1,
      filteredNotText: ['Save'],
      filteredVisibleCount: 2,
      hasCount: 1,
      hasNotText: 'Cancel',
      andCount: 1,
      orTexts: ['Save', 'Cancel'],
      firstCachedText: 'Save',
      secondCachedText: 'Cancel',
      buttonText: 'Save',
      buttonInnerText: 'Save',
      buttonTestId: 'save',
      buttonVisible: true,
      buttonEnabled: true,
      textCount: 1,
      placeholderVisible: true,
      testIdCount: 1,
      frameCount: 1,
      screenshotLength: 3,
      clipboardText: 'reference clipboard',
      richClipboardText: 'rich clipboard',
      richClipboardStyle: 'inline',
      devLogLevel: 'warn',
      devLogMessage: 'reference warning',
      assetCount: 1,
      directAssetCount: 1,
      tabCapabilityIds: ['pageAssets', 'webmcp'],
      inlineSvgCount: 1,
      bundleDownloaded: 1,
      webMcpToolName: 'summarize',
      webMcpInputType: 'object',
      webMcpResultOk: true,
      webMcpResultTopic: 'iab'
    })
    expect(payload.snapshot).toContain('button')
    expect(payload.visibleDom).toContain('node_id=42')
    expect(browserManager.cdpCommands.map((command) => command.method)).toEqual(expect.arrayContaining([
      'Page.navigate',
      'Runtime.evaluate',
      'DOM.getDocument',
      'DOMSnapshot.captureSnapshot',
      'Page.getResourceContent',
      'Input.dispatchMouseEvent',
      'Input.dispatchKeyEvent',
      'Input.synthesizeScrollGesture'
    ]))
    expect(browserManager.cdpCommands).toContainEqual(expect.objectContaining({
      method: 'Input.synthesizeScrollGesture',
      commandParams: expect.objectContaining({
        gestureSourceType: 'mouse',
        preventFling: true,
        speed: 8000
      })
    }))
    expect(browserManager.cdpCommands).toContainEqual(expect.objectContaining({
      method: 'Input.synthesizeScrollGesture',
      commandParams: expect.objectContaining({
        x: 12,
        y: 18,
        yDistance: -80
      })
    }))
    expect(browserManager.cdpCommands).toContainEqual(expect.objectContaining({
      method: 'Input.synthesizeScrollGesture',
      commandParams: expect.objectContaining({
        x: 640,
        y: 360,
        yDistance: -120
      })
    }))
    expect(browserManager.cdpCommands).toContainEqual(expect.objectContaining({
      method: 'Input.synthesizeScrollGesture',
      commandParams: expect.objectContaining({
        x: 60,
        y: 40,
        yDistance: -120
      })
    }))
    expect(browserManager.unhandledCommands).toContainEqual(expect.objectContaining({
      type: 'playwright_wait_for_load_state',
      state: 'load',
      timeout_ms: 1000
    }))
    expect(browserManager.unhandledCommands).toContainEqual(expect.objectContaining({
      type: 'tab_screenshot',
      tab_id: expect.any(Number)
    }))
    expect(browserManager.moveMouseCalls).toContainEqual(expect.objectContaining({
      x: 12,
      y: 18
    }))
    manager.reset('thread-reference-api')
  })

  it('rejects IAB history and ordinary transfer APIs through the DotCraft browser client', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-reference-unsupported',
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
        await setupBrowserRuntime({ globals: globalThis, backend: "iab" })
        const browser = await agent.browsers.get("iab")
        const tab = await browser.tabs.new()
        const messageOf = async (action) => {
          try {
            await action()
            return "resolved"
          } catch (error) {
            return error instanceof Error ? error.message : String(error)
          }
        }
        return JSON.stringify({
          history: await messageOf(() => browser.user.history({ limit: 1 })),
          downloadEvent: await messageOf(() => tab.playwright.waitForEvent("download", { timeoutMs: 1000 })),
          fileChooserEvent: await messageOf(() => tab.playwright.waitForEvent("filechooser", { timeoutMs: 1000 })),
          contentExport: await messageOf(() => tab.content.export()),
          gsuiteExport: await messageOf(() => tab.content.exportGsuite("pdf")),
          locatorMedia: await messageOf(() => tab.playwright.locator("img").downloadMedia({ timeoutMs: 1000 })),
          domMedia: await messageOf(() => tab.dom_cua.downloadMedia({ node_id: "42", timeoutMs: 1000 }))
        })
      `
    })

    expect(result.error).toBeUndefined()
    const payload = JSON.parse(result.resultText ?? '{}')
    expect(payload.history).toContain('UnsupportedApi: browser.user.history is not supported by Desktop IAB')
    expect(payload.downloadEvent).toContain('Downloads are not supported by DotCraft Browser')
    expect(payload.fileChooserEvent).toContain('File uploads are not supported by DotCraft Browser')
    expect(payload.contentExport).toContain('UnsupportedApi: tab_content_export')
    expect(payload.gsuiteExport).toContain('Downloads are not supported by DotCraft Browser')
    expect(payload.locatorMedia).toContain('Downloads are not supported by DotCraft Browser')
    expect(payload.locatorMedia).toContain('locator.downloadMedia failed for selector img')
    expect(payload.domMedia).toContain('Downloads are not supported by DotCraft Browser')
    manager.reset('thread-reference-unsupported')
  })

  it('surfaces stale DOM-CUA nodes as NodeStale through the DotCraft IAB client', async () => {
    const browserManager = createFakeBrowserManager({ staleDomNodesAfterNavigation: true })
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-reference-node-stale',
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
        await setupBrowserRuntime({ globals: globalThis, backend: "iab" })
        const browser = await agent.browsers.get("iab")
        const tab = await browser.tabs.new()
        await tab.dom_cua.get_visible_dom()
        await tab.goto("http://localhost:3000/after")
        try {
          await tab.dom_cua.click({ node_id: "42" })
          return "resolved"
        } catch (error) {
          return error instanceof Error ? error.message : String(error)
        }
      `
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toContain('NodeStale: Browser node is no longer available: 42')
    manager.reset('thread-reference-node-stale')
  })

  it('loads chrome browser-client.mjs and can delegate non-extension backends to IAB globals', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.chromeBrowserClientPath)
        await setupBrowserRuntime({ globals: globalThis, backend: "iab" })
        return typeof agent.browsers.get
      `
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('function')
    manager.reset('thread-1')
  })

  it('registers the Chrome extension backend when IAB globals already exist', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        if (!globalThis.agent) {
          const { setupBrowserRuntime: setupIabRuntime } = await import(dotcraft.browserClientPath)
          await setupIabRuntime({ globals: globalThis, backend: "iab" })
        }
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
    const backends = JSON.parse(result.resultText ?? '[]').map((item: Record<string, unknown>) => ({
      id: item.id,
      name: item.name,
      type: item.type
    }))
    expect(backends).toEqual([
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
        tmpDir: typeof nodeRepl.tmpDir,
        setResponseMeta: typeof nodeRepl.setResponseMeta
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
      tmpDir: 'string',
      setResponseMeta: 'function'
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
        try {
          await nodeRepl.nativePipe.createConnection("not-a-dotcraft-browser-use-pipe")
          return "connected"
        } catch (error) {
          return error.message
        }
      `
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toContain('Refusing to connect to a non-DotCraft browser-use native pipe.')
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
        await setupBrowserRuntime({ globals: globalThis, backend: "iab" })
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
        await setupBrowserRuntime({ globals: globalThis, backend: "iab" })
        await new Promise(() => {})
      `,
      timeoutMs: 1
    })

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
      code: `
        const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
        await setupBrowserRuntime({ globals: globalThis, backend: "iab" })
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
        await setupBrowserRuntime({ globals: globalThis, backend: "iab" })
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
