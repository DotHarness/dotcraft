import { vi } from 'vitest'
import { BrowserUseBackendServer, BrowserUseBackendError } from '../../browserUseBackendServer'
export function createFakeBrowserManager(options: {
  staleDomNodesAfterNavigation?: boolean
  webMcpAvailable?: boolean
  navigationFailure?: { errorDescription: string; validatedURL: string; finalURL: string; errorCode?: number }
} = {}) {
  let activeSessionId: string | undefined
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
        if (params.session_id !== activeSessionId) throw new Error('Foreign test browser session')
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
        if (params.type === 'playwright_locator_operation') {
          return { value: evaluateExpression('__dotcraftBrowserUseClientLocator ' + JSON.stringify(params.descriptor) + JSON.stringify(params.operation) + JSON.stringify(params.payload), backendTabs[0]) }
        }
        if (params.type === 'playwright_dom_snapshot') return { dom_snapshot: JSON.stringify({ title: 'Test Page', url: backendTabs[0]?.url, elements: [{ node_id: '42', ref: '42', role: 'button', name: 'Save', selector: 'button', visible: true, enabled: true }] }) }
        if (params.type === 'dom_cua_node_info') {
          if (staleDomNodeIds.has(Number(params.node_id))) throw BrowserUseBackendError.nodeStale(Number(params.node_id))
          return { visible: true, enabled: true, boundingBox: { x: 10, y: 20, width: 100, height: 40 } }
        }
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
    prepareNodeRepl: vi.fn(async (_owner: unknown, params: any) => {
      logs.length = 0
      images.length = 0
      activeSessionId = params.browserSession?.sessionId ?? params.threadId
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
