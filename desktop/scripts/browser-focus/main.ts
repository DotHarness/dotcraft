import { app, BrowserWindow, type WebContents, type WebFrameMain } from 'electron'
import assert from 'node:assert/strict'
import { once } from 'node:events'
import { appendFileSync, readFileSync } from 'node:fs'
import { createServer } from 'node:http'
import { createRequire } from 'node:module'
import { join } from 'node:path'
import { withAutomationInput } from '../../src/main/browserAutomationInput'
import { browserFocusIsolation } from '../../src/main/browserFocusIsolation'

const config = JSON.parse(readFileSync(join(__dirname, 'config.json'), 'utf8')) as {
  directory: string
  logFile: string
}
const log = (value: unknown): void => appendFileSync(config.logFile, JSON.stringify(value) + '\n')
const require = createRequire(import.meta.url)
app.setPath('userData', join(config.directory, 'profile'))
app.commandLine.appendSwitch('host-resolver-rules', 'MAP localhost 127.0.0.1')
app.commandLine.appendSwitch('no-proxy-server')
app.commandLine.appendSwitch('site-per-process')

interface ObservedEvent { type: string; target: string; key?: string; data?: string }
interface PageState { active: string; values: Record<string, string>; events: ObservedEvent[] }
const observation = `
window.events=[];
for(const type of ['blur','keydown','click','compositionstart','compositionupdate','compositionend']) {
  document.addEventListener(type,e=>events.push({type,target:e.target.id,key:e.key,data:e.data}),true);
}
window.state=()=>({active:document.activeElement.id,events,values:Object.fromEntries(
  [...document.querySelectorAll('input,[contenteditable]')].map(e=>[e.id,e.value??e.textContent]))});
`
const server = createServer((request, response) => {
  response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' })
  const port = (server.address() as { port: number }).port
  const content = request.url === '/host'
    ? '<div id="composer" contenteditable style="height:90px">HOST</div>'
    : request.url === '/frame'
      ? '<input id="frameTarget" value="FRAME"><input id="frameOther" value="OTHER">'
      : '<input id="target" value="PAGE"><input id="other" value="OTHER">' +
        '<iframe id="child" src="http://localhost:' + port + '/frame" style="display:block;width:400px;height:140px"></iframe>'
  response.end('<!doctype html><html><body>' + content + '<script>' + observation + '</script></body></html>')
})
let owner: BrowserWindow
const host = <T = unknown>(script: string): Promise<T> => owner.webContents.executeJavaScript(script)
const evaluate = <T = unknown>(page: WebContents | WebFrameMain, script: string): Promise<T> => page.executeJavaScript(script)
const state = (page: WebContents | WebFrameMain): Promise<PageState> => evaluate(page, 'state()')
const settle = (): Promise<void> => host('new Promise(resolve=>setTimeout(resolve,50))')
const raw = (page: WebContents, method: string, params: Record<string, unknown>): Promise<unknown> =>
  page.debugger.sendCommand(method, params)
const input = (page: WebContents, method: string, params: Record<string, unknown>): Promise<unknown> => {
  browserFocusIsolation.arm(page)
  return withAutomationInput(page, () => raw(page, method, params))
}

function child(page: WebContents): WebFrameMain {
  const frame = page.mainFrame.frames.find(frame => frame.url.endsWith('/frame'))
  assert(frame, 'Expected the cross-site iframe')
  return frame
}

async function prepare(page: WebContents, composition = false, frame = false): Promise<void> {
  browserFocusIsolation.arm(page)
  await evaluate(page, "document.querySelector('#target').value='PAGE';document.querySelector('#other').value='OTHER';document.querySelector('#target').focus()")
  await evaluate(child(page), "document.querySelector('#frameTarget').value='FRAME';document.querySelector('#frameOther').value='OTHER'")
  if (frame) {
    await evaluate(page, "document.querySelector('#child').focus()")
    await evaluate(child(page), "document.querySelector('#frameTarget').focus()")
  }
  owner.webContents.focus()
  await host("document.querySelector('#composer').focus();document.querySelector('#composer').textContent='HOST'")
  await raw(owner.webContents, 'Input.imeSetComposition', { text: '', selectionStart: 0, selectionEnd: 0 })
  await settle()
  await Promise.all([host('events=[]'), evaluate(page, 'events=[]'), evaluate(child(page), 'events=[]')])
  if (composition) {
    await raw(owner.webContents, 'Input.imeSetComposition', { text: 'ni', selectionStart: 2, selectionEnd: 2 })
    assert((await host<PageState>('state()')).events.some(event => event.type === 'compositionstart'))
  }
}

