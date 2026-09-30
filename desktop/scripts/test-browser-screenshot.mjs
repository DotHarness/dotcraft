import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { build } from 'esbuild'

const require = createRequire(import.meta.url)
const desktop = fileURLToPath(new URL('..', import.meta.url))
const directory = await mkdtemp(join(tmpdir(), 'dotcraft-screenshot-'))
const pageUrl = process.argv[2]
const entry = join(directory, 'main.cjs')
const screenshotModule = join(desktop, 'src/main/browserScreenshot.ts')
const guestModule = join(desktop, 'src/main/browserGuestRegistry.ts')
const renderer = await build({
  entryPoints: [join(desktop, 'src/renderer/browser/browserGuestHost.ts')],
  bundle: true, format: 'iife', globalName: 'guestHost', write: false
})
const css = await readFile(join(desktop, 'src/renderer/browser/browser-guest.css'), 'utf8')
await writeFile(join(directory, 'owner.html'), `<html><head><style>${css}</style></head><body>
<input id="focus" value="owner"><script>${renderer.outputFiles[0].text}
guestHost.startBrowserGuestHost(window.testBrowserHost);document.getElementById('focus').focus();
</script></body></html>`)
await writeFile(join(directory, 'preload.cjs'), `const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('testBrowserHost',{
 list:()=>ipcRenderer.invoke('test:list'),bind:p=>ipcRenderer.invoke('test:bind',p),failed:p=>ipcRenderer.invoke('test:failed',p),
 onEvent:callback=>{const listener=(_event,p)=>callback(p);ipcRenderer.on('viewer:browser:host-event',listener);return()=>ipcRenderer.removeListener('viewer:browser:host-event',listener)}
});`)

