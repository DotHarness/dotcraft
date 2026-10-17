import { createReplWorkerFixture } from './helpers/replWorkerFixture'
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { EventEmitter } from 'node:events'
import { createServer, type Server } from 'node:http'
import type { AddressInfo } from 'node:net'
import WebSocket from 'ws'
import { chromium, type Browser, type Page } from 'playwright-core'

vi.mock('electron', () => ({
  app: { getAppPath: () => process.cwd() }, BrowserWindow: { getAllWindows: () => [] },
  WebContentsView: vi.fn(), session: { fromPartition: vi.fn() }, shell: {}, nativeImage: {}
}))

import { BrowserUseManager } from '../browserUseManager'
import { NodeReplManager } from '../nodeReplManager'

const workerFixture = createReplWorkerFixture()

async function serve(body: string): Promise<{ server: Server; port: number }> {
  const server = createServer((_request, response) => {
    response.writeHead(200, { 'content-type': 'text/html' })
    response.end(body)
  })
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve))
  return { server, port: (server.address() as AddressInfo).port }
}

async function freePort(): Promise<number> {
  const { server, port } = await serve('')
  await new Promise(resolve => server.close(resolve))
  return port
}

describe('bundled browser client with cross-origin frames', () => {
  const servers: Server[] = []
  let browser: Browser
  let page: Page
  let socket: WebSocket
  let manager: BrowserUseManager
  let repl: NodeReplManager
  let owner: Electron.BrowserWindow
  let currentId = ''

  beforeAll(async () => {
    const crossSite = await serve(`<p>Cross site text</p><button onclick="this.textContent='Paid'">Pay now</button><button onclick="this.textContent='Confirmed'">Confirm</button>`)
    const sameSite = await serve(`<p>Same site text</p><label for="code">Code</label><input id="code">`)
    const scaled = await serve(`<button style="margin-left:200px;width:120px" onclick="this.textContent='Scaled done'">Scaled action</button>`)
    const far = await serve(`<button onclick="this.textContent='Far done'">Far action</button><button onclick="this.textContent='Far second done'">Far second</button>`)
    const parent = await serve(`<title>Frames fixture</title><h1>Parent heading</h1>
      <iframe id="cross" style="margin:40px" src="http://localhost:${crossSite.port}/"></iframe>
      <iframe id="samesite" src="http://127.0.0.1:${sameSite.port}/"></iframe>
      <iframe id="scaled" style="width:400px;height:200px;transform:scale(0.5);transform-origin:0 0" src="http://localhost:${scaled.port}/"></iframe>
      <iframe id="far" style="display:block;margin-top:2000px" src="http://localhost:${far.port}/"></iframe>`)
    servers.push(crossSite.server, sameSite.server, scaled.server, far.server, parent.server)
    const debuggingPort = await freePort()
    browser = await chromium.launch({ headless: true, args: [`--remote-debugging-port=${debuggingPort}`, '--site-per-process'] })
    page = await browser.newPage()
    const parentUrl = `http://127.0.0.1:${parent.port}/`
    await page.goto(parentUrl)
    await page.frameLocator('#cross').getByRole('button', { name: 'Confirm' }).waitFor()
    await page.frameLocator('#samesite').getByLabel('Code').waitFor()
    await page.frameLocator('#scaled').getByRole('button').waitFor()
    await page.frameLocator('#far').getByRole('button', { name: 'Far second' }).waitFor({ state: 'attached' })

    const targets = await (await fetch(`http://127.0.0.1:${debuggingPort}/json/list`)).json() as Array<{ type: string; url: string; webSocketDebuggerUrl: string }>
    expect(targets.filter(target => target.type === 'iframe').map(target => target.url).sort())
      .toEqual([crossSite.port, scaled.port, far.port].map(port => `http://localhost:${port}/`).sort())
    socket = new WebSocket(targets.find(target => target.type === 'page' && target.url === parentUrl)!.webSocketDebuggerUrl)
    await new Promise(resolve => socket.once('open', resolve))
    const pending = new Map<number, { resolve(value: unknown): void; reject(error: Error): void }>()
    const debug = new EventEmitter()
    socket.on('message', data => {
      const message = JSON.parse(String(data))
      if (message.id == null) {
        debug.emit('message', {}, message.method, message.params, message.sessionId ?? '')
        return
      }
      const entry = pending.get(message.id)
      pending.delete(message.id)
      if (message.error) entry?.reject(new Error(message.error.message))
      else entry?.resolve(message.result)
    })
    let nextId = 0
    const sendCommand = (method: string, params: Record<string, unknown> = {}, sessionId?: string) =>
      new Promise((resolve, reject) => {
        const id = ++nextId
        pending.set(id, { resolve, reject })
        socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }))
      })

    const contents = Object.assign(new EventEmitter(), {
      isDestroyed: () => page.isClosed(), getURL: () => page.url(), getTitle: () => 'Frames fixture',
      isLoading: () => false, stop: () => {},
      executeJavaScript: (source: string) => page.evaluate(source),
      debugger: Object.assign(debug, { isAttached: () => true, attach: () => {}, detach: () => {}, sendCommand })
    })
    owner = Object.assign(new EventEmitter(), { id: 101, isDestroyed: () => false, getTitle: () => 'Test', webContents: { isDestroyed: () => false, send: () => {} } }) as unknown as Electron.BrowserWindow
    manager = new BrowserUseManager({
      setCaptureSurface: () => {},
      createAutomationTab: (_win: unknown, params: { tabId: string }) => { currentId = params.tabId },
      getTabWebContents: () => contents as unknown as Electron.WebContents,
      loadAutomationUrl: async () => {}, destroyTab: () => {},
      snapshotState: () => ({ tabId: currentId, currentUrl: page.url(), title: 'Frames fixture', loading: false }),
      setAutomationState: () => {},
      moveMouse: async (_win: unknown, params: any) => page.mouse.move(params.x, params.y)
    } as any)
    repl = new NodeReplManager(manager, workerFixture.fork)
  }, 30000)

  afterAll(async () => {
    await repl?.disposeAll()
    workerFixture.dispose()
    await manager?.closeBackendForTests()
    socket?.close()
    await browser?.close()
    for (const server of servers) server.close()
  })

  async function evaluate(code: string): Promise<any> {
    const result = await repl.evaluate(owner, {
      threadId: 'frames', workspacePath: process.cwd(), code,
      browserSession: { sessionId: 'frames', threadId: 'frames', turnId: 'turn-frames', backendId: 'iab' }
    })
    expect(result.error, result.error).toBeUndefined()
    return result.resultText ? JSON.parse(result.resultText) : undefined
  }

  it('reads cross-origin frames into the DOM snapshot in page coordinates', async () => {
    const snapshot = await evaluate(`
      const { setupBrowserRuntime } = await import(dotcraft.browserClientPath);
      const agent = await setupBrowserRuntime();
      const browser = await agent.browsers.get('iab');
      let tab = await browser.tabs.new();
      await tab.playwright.domSnapshot();
    `)
    expect(snapshot.bodyText).toContain('Cross site text')
    expect(snapshot.bodyText).toContain('Same site text')
    expect(snapshot.accessibilitySnapshot).toContain('Pay now')
    expect(snapshot.accessibilitySnapshot).toContain('[frameSelectors=["#cross"]]')
    expect(snapshot.accessibilitySnapshot).toContain('[frameSelectors=["#samesite"]]')
    expect(snapshot.elements.find((element: any) => element.name === 'Code').frameSelectors).toEqual(['#samesite'])
    expect(snapshot.elements.find((element: any) => element.name === 'Parent heading').frameSelectors).toBeUndefined()
    const pay = snapshot.elements.find((element: any) => element.name === 'Pay now')
    expect(pay.frameSelectors).toEqual(['#cross'])
    const frame = (await page.locator('#cross').boundingBox())!
    expect(pay.boundingBox.x).toBeGreaterThanOrEqual(frame.x)
    expect(pay.boundingBox.y).toBeGreaterThanOrEqual(frame.y)
    expect(pay.boundingBox.x + pay.boundingBox.width).toBeLessThanOrEqual(frame.x + frame.width)
  })

  it('acts inside cross-origin frames through locators and DOM-CUA ids', async () => {
    await evaluate(`
      const before = JSON.parse(await tab.playwright.domSnapshot()).elements;
      const scope = (name) => before.find(node => node.name === name).frameSelectors.reduce((frame, selector) => frame.frameLocator(selector), tab.playwright);
      await scope('Code').getByLabel('Code').fill('42');
      await scope('Confirm').getByRole('button', { name: 'Confirm' }).click();
      const nodes = JSON.parse(await tab.playwright.domSnapshot()).elements;
      await tab.dom_cua.click({ node_id: nodes.find(node => node.name === 'Pay now').ref });
      true;
    `)
    expect(await page.frameLocator('#samesite').getByLabel('Code').inputValue()).toBe('42')
    expect(await page.frameLocator('#cross').getByRole('button').first().innerText()).toBe('Paid')
    expect(await page.frameLocator('#cross').getByRole('button').last().innerText()).toBe('Confirmed')
    expect(await evaluate(`JSON.stringify(await tab.playwright.frameLocator('#cross').getByRole('button', { name: 'Paid' }).innerText())`)).toBe('Paid')
  })

  it('maps scaled and scrolled frames at action time', async () => {
    const result = await evaluate(`
      const elements = JSON.parse(await tab.playwright.domSnapshot()).elements;
      const scaledBox = elements.find(node => node.name === 'Scaled action').boundingBox;
      await tab.playwright.frameLocator('#scaled').getByRole('button', { name: 'Scaled action' }).click();
      await tab.playwright.frameLocator('#far').getByRole('button', { name: 'Far action' }).click();
      JSON.stringify({ scaledBox, farSecond: elements.find(node => node.name === 'Far second').ref });
    `)
    expect(result.scaledBox.width).toBeCloseTo(60, 0)
    expect(await page.frameLocator('#scaled').getByRole('button').innerText()).toBe('Scaled done')
    expect(await page.frameLocator('#far').getByRole('button').first().innerText()).toBe('Far done')
    await page.evaluate(() => window.scrollTo(0, 0))
    await evaluate(`await tab.dom_cua.click({ node_id: ${JSON.stringify(result.farSecond)} }); true`)
    expect(await page.frameLocator('#far').getByRole('button').last().innerText()).toBe('Far second done')
  })

  it('keeps node ids per document and retires them when a frame loads a new document', async () => {
    const payNow = `JSON.stringify(JSON.parse(await tab.playwright.domSnapshot()).elements.find(node => node.name === 'Paid' || node.name === 'Pay now').ref)`
    const before = await evaluate(payNow)
    expect(await evaluate(payNow)).toBe(before)
    await page.locator('#cross').evaluate((frame: any) => { frame.src = `${frame.src}?v=2` })
    await page.frameLocator('#cross').getByRole('button', { name: 'Pay now' }).waitFor()
    const after = await evaluate(payNow)
    expect(after).not.toBe(before)
    const stale = await evaluate(`
      let message = '';
      try { await tab.dom_cua.click({ node_id: ${JSON.stringify(before)} }); } catch (error) { message = String(error); }
      JSON.stringify(message);
    `)
    expect(stale).toContain('NodeStale')
  })
})
