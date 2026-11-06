import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { createServer } from 'node:http'
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { build } from 'esbuild'

const require = createRequire(import.meta.url)
const desktop = fileURLToPath(new URL('..', import.meta.url))
const directory = await mkdtemp(join(tmpdir(), 'dotcraft-panels-electron-'))
const server = createServer((request, response) => {
  response.setHeader('content-type', 'text/html')
  response.end(`<title>${request.url}</title><input id="draft"><p>${request.url}</p>`)
})
try {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
  const base = `http://127.0.0.1:${server.address().port}`
  const renderer = await build({ entryPoints: [join(desktop, 'src/renderer/browser/browserGuestHost.ts')],
    bundle: true, format: 'iife', globalName: 'guestHost', write: false })
  const css = await readFile(join(desktop, 'src/renderer/browser/browser-guest.css'), 'utf8')
  await writeFile(join(directory, 'owner.html'), `<html><head><style>${css}</style></head><body><script>${renderer.outputFiles[0].text}
    guestHost.startBrowserGuestHost(window.testBrowserHost);</script></body></html>`)
  await writeFile(join(directory, 'preload.cjs'), `const {contextBridge,ipcRenderer}=require('electron');
    contextBridge.exposeInMainWorld('testBrowserHost',{
      list:()=>ipcRenderer.invoke('test:list'),cursorArrived:p=>ipcRenderer.invoke('test:cursor',p),
      onEvent:cb=>{const fn=(_e,p)=>cb(p);ipcRenderer.on('viewer:browser:host-event',fn);
        return()=>ipcRenderer.removeListener('viewer:browser:host-event',fn)}
    });`)
  const main = `
    import assert from 'node:assert/strict'
    import { once } from 'node:events'
    import { randomUUID } from 'node:crypto'
    import { join } from 'node:path'
    import { app, BrowserWindow, ipcMain, protocol } from 'electron'
    import { ViewerBrowserManager } from ${JSON.stringify(join(desktop, 'src/main/viewerBrowser.ts'))}
    import { BrowserPageSnapshotStore } from ${JSON.stringify(join(desktop, 'src/main/browserPageSnapshotStore.ts'))}
    import { ThreadPanelStateStore } from ${JSON.stringify(join(desktop, 'src/main/threadPanelStateStore.ts'))}
    const directory=${JSON.stringify(directory)}, base=${JSON.stringify(base)}
    protocol.registerSchemesAsPrivileged(['dotcraft-viewer','dotcraft-plugin'].map(scheme=>({scheme,privileges:{standard:true,secure:true,supportFetchAPI:true,stream:true}})))
    app.setPath('userData',join(directory,'profile'))
    app.on('window-all-closed',()=>{})
    process.on('uncaughtException',error=>{console.error(error);app.exit(1)})
    void (async()=>{
      await app.whenReady()
      const manager=new ViewerBrowserManager({snapshotStore:new BrowserPageSnapshotStore(join(directory,'pages.json'))})
      const panels=new ThreadPanelStateStore(join(directory,'panels.json'))
      const scope={projectKey:'/workspace/a',threadId:'a'}
      const makeWindow=async()=>{
        const win=new BrowserWindow({show:false,webPreferences:{preload:join(directory,'preload.cjs'),
          contextIsolation:true,sandbox:true,webviewTag:true}})
        manager.hosts.attachWindow(win)
        await win.loadFile(join(directory,'owner.html'))
        return win
      }
      ipcMain.handle('test:list',event=>manager.hosts.list(BrowserWindow.fromWebContents(event.sender)))
      ipcMain.handle('test:cursor',(event,p)=>manager.hosts.cursors.arrived(BrowserWindow.fromWebContents(event.sender),p.tabId,p.moveSequence))
      const win=await makeWindow()
      manager.registerPersistenceOwner(win)
      const params={tabId:'tab-a',threadId:'a',projectKey:scope.projectKey,workspacePath:'/workspace/a',initialUrl:base+'/one'}
      const phase=process.argv[2]
      if(phase==='save'){
        const created=await manager.createTab(win,params)
        const page=manager.getTabWebContents(win,params.tabId)
        await page.loadURL(base+'/one')
        if(page.isLoading()) await once(page,'did-stop-loading')
        await page.executeJavaScript('window.runtimeSentinel=42;document.querySelector("input").value="draft"')
        manager.setAutomationState(win,{tabId:params.tabId,active:true})
        manager.setForegroundScope(win,{projectKey:'/workspace/b',threadId:'b'})
        manager.setVisible(win,{tabId:params.tabId,visible:false})
        assert.equal(manager.suspendTab(win,params.tabId),false,'active Agent page must not suspend')
        manager.setForegroundScope(win,scope)
        assert.equal((await manager.createTab(win,{...params,browserStorageId:created.browserStorageId})).browserStorageId,created.browserStorageId)
        assert.equal(manager.getTabWebContents(win,params.tabId).id,page.id)
        assert.equal(await page.executeJavaScript('runtimeSentinel'),42)
        assert.equal(await page.executeJavaScript('document.querySelector("input").value'),'draft')
        console.log('PASS A/B/A retains active webContents and DOM state')
        await page.loadURL(base+'/two')
        if(page.isLoading()) await once(page,'did-stop-loading')
        manager.setAutomationState(win,{tabId:params.tabId,active:false})
        manager.setVisible(win,{tabId:params.tabId,visible:false})
        const before=page.id
        assert.equal(manager.suspendTab(win,params.tabId),true,'ordinary page must suspend after automation release')
        assert.equal(manager.getTabWebContents(win,params.tabId),null)
        await manager.createTab(win,{...params,browserStorageId:created.browserStorageId})
        const restored=manager.getTabWebContents(win,params.tabId)
        assert.notEqual(restored.id,before)
        assert.equal(restored.getURL(),base+'/two')
        assert.equal(restored.navigationHistory.canGoBack(),true)
        console.log('PASS ordinary page suspension preserves navigation history')
        const other=await makeWindow()
        const freshStorageId='browser:'+randomUUID()
        const fresh=await manager.createTab(other,{tabId:'secondary-fresh',threadId:'b',projectKey:'/workspace/b',
          workspacePath:'/workspace/b',browserStorageId:freshStorageId,restore:'none',initialUrl:base+'/secondary'})
        const freshPage=manager.getTabWebContents(other,'secondary-fresh')
        if(freshPage.isLoading()) await once(freshPage,'did-stop-loading')
        assert.equal(fresh.browserStorageId,freshStorageId)
        assert.equal(freshPage.getURL(),base+'/secondary')
        assert.notEqual(freshPage.id,restored.id)
        assert.equal(manager.snapshotState(win,params.tabId).browserStorageId,created.browserStorageId)
        console.log('PASS secondary window creates an independent fresh page with a renderer storage identity')
        assert.equal(manager.restoreState(other,{...params,tabs:[{tabId:params.tabId,browserStorageId:created.browserStorageId} ]})[0].status,'owned-by-another-window')
        manager.destroyAllTabs(other);other.destroy()
        panels.update(scope,{version:1,tabs:[{id:params.tabId,kind:'browser',label:'Saved',target:params.tabId,
          browserStorageId:created.browserStorageId,currentUrl:base+'/two',loading:false,canGoBack:true,canGoForward:false}],
          systemTabs:['plan'],activeTab:{kind:'viewer',id:params.tabId},visible:true,fullView:true,width:650})
        manager.destroyAllTabs(win)
        console.log('PASS snapshot ownership and checkpoint saved')
      }else{
        const checkpoint=panels.read(scope)
        assert.equal(checkpoint.fullView,true);assert.equal(checkpoint.width,650)
        const tab=checkpoint.tabs[0]
        const result=manager.restoreState(win,{...params,tabs:[{tabId:tab.id,browserStorageId:tab.browserStorageId}]})
        assert.equal(result[0].status,'snapshot-ready')
        assert.equal(manager.getTabWebContents(win,tab.id),null,'restore metadata must not eagerly create a page')
        await manager.createTab(win,{...params,browserStorageId:tab.browserStorageId,restore:'required',initialUrl:tab.currentUrl})
        const page=manager.getTabWebContents(win,tab.id)
        assert.equal(page.getURL(),base+'/two')
        assert.equal(page.navigationHistory.canGoBack(),true)
        const navigated=once(page,'did-finish-load');page.navigationHistory.goBack();await navigated
        assert.equal(page.getURL(),base+'/one')
        console.log('PASS process restart lazily restores panel and Chromium back/forward history')
        manager.destroyTab(win,tab.id)
        assert.equal(manager.restoreState(win,{...params,tabs:[{tabId:tab.id,browserStorageId:tab.browserStorageId}]})[0].status,'missing')
        console.log('PASS explicit close removes page snapshot')
      }
      win.destroy();app.exit(0)
    })().catch(error=>{console.error(error);app.exit(1)})
  `
  const entry = join(directory, 'main.cjs')
  await build({ stdin: { contents: main, resolveDir: desktop, loader: 'ts', sourcefile: 'thread-panels-validation.ts' },
    bundle: true, platform: 'node', format: 'cjs', external: ['electron'], outfile: entry })
  for (const phase of ['save', 'restore']) {
    const env = { ...process.env }; delete env.ELECTRON_RUN_AS_NODE
    const child = spawn(require('electron'), [entry, phase], { env, windowsHide: true, stdio: 'inherit' })
    const timeout = setTimeout(() => child.kill(), 60000)
    const code = await new Promise((resolve, reject) => { child.once('error', reject); child.once('exit', resolve) })
      .finally(() => clearTimeout(timeout))
    assert.equal(code, 0, `Electron panel lifecycle validation failed: ${phase}`)
  }
} finally {
  server.close()
  await rm(directory, { recursive: true, force: true, maxRetries: 3, retryDelay: 200 })
}