const main = `
import { app, BrowserWindow, ipcMain, nativeImage } from 'electron'
import assert from 'node:assert/strict'
import { createServer } from 'node:http'
import { join } from 'node:path'
import { BrowserScreenshot } from ${JSON.stringify(screenshotModule)}
import { BrowserGuestRegistry } from ${JSON.stringify(guestModule)}
void (async () => {
app.setPath('userData', join(${JSON.stringify(directory)}, 'profile'))
app.on('window-all-closed', () => {})
const server = createServer((_request, response) => {
  response.setHeader('content-type', 'text/html')
  response.end('<html><head><style>html,body{margin:0}::-webkit-scrollbar{display:none}section{height:600px}#top{background:#ff0000}#middle{background:#00ff00}#bottom{background:#0000ff}</style></head><body><section id="top"></section><section id="middle"></section><section id="bottom"></section></body></html>')
})
let owner
const hosts = new BrowserGuestRegistry()
const captures = new BrowserScreenshot()
const viewport = { width: 800, height: 600 }
const dimensions = async page => await page.executeJavaScript('({width:innerWidth,height:innerHeight})')
const image = data => {
  assert.ok(data.startsWith('iVBOR'), 'Screenshot must remain PNG')
  const value = nativeImage.createFromBuffer(Buffer.from(data, 'base64'))
  assert.ok(!value.isEmpty(), 'Screenshot is empty')
  return value
}
const color = (value, y) => {
  const size = value.getSize()
  const bytes = value.toBitmap({scaleFactor:1})
  const offset = (y * size.width + Math.floor(size.width / 2)) * 4
  return [...bytes.subarray(offset, offset + 3)]
}
try {
  await app.whenReady()
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
  owner = new BrowserWindow({width:1000,height:800,show:false,webPreferences:{preload:join(${JSON.stringify(directory)},'preload.cjs'),contextIsolation:true,sandbox:true,webviewTag:true}})
  hosts.attachWindow(owner)
  ipcMain.handle('test:list', () => hosts.list(owner))
  ipcMain.handle('test:bind', (_event, params) => hosts.bind(owner, params.tabId, params.webContentsId))
  ipcMain.handle('test:failed', (_event, params) => { throw new Error(params.message) })
  const ready = hosts.request(owner, 'tab', 'screenshot-validation', viewport)
  await owner.loadFile(join(${JSON.stringify(directory)}, 'owner.html'))
  const page = await ready
  await page.loadURL('http://127.0.0.1:' + server.address().port)
  page.debugger.attach('1.3')
  const context = {
    tabId:'tab',page,viewport,timeoutMs:10000,
    send:(method,params)=>page.debugger.sendCommand(method,params),
    setSurface:size=>hosts.setCaptureSurface(owner,'tab',size),
    diagnostic:message=>console.log(message)
  }
  const screenshot = options => captures.screenshot(context, options)
  owner.showInactive()
  const focusedElement = await owner.webContents.executeJavaScript('document.activeElement.id')
  for (const scenario of ['hidden', 'visible-unfocused', 'owner-hidden']) {
    hosts.update(owner, 'tab', { visible: scenario === 'visible-unfocused' })
    if (scenario === 'owner-hidden') owner.hide()
    const value = image(await screenshot())
    assert.equal(owner.isFocused(), false)
    assert.deepEqual(value.getSize(), viewport)
    assert.deepEqual(color(value, 100), [0, 0, 255])
    assert.equal(hosts.list(owner)[0].captureSurfaceSize, undefined)
    assert.deepEqual(await dimensions(page), viewport)
    assert.equal(await owner.webContents.executeJavaScript('document.activeElement.id'), focusedElement)
    console.log('PASS viewport ' + scenario)
  }
  const full = image(await screenshot({fullPage:true}))
  assert.deepEqual(full.getSize(), {width:800,height:1800})
  assert.deepEqual(color(full, 100), [0, 0, 255])
  assert.deepEqual(color(full, 1700), [255, 0, 0])
  const crop = image(await screenshot({clip:{x:0,y:1200,width:800,height:600}}))
  assert.deepEqual(crop.getSize(), viewport)
  assert.deepEqual(color(crop, 100), [255, 0, 0])
  const raw = image((await captures.captureCdp(context,{format:'png',captureBeyondViewport:true,clip:{x:0,y:0,width:800,height:1800,scale:1}})).data)
  assert.deepEqual(raw.getSize(), {width:800,height:1800})
  assert.deepEqual(color(raw, 1700), [255, 0, 0])
  // Host updates cross IPC; wait for restored native guest dimensions before asserting.
  for (let i=0;i<100;i++) { if ((await dimensions(page)).height===600) break; await new Promise(resolve=>setTimeout(resolve,16)) }
  assert.deepEqual(await dimensions(page), viewport)
  assert.deepEqual(hosts.list(owner)[0].bounds, {x:0,y:0,...viewport})
  assert.equal(hosts.list(owner)[0].visible, false)
  console.log('PASS full-page, bottom crop, raw CDP, and restoration')
  if (${JSON.stringify(pageUrl ?? null)}) {
    await page.loadURL(${JSON.stringify(pageUrl ?? '')})
    image(await screenshot())
    image(await screenshot({fullPage:true}))
    console.log('PASS supplied page viewport and full-page')
  }
  hosts.update(owner,'tab',{automation:false,visible:false})
  assert.equal(page.getBackgroundThrottling(),true)
  console.log('PASS idle throttling restored')
  page.debugger.detach()
  hosts.clear(owner)
  owner.destroy()
  app.exit(0)
} catch(error) {
  console.error(error)
  app.exit(1)
} finally { server.close() }
})()
`

try {
  await build({ stdin: { contents: main, resolveDir: desktop, sourcefile: 'screenshot-validation.ts', loader: 'ts' }, bundle: true, platform: 'node', format: 'cjs', external: ['electron'], outfile: entry })
  const env = { ...process.env }
  delete env.ELECTRON_RUN_AS_NODE
  const child = spawn(require('electron'), [entry], { env, windowsHide: true, stdio: 'inherit' })
  const timer = setTimeout(() => child.kill(), 60_000)
  const code = await new Promise((accept, reject) => {
    child.once('error', reject)
    child.once('exit', accept)
  }).finally(() => clearTimeout(timer))
  assert.equal(code, 0, 'Electron screenshot validation failed')
} finally {
  assert.ok(resolve(directory).startsWith(resolve(tmpdir()) + '\\') || resolve(directory).startsWith(resolve(tmpdir()) + '/'))
  await rm(directory, { recursive: true, force: true, maxRetries: 3, retryDelay: 200 })
}
