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

type FakeBrowser = ReturnType<typeof createFakeBrowserManager>

const prelude = `
  const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
  const agent = await setupBrowserRuntime({ backend: "iab" })
  const browser = await agent.browsers.get("iab")
  const messageOf = async (action) => {
    try {
      await action()
      return "resolved"
    } catch (error) {
      return error instanceof Error ? error.message : String(error)
    }
  }
`

const settleClicks = {
  onInput: ({ commandParams }: { commandParams: Record<string, unknown> }, emit: (method: string, params?: Record<string, unknown>) => void) => {
    if (commandParams.type === 'mouseReleased') emit('Page.navigatedWithinDocument', { frameId: 'main-frame', url: 'http://localhost:3000/#a' })
  }
}

describe('Node REPL browser clients', () => {
  const initialCwd = nodeProcess.cwd()
  const managers: NodeReplManager[] = []
  const browserManagers: FakeBrowser[] = []
  let runCount = 0

  async function evaluate(browserManager: FakeBrowser, code: string) {
    const manager = new NodeReplManager(browserManager as never, workerFixture.fork)
    managers.push(manager)
    browserManagers.push(browserManager)
    return await manager.evaluate({} as Electron.BrowserWindow, { threadId: `client-${runCount += 1}`, code })
  }

  async function run(browserManager: FakeBrowser, body: string): Promise<any> {
    const result = await evaluate(browserManager, `${prelude}
${body}`)
    expect(result.error).toBeUndefined()
    return result.resultText ? JSON.parse(result.resultText) : undefined
  }

  afterEach(async () => {
    if (nodeProcess.cwd() !== initialCwd) nodeProcess.chdir(initialCwd)
    await Promise.all(managers.map((manager) => manager.disposeAll()))
    await Promise.all(browserManagers.map((manager) => manager.closeBackendForTests()))
    managers.length = 0
    browserManagers.length = 0
  })

  it('loads the IAB and Chrome clients as separate agents without installing user globals', async () => {
    const result = await evaluate(createFakeBrowserManager(), `
      const { setupBrowserRuntime: setupIab } = await import(dotcraft.browserClientPath)
      const { setupBrowserRuntime: setupChrome } = await import(dotcraft.chromeBrowserClientPath)
      const iab = await setupIab({ backend: "iab" })
      const chrome = await setupChrome()
      const delegated = await setupChrome({ backend: "iab" })
      const browser = await iab.browsers.get("iab")
      await nodeRepl.emitImage({ mediaType: "image/png", dataBase64: "BAUG" })
      JSON.stringify({
        iab: await iab.browsers.list(),
        chrome: (await chrome.browsers.list())[0].id,
        delegated: typeof delegated.browsers.get,
        nameSession: typeof browser.nameSession,
        tabsContent: typeof browser.tabs.content,
        globalAgent: typeof globalThis.agent
      })
    `)

    expect(result.error).toBeUndefined()
    expect(result.images).toEqual([{ mediaType: 'image/png', dataBase64: 'BAUG' }])
    const payload = JSON.parse(result.resultText!)
    expect(payload.iab[0]).toMatchObject({ id: 'iab', name: 'DotCraft Browser', type: 'iab' })
    expect(payload).toMatchObject({
      chrome: 'extension',
      delegated: 'function',
      nameSession: 'function',
      tabsContent: 'function',
      globalAgent: 'undefined'
    })
  })

  it('only exposes WebMCP through the browser client when the current page provides page tools', async () => {
    const payload = await run(createFakeBrowserManager(), `
      const tab = await browser.tabs.new("http://localhost:3000/")
      const capabilities = await tab.capabilities.list()
      JSON.stringify({
        ids: capabilities.map((capability) => capability.id),
        webmcpError: await messageOf(() => tab.capabilities.get("webmcp"))
      })
    `)

    expect(payload).toEqual({
      ids: ['pageAssets'],
      webmcpError: 'Capability is not available: webmcp'
    })
  })

  it('creates, lists, selects, and finalizes IAB tabs, opening URL tabs without a second navigation', async () => {
    const browserManager = createFakeBrowserManager()
    const payload = await run(browserManager, `
      const selectedBefore = await browser.tabs.selected()
      const listBefore = await browser.tabs.list()
      const tab = await browser.tabs.new()
      const selected = await browser.tabs.selected()
      const list = await browser.tabs.list()
      const openTabs = await browser.user.openTabs()
      const missingClaim = await browser.user.claimTab("999")
      const json = JSON.stringify({ list, openTabs, tab })
      await browser.tabs.finalize({ keep: [{ tab, status: "handoff" }] })
      await (await browser.tabs.new("http://localhost:3000/")).url()
      JSON.stringify({
        selectedBefore: typeof selectedBefore,
        listBeforeLength: listBefore.length,
        tabId: tab.id,
        selectedId: selected?.id,
        listIds: list.map((item) => item.id),
        openTabIds: openTabs.map((item) => item.id),
        json,
        listItemGotoType: typeof list[0]?.goto,
        openTabsItemGotoType: typeof openTabs[0]?.goto,
        missingClaimIsNull: missingClaim === null,
        listItemTitle: list[0]?.title,
        listItemUrl: list[0]?.url,
        tabsApi: browser.tabs.describeApi().join(',')
      })
    `)

    expect(payload.selectedBefore).toBe('undefined')
    expect(payload.listBeforeLength).toBe(0)
    expect(payload.tabId).toMatch(/^[1-9]\d*$/)
    expect(payload.selectedId).toBe(payload.tabId)
    expect(payload.listIds).toEqual([payload.tabId])
    expect(payload.openTabIds).toEqual([payload.tabId])
    expect(payload.json).toContain('"id"')
    expect(payload.listItemGotoType).toBe('undefined')
    expect(payload.openTabsItemGotoType).toBe('undefined')
    expect(payload.missingClaimIsNull).toBe(true)
    expect(payload.listItemTitle).toBe('Test Page')
    expect(payload.listItemUrl).toBe('about:blank')
    expect(payload.tabsApi).toContain('finalize({ keep: [{ tab, status: "deliverable"|"handoff" }] })')
    expect(browserManager.createTabRequests.at(-1)).toMatchObject({ url: 'http://localhost:3000/' })
    expect(browserManager.cdpCommands.filter((command) => command.method === 'Page.navigate')).toHaveLength(0)
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
    const payload = await run(browserManager, `
      const tab = await browser.tabs.new()
      let failure
      try {
        await tab.goto("https://bad.example/")
      } catch (error) {
        failure = JSON.stringify({
          name: error.name,
          category: error.category,
          code: error.code,
          data: error.data,
          message: error.message
        })
      }
      failure
    `)

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
  })

  it('drives common Playwright, DOM-CUA, and pageAssets APIs through the DotCraft IAB client', async () => {
    const browserManager = createFakeBrowserManager({ webMcpAvailable: true, ...settleClicks })
    const payload = await run(browserManager, `
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
        JSON.stringify({
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
    `)

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
      'DOMSnapshot.captureSnapshot',
      'Page.getResourceContent',
      'Input.dispatchMouseEvent',
      'Input.dispatchKeyEvent'
    ]))
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
  })

  it('rejects IAB history and ordinary transfer APIs through the DotCraft browser client', async () => {
    const payload = await run(createFakeBrowserManager(), `
        const tab = await browser.tabs.new()
        JSON.stringify({
          history: await messageOf(() => browser.user.history({ limit: 1 })),
          downloadEvent: await messageOf(() => tab.playwright.waitForEvent("download", { timeoutMs: 1000 })),
          fileChooserEvent: await messageOf(() => tab.playwright.waitForEvent("filechooser", { timeoutMs: 1000 })),
          contentExport: await messageOf(() => tab.content.export()),
          gsuiteExport: await messageOf(() => tab.content.exportGsuite("pdf")),
          locatorMedia: await messageOf(() => tab.playwright.locator("img").downloadMedia({ timeoutMs: 1000 })),
          domMedia: await messageOf(() => tab.dom_cua.downloadMedia({ node_id: "42", timeoutMs: 1000 }))
        })
    `)

    expect(payload.history).toContain('UnsupportedApi: browser.user.history is not supported by Desktop IAB')
    expect(payload.downloadEvent).toContain('Downloads are not supported by DotCraft Browser')
    expect(payload.fileChooserEvent).toContain('File uploads are not supported by DotCraft Browser')
    expect(payload.contentExport).toContain('UnsupportedApi: tab_content_export')
    expect(payload.gsuiteExport).toContain('Downloads are not supported by DotCraft Browser')
    expect(payload.locatorMedia).toContain('Downloads are not supported by DotCraft Browser')
    expect(payload.locatorMedia).toContain('locator.downloadMedia failed for selector img')
    expect(payload.domMedia).toContain('Downloads are not supported by DotCraft Browser')
  })

  it('surfaces stale DOM-CUA nodes as NodeStale through the DotCraft IAB client', async () => {
    const message = await run(createFakeBrowserManager({ staleDomNodesAfterNavigation: true }), `
      const tab = await browser.tabs.new()
      await tab.dom_cua.get_visible_dom()
      await tab.goto("http://localhost:3000/after")
      JSON.stringify(await messageOf(() => tab.dom_cua.click({ node_id: "42" })))
    `)

    expect(message).toContain('NodeStale: Browser node is no longer available: 42')
  })
})
