import { afterEach, describe, expect, it } from 'vitest'

import { createMockBridge, createPipePath } from './helpers/chromeBridgeFixture'

async function importChromeClient() {
  return await import(new URL('../../../resources/chrome/browser-client.mjs', import.meta.url).href)
}

describe('chrome browser-client', () => {
  const bridges: Array<{ close: () => Promise<void> }> = []

  afterEach(async () => {
    await Promise.all(bridges.map((bridge) => bridge.close()))
    bridges.length = 0
  })

  it('exposes discoverable tabs and user APIs over the bridge', async () => {
    const tab = {
      id: 7,
      tabId: 7,
      windowId: 1,
      title: 'Example',
      url: 'https://example.test/',
      active: true,
      index: 0
    }
    const bridge = createMockBridge((request) => {
      switch (request.method) {
        case 'browser.nameSession':
          return { ok: true, name: request.params.name }
        case 'user.openTabs':
        case 'tabs.list':
          return [tab]
        case 'user.claimTab':
        case 'tabs.selected':
        case 'tabs.get':
          return tab
        case 'tab.contentText':
          return 'hello world'
        case 'tab.contentHtml':
          return '<main>hello world</main>'
        case 'tabs.content':
          return [{ tab, title: tab.title, url: tab.url, content: '<main>hello world</main>' }]
        case 'tab.domSnapshot':
          return [{ tagName: 'button', name: 'Save' }]
        case 'tab.goto':
          return { ...tab, url: String(request.params.url), title: 'Next', loading: false }
        case 'tab.url':
          return tab.url
        case 'tab.title':
          return tab.title
        case 'locator.action':
          if (request.params.action === 'innerText') return 'Save'
          if (request.params.action === 'getAttribute') return 'button'
          if (request.params.action === 'isVisible') return true
          if (request.params.action === 'allTextContents') return ['Save', 'Cancel']
          if (request.params.action === 'waitFor') return { ok: true }
          if (request.params.action === 'press') return { ok: true }
          if (request.params.action === 'check') return { ok: true }
          if (request.params.action === 'selectOption') return { ok: true }
          return { ok: true }
        case 'tab.waitForLoadState':
          return { ok: true, state: request.params.state }
        case 'tab.waitForURL':
          return { ok: true }
        case 'tab.waitForNavigation':
          return { ok: true, url: 'https://example.test/next' }
        case 'tab.screenshot':
          return { mediaType: 'image/png', dataBase64: 'AQID' }
        case 'tab.evaluate':
          return request.params.arg
        case 'tab.waitForFileChooser':
          return { selector: '[data-dotcraft-file-chooser-id="abc"]', multiple: true, accept: '.txt' }
        case 'tab.fileChooserIsMultiple':
          return true
        case 'tab.fileChooserSetFiles':
          return { ok: true, fileCount: Array.isArray(request.params.files) ? request.params.files.length : 0 }
        case 'cua.action':
          return { ok: true, action: request.params.action }
        case 'domCua.visibleDom':
          return [{ node_id: 'dom:0', tagName: 'button', visibleText: 'Save' }]
        case 'domCua.action':
          return { ok: true, action: request.params.action }
        case 'tabs.finalize':
          return { ok: true }
        default:
          throw new Error(`unexpected method ${request.method}`)
      }
    })
    bridges.push(bridge)
    const pipePath = await bridge.listen()
    const { setupBrowserRuntime } = await importChromeClient()
    const browserSession = {
      protocolVersion: 1,
      sessionId: 'thread-1',
      threadId: 'thread-1',
      turnId: 'turn-1',
      evaluationId: 'eval-1'
    }
    const globals: Record<string, unknown> = { dotcraft: { browserSession } }

    const agent = await setupBrowserRuntime({ globals, backend: 'extension', chromeHost: { pipePaths: [pipePath] } })
    const browser = await agent.browsers.get('extension')

    expect(await agent.browsers.list()).toEqual([
      { id: 'extension', name: 'DotCraft Chrome', type: 'extension' }
    ])
    expect(await browser.nameSession('docs')).toEqual({ ok: true, name: 'docs' })
    expect(Object.getOwnPropertyNames(Object.getPrototypeOf(browser.tabs))).toContain('read')
    expect(Object.getOwnPropertyNames(Object.getPrototypeOf(browser.user))).toContain('claimTab')

    const tabs = await browser.user.openTabs()
    const claimed = await browser.user.claimTab(tabs[0])
    expect(await claimed.content.get({ maxLength: 5 })).toBe('hello')
    expect(await claimed.content.read({ contentType: 'html' })).toBe('<main>hello world</main>')
    expect(await browser.tabs.read({ maxLength: 5 })).toBe('hello')
    expect(await browser.tabs.content({ urls: ['https://example.test/'], contentType: 'html' })).toEqual([
      { tab, title: tab.title, url: tab.url, content: '<main>hello world</main>' }
    ])
    expect(await claimed.playwright.domSnapshot()).toEqual([{ tagName: 'button', name: 'Save' }])
    expect(await claimed.playwright.locator('button').innerText()).toBe('Save')
    expect(await claimed.playwright.locator('button').getAttribute('role')).toBe('button')
    expect(await claimed.playwright.locator('button').isVisible()).toBe(true)
    expect(await claimed.playwright.locator('button').allTextContents()).toEqual(['Save', 'Cancel'])
    await claimed.playwright.locator('button').waitFor()
    await claimed.playwright.locator('button').press('Enter')
    await claimed.playwright.locator('input[type=checkbox]').check()
    await claimed.playwright.locator('input[type=checkbox]').setChecked(false)
    await claimed.playwright.locator('select').selectOption('a')
    await claimed.playwright.waitForLoadState({ state: 'domcontentloaded', timeoutMs: 1234 })
    await claimed.playwright.waitForLoadState('load', { timeoutMs: 2345 })
    await claimed.playwright.waitForURL('https://example.test/next', { timeoutMs: 3456 })
    await expect(claimed.goto('https://example.test/next')).resolves.toBe(claimed)
    await expect(claimed.evaluate((arg: { answer: number }) => arg.answer, { answer: 42 }, {
      timeoutMs: 1234,
      maxBytes: 256
    })).resolves.toEqual({ answer: 42 })
    await expect(claimed.observe({ screenshot: true })).resolves.toMatchObject({
      tab: { id: 7, tabId: 7, url: 'https://example.test/' },
      url: 'https://example.test/',
      title: 'Example',
      loading: false,
      domSnapshot: [{ tagName: 'button', name: 'Save' }],
      screenshot: { mediaType: 'image/png', dataBase64: 'AQID' }
    })
    await expect(claimed.playwright.observe()).resolves.toMatchObject({
      url: 'https://example.test/',
      domSnapshot: [{ tagName: 'button', name: 'Save' }]
    })
    await expect(claimed.playwright.expectNavigation(
      () => claimed.playwright.locator('a').click(),
      { timeoutMs: 4567, waitUntil: 'load' }
    )).resolves.toEqual({ ok: true })
    await expect(claimed.playwright.expectNavigation(
      () => claimed.playwright.locator('a').click(),
      { url: 'https://example.test/target', timeoutMs: 5678 }
    )).resolves.toEqual({ ok: true })
    expect(await claimed.capabilities.list()).toEqual([])
    expect(await browser.capabilities.list()).toEqual([])
    const chooser = await claimed.playwright.waitForEvent('filechooser', { timeoutMs: 4567 })
    expect(await chooser.isMultiple()).toBe(true)
    await expect(chooser.setFiles(['C:\\tmp\\a.txt', 'C:\\tmp\\b.txt'])).resolves.toEqual({ ok: true, fileCount: 2 })
    expect(await claimed.cua.get_visible_screenshot()).toEqual({ mediaType: 'image/png', dataBase64: 'AQID' })
    await expect(claimed.cua.click({ x: 10, y: 20 })).resolves.toEqual({ ok: true, action: 'click' })
    await expect(claimed.cua.double_click({ x: 10, y: 20 })).resolves.toEqual({ ok: true, action: 'double_click' })
    await expect(claimed.cua.scroll({ x: 10, y: 20, deltaY: 100 })).resolves.toEqual({ ok: true, action: 'scroll' })
    await expect(claimed.cua.type('hello')).resolves.toEqual({ ok: true, action: 'type' })
    await expect(claimed.cua.keypress('Enter')).resolves.toEqual({ ok: true, action: 'keypress' })
    expect(await claimed.dom_cua.get_visible_dom()).toEqual([{ node_id: 'dom:0', tagName: 'button', visibleText: 'Save' }])
    await expect(claimed.dom_cua.click({ node_id: 'dom:0' })).resolves.toEqual({ ok: true, action: 'click' })
    await expect(claimed.playwright.waitForEvent('download')).rejects.toThrow('does not support')
    await expect(claimed.clipboard.readText()).rejects.toThrow('does not support')
    await browser.tabs.finalize({ keep: [{ tab: claimed, status: 'deliverable' }] })
    await browser.tabs.finalize({ keep: [{ tab: claimed.info, status: 'handoff' }] })
    await expect(browser.tabs.finalize({ keep: true })).rejects.toThrow('requires keep to be an array')
    await expect(browser.tabs.finalize({ keep: [claimed] })).rejects.toThrow('keep status must be')

    expect(bridge.requests.map((request) => request.method)).toContain('tabs.finalize')
    expect(bridge.requests.find((request) => request.method === 'tab.contentText')?.params).toMatchObject({
      maxLength: 5
    })
    expect(bridge.requests.find((request) => request.method === 'tab.waitForLoadState')?.params).toMatchObject({
      state: 'domcontentloaded',
      options: { state: 'domcontentloaded', timeoutMs: 1234 }
    })
    expect(bridge.requests.filter((request) => request.method === 'tab.waitForLoadState')[1]?.params).toMatchObject({
      state: 'load',
      options: { timeoutMs: 2345 }
    })
    expect(bridge.requests.find((request) => request.method === 'tab.waitForURL')?.params).toMatchObject({
      url: 'https://example.test/next',
      options: { timeoutMs: 3456 }
    })
    expect(bridge.requests.find((request) => request.method === 'tab.goto')?.params).toMatchObject({
      url: 'https://example.test/next',
      options: {}
    })
    expect(bridge.requests.find((request) => request.method === 'tab.evaluate')?.params).toMatchObject({
      arg: { answer: 42 },
      maxBytes: 256,
      timeoutMs: 1234
    })
    expect(bridge.requests.find((request) => request.method === 'tab.evaluate')).toMatchObject({
      kind: 'command',
      commandId: expect.stringMatching(/^chrome-command-/),
      browserSession,
      timeoutMs: 1234
    })
    expect(bridge.requests.find((request) => request.method === 'tab.waitForNavigation')?.params).toMatchObject({
      previousUrl: 'https://example.test/',
      options: { timeoutMs: 4567, waitUntil: 'load' }
    })
    expect(bridge.requests.filter((request) => request.method === 'tab.waitForURL')[1]?.params).toMatchObject({
      url: 'https://example.test/target',
      options: { url: 'https://example.test/target', timeoutMs: 5678 }
    })
    expect(bridge.requests.filter((request) => request.method === 'tabs.finalize')[0]?.params).toEqual({
      keep: [{
        status: 'deliverable',
        tab: { id: 7, tabId: 7, windowId: 1, title: 'Example', url: 'https://example.test/', active: true, index: 0, claimed: false, loading: false }
      }]
    })
    expect(bridge.requests.filter((request) => request.method === 'tabs.finalize')[1]?.params).toEqual({
      keep: [
        {
          status: 'handoff',
          tab: { id: 7, tabId: 7, windowId: 1, title: 'Example', url: 'https://example.test/', active: true, index: 0, claimed: false, loading: false }
        }
      ]
    })
    expect(bridge.requests.find((request) => request.method === 'tab.waitForFileChooser')?.params).toMatchObject({
      options: { timeoutMs: 4567 }
    })
    expect(bridge.requests.find((request) => request.method === 'tab.fileChooserIsMultiple')?.params).toMatchObject({
      fileChooser: { selector: '[data-dotcraft-file-chooser-id="abc"]' }
    })
    expect(bridge.requests.find((request) => request.method === 'tab.fileChooserSetFiles')?.params).toMatchObject({
      fileChooser: { selector: '[data-dotcraft-file-chooser-id="abc"]' },
      files: ['C:\\tmp\\a.txt', 'C:\\tmp\\b.txt']
    })
    expect(bridge.requests.map((request) => request.method)).toContain('cua.action')
    expect(bridge.requests.map((request) => request.method)).toContain('domCua.visibleDom')
    expect(bridge.requests.map((request) => request.method)).toContain('domCua.action')
  })

  it('reports a helpful bridge recovery error when Chrome is not connected', async () => {
    const { setupBrowserRuntime } = await importChromeClient()
    const globals: Record<string, unknown> = {}
    const agent = await setupBrowserRuntime({ globals, backend: 'extension', chromeHost: { pipePaths: [createPipePath()], timeoutMs: 50, discoveryTimeoutMs: 50 } })

    await expect(agent.browsers.get('extension')).rejects.toThrow(
      'Chrome extension backend is not connected'
    )
  })

  it('returns its own Chrome registry and leaves existing user bindings intact', async () => {
    const bridge = createMockBridge(() => [])
    bridges.push(bridge)
    const pipePath = await bridge.listen()
    const { setupBrowserRuntime } = await importChromeClient()
    const iabBrowser = { kind: 'iab' }
    const globals: Record<string, any> = {
      agent: {
        browsers: {
          list: async () => [{ id: 'iab', name: 'DotCraft Browser', type: 'iab' }],
          get: async (id: string) => {
            if (id === 'iab') return iabBrowser
            throw new Error(`Browser not found: ${id}. Available browser id: iab.`)
          },
          describeApi: () => ['get("iab")']
        }
      }
    }

    const agent = await setupBrowserRuntime({ globals, backend: 'extension', chromeHost: { pipePaths: [pipePath] } })

    expect(await globals.agent.browsers.list()).toEqual([
      { id: 'iab', name: 'DotCraft Browser', type: 'iab' }
    ])
    expect(await globals.agent.browsers.get('iab')).toBe(iabBrowser)
    expect(await agent.browsers.list()).toEqual([{ id: 'extension', name: 'DotCraft Chrome', type: 'extension' }])
    await expect(agent.browsers.get('extension')).resolves.toBeTruthy()
    await expect(agent.browsers.get('chrome')).resolves.toBeTruthy()
  })

  it('classifies bridge request timeouts as command timeouts', async () => {
    const tab = { id: 7, tabId: 7, windowId: 1, title: 'Example', url: 'https://example.test/' }
    const bridge = createMockBridge((request) => {
      if (request.method === 'tabs.selected') return tab
      if (request.method === 'tab.evaluate') return new Promise(() => undefined)
      throw new Error(`unexpected method ${request.method}`)
    })
    bridges.push(bridge)
    const pipePath = await bridge.listen()
    const { setupBrowserRuntime } = await importChromeClient()
    const browserSession = {
      protocolVersion: 1,
      sessionId: 'thread-1',
      threadId: 'thread-1',
      turnId: 'turn-1',
      evaluationId: 'eval-1'
    }
    const globals: Record<string, unknown> = { dotcraft: { browserSession } }

    const agent = await setupBrowserRuntime({ globals, backend: 'extension', chromeHost: { pipePaths: [pipePath], timeoutMs: 500 } })
    const browser = await agent.browsers.get('extension')
    const selected = await browser.tabs.selected()

    await expect(selected.evaluate('return true;', null, { timeoutMs: 5 })).rejects.toThrow(
      'CommandTimeout: Chrome bridge request timed out: tab.evaluate after 5ms.'
    )
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(bridge.cancels[0]).toMatchObject({
      kind: 'cancel',
      commandId: bridge.requests.find((request) => request.method === 'tab.evaluate')?.commandId,
      reason: 'timeout'
    })
  })

  it('rejects oversized evaluate results on the client side', async () => {
    const tab = { id: 7, tabId: 7, windowId: 1, title: 'Example', url: 'https://example.test/' }
    const bridge = createMockBridge((request) => {
      if (request.method === 'tabs.selected') return tab
      if (request.method === 'tab.evaluate') return 'x'.repeat(100)
      throw new Error(`unexpected method ${request.method}`)
    })
    bridges.push(bridge)
    const pipePath = await bridge.listen()
    const { setupBrowserRuntime } = await importChromeClient()
    const browserSession = {
      protocolVersion: 1,
      sessionId: 'thread-1',
      threadId: 'thread-1',
      turnId: 'turn-1',
      evaluationId: 'eval-1'
    }
    const globals: Record<string, unknown> = { dotcraft: { browserSession } }

    const agent = await setupBrowserRuntime({ globals, backend: 'extension', chromeHost: { pipePaths: [pipePath] } })
    const browser = await agent.browsers.get('extension')
    const selected = await browser.tabs.selected()

    await expect(selected.evaluate('return "large";', null, { maxBytes: 10 })).rejects.toThrow('ResultTooLarge')
  })
})