async function key(page: WebContents, value: 'a' | 'Escape'): Promise<void> {
  const text = value === 'a'
  const params = { key: value, code: text ? 'KeyA' : 'Escape', windowsVirtualKeyCode: text ? 65 : 27 }
  await input(page, 'Input.dispatchKeyEvent', { ...params, type: text ? 'keyDown' : 'rawKeyDown', text: text ? 'a' : '' })
  await input(page, 'Input.dispatchKeyEvent', { ...params, type: 'keyUp' })
}

async function click(page: WebContents, inFrame = false, isolated = true): Promise<void> {
  const selector = inFrame ? '#frameOther' : '#other'
  const position = await evaluate<{ x: number; y: number }>(inFrame ? child(page) : page,
    `(()=>{const r=document.querySelector('${selector}').getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2}})()`)
  if (inFrame) {
    const offset = await evaluate<{ x: number; y: number }>(page, "(()=>{const r=document.querySelector('#child').getBoundingClientRect();return{x:r.x+2,y:r.y+2}})()")
    position.x += offset.x
    position.y += offset.y
  }
  const dispatch = isolated ? input : raw
  await dispatch(page, 'Input.dispatchMouseEvent', { type: 'mousePressed', button: 'left', buttons: 1, clickCount: 1, ...position })
  await dispatch(page, 'Input.dispatchMouseEvent', { type: 'mouseReleased', button: 'left', buttons: 0, clickCount: 1, ...position })
}

async function assertComposer(composition = false): Promise<void> {
  await settle()
  const current = await host<PageState>('state()')
  assert.equal(current.active, 'composer')
  assert(!current.events.some(event => event.type === 'blur' && event.target === 'composer'))
  assert(!current.events.some(event => event.type === 'keydown' || event.type === 'compositionend'))
  if (!composition) {
    assert.equal(current.values.composer, 'HOST')
    return
  }
  const length = current.events.length
  await raw(owner.webContents, 'Input.imeSetComposition', { text: 'nihao', selectionStart: 5, selectionEnd: 5 })
  const continuation = (await host<PageState>('state()')).events.slice(length)
  assert(continuation.some(event => event.type === 'compositionupdate' && event.data === 'nihao'))
  assert(!continuation.some(event => event.type === 'compositionstart' || event.type === 'compositionend'))
}

async function main(): Promise<void> {
  await app.whenReady()
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve))
  const port = (server.address() as { port: number }).port
  owner = new BrowserWindow({ width: 1000, height: 800, show: false, webPreferences: {
    webviewTag: true, nodeIntegration: false, contextIsolation: true, sandbox: true, backgroundThrottling: false
  } })
  const attached = new Promise<WebContents>(resolve => {
    owner.webContents.once('did-attach-webview', (_event, page) => {
      page.setBackgroundThrottling(false)
      page.once('did-finish-load', () => resolve(page))
    })
  })
  await owner.loadURL('http://127.0.0.1:' + port + '/host')
  await host(`(()=>{const w=document.createElement('webview');w.style.cssText='display:flex;width:900px;height:450px';w.src='http://127.0.0.1:${port}/page';document.body.append(w)})()`)
  const page = await attached
  owner.show()
  owner.focus()
  owner.webContents.debugger.attach('1.3')
  page.debugger.attach('1.3')
  await raw(page, 'Emulation.setFocusEmulationEnabled', { enabled: true })

  await prepare(page)
  await key(page, 'Escape')
  await key(page, 'a')
  await input(page, 'Input.insertText', { text: 'INSERTED' })
  await assertComposer()
  const guest = await state(page)
  assert.deepEqual(guest.events.filter(event => event.type === 'keydown').map(event => event.key), ['Escape', 'a'])
  assert(guest.values.target.includes('a') && guest.values.target.includes('INSERTED'))
  log({ status: 'pass', name: 'guest keyboard and inserted text stay out of composer' })

  await prepare(page, true)
  await click(page)
  await key(page, 'a')
  await key(page, 'Escape')
  await assertComposer(true)
  const clicked = await state(page)
  assert.equal(clicked.values.other.replace('a', ''), 'OTHER')
  assert(clicked.events.some(event => event.type === 'click' && event.target === 'other'))
  assert(clicked.events.some(event => event.type === 'keydown' && event.key === 'Escape'))
  log({ status: 'pass', name: 'click and follow-up input preserve composition continuity' })

  assert.notEqual(page.mainFrame.processId, child(page).processId)
  await prepare(page, true, true)
  await key(page, 'Escape')
  await key(page, 'a')
  await assertComposer(true)
  const frame = await state(child(page))
  assert.deepEqual(frame.events.filter(event => event.type === 'keydown').map(event => event.key), ['Escape', 'a'])
  assert.equal(frame.values.frameTarget.replace('a', ''), 'FRAME')
  await prepare(page, true)
  await click(page, true)
  await key(page, 'a')
  await assertComposer(true)
  const frameClick = await state(child(page))
  assert.equal(frameClick.values.frameOther.replace('a', ''), 'OTHER')
  assert(frameClick.events.some(event => event.type === 'click' && event.target === 'frameOther'))
  log({ status: 'pass', name: 'root CDP input follows the cross-process iframe' })

  await prepare(page, true)
  browserFocusIsolation.release(page)
  await click(page, false, false)
  await settle()
  const released = await host<PageState>('state()')
  assert(released.events.some(event => event.type === 'blur' && event.target === 'composer'))
  assert(released.events.some(event => event.type === 'compositionend'))
  await prepare(page, true)
  await click(page)
  await assertComposer(true)
  assert((await state(page)).events.some(event => event.type === 'click' && event.target === 'other'))
  log({ status: 'pass', name: 'deactivation releases focus and automated input rearms isolation' })

  const native = require(join(app.getAppPath(), 'resources', 'native', 'browser-focus.node')) as { diagnostics(): Record<string, number> }
  const identity = { processId: page.mainFrame.processId, routingId: page.mainFrame.routingId }
  await page.loadURL('http://localhost:' + port + '/page')
  assert.notDeepEqual({ processId: page.mainFrame.processId, routingId: page.mainFrame.routingId }, identity)
  await evaluate(page, "document.querySelector('#target').focus()")
  await input(page, 'Input.insertText', { text: 'NAVIGATED' })
  assert((await state(page)).values.target.includes('NAVIGATED'))
  const before = native.diagnostics()
  const destroyed = once(page, 'destroyed')
  page.close({ waitForBeforeUnload: false })
  await destroyed
  await settle()
  const diagnostics = native.diagnostics()
  assert.equal(diagnostics.registeredGuests, before.registeredGuests - 1)
  assert.equal(diagnostics.explicitUnregisters, before.explicitUnregisters + 1)
  assert(diagnostics.focusedWidgetLocal > 0 && diagnostics.pageWidgetLocal > 0)
  assert.equal(diagnostics.wrongThreadCalls, 0)
  log({ status: 'pass', name: 'destruction releases the latest frame identity after navigation' })
  log({ status: 'complete', composition: 'Chromium Input.imeSetComposition', diagnostics })
}

function finish(code: number): void {
  owner?.destroy()
  server.close()
  app.exit(code)
}

process.on('uncaughtException', error => { log({ status: 'failed', error: error.stack }); finish(1) })
void main().then(() => finish(0)).catch(error => { log({ status: 'failed', error: String(error.stack ?? error) }); finish(1) })
