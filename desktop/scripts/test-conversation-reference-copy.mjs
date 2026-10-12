import assert from 'node:assert/strict'
import { mkdtemp, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { _electron as electron } from 'playwright'
import { createServer } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// jsdom does not implement Chromium's layout-aware selection/clipboard serialization.
const desktop = fileURLToPath(new URL('..', import.meta.url))
const directory = await mkdtemp(join(tmpdir(), 'dotcraft-reference-copy-'))
const cases = [
  { content: 'Before [source.ts](source.ts:12) after', expected: 'Before source.ts after' },
  { content: '前文 [源码](source.ts:12-20) 后文', expected: '前文 源码 后文' },
  { content: 'Before [docs](https://example.com/docs) after', expected: 'Before docs after' },
  { content: '[first](first.ts:1) and [second](second.ts:2)', expected: 'first and second' },
  { content: 'Before [**source**](source.ts#L12) after', expected: 'Before source after' },
  { content: 'Before [source.ts](source.ts:12) after', scope: 'label', expected: 'source.ts' },
  { content: 'First [source.ts](source.ts:12).\n\nSecond paragraph.', expected: 'First source.ts.\n\nSecond paragraph.' },
  { content: 'Before [source.ts](source.ts:12)\n\nafter', expected: 'Before source.ts\n\nafter' },
  { content: 'Before [source.ts](source.ts:12) after', scope: 'partial', expected: 'Before sou' }
]
const entry = `
import React from 'react'
import { createRoot } from 'react-dom/client'
import { flushSync } from 'react-dom'
import { MarkdownRenderer } from '/src/renderer/components/conversation/MarkdownRenderer.tsx'
import { LocaleProvider } from '/src/renderer/contexts/LocaleContext.tsx'
import { useConversationStore } from '/src/renderer/stores/conversationStore.ts'
import { useThreadStore } from '/src/renderer/stores/threadStore.ts'
import { ensureVscodeIcons } from '/src/renderer/utils/fileTypeIcons.ts'
import '/src/renderer/styles/index.css'
useConversationStore.setState({ workspacePath: 'E:/workspace' })
useThreadStore.setState({ activeThreadId: 'reference-copy' })
await ensureVscodeIcons()
const root = createRoot(document.getElementById('root'))
window.renderReference = (content) => flushSync(() => root.render(
  React.createElement(LocaleProvider, { loadSettings: false },
    React.createElement('div', { className: 'selectable' },
      React.createElement(MarkdownRenderer, { content, enableMermaid: false })))
))
window.fixtureReady = true
`
let server
let app
try {
  server = await createServer({
    configFile: false,
    root: desktop,
    cacheDir: join(directory, 'vite-cache'),
    plugins: [react({ exclude: [/node_modules/, /vite-cache/] }), tailwindcss({ optimize: false }), {
      name: 'reference-copy-fixture',
      resolveId(id) { if (id === '/reference-copy.js') return '\0reference-copy.js' },
      load(id) { if (id === '\0reference-copy.js') return entry },
      configureServer(server) {
        server.middlewares.use('/reference-copy.html', async (_request, response) => {
          response.setHeader('Content-Type', 'text/html')
          response.end(await server.transformIndexHtml('/reference-copy.html',
            '<html><body><div id="root"></div><script type="module" src="/reference-copy.js"></script></body></html>'))
        })
      }
    }],
    server: { host: '127.0.0.1', port: 0, hmr: false, watch: null }
  })
  await server.listen()
  const url = server.resolvedUrls.local[0] + 'reference-copy.html'
  const main = join(directory, 'main.cjs')
  await writeFile(main, `
const { app, BrowserWindow } = require('electron')
app.setPath('userData', ${JSON.stringify(join(directory, 'profile'))})
app.whenReady().then(() => {
  const window = new BrowserWindow({ width: 1000, height: 800, show: false,
    webPreferences: { contextIsolation: true, sandbox: true, backgroundThrottling: false } })
  window.loadURL(${JSON.stringify(url)})
})
`, 'utf8')
  const env = { ...process.env }
  delete env.ELECTRON_RUN_AS_NODE
  app = await electron.launch({ args: [main], env })
  const page = await app.firstWindow()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.waitForFunction(() => window.fixtureReady)
  console.log('Electron/Chromium ' + await app.evaluate(() => `${process.versions.electron}/${process.versions.chrome}`))
  for (const [index, test] of cases.entries()) {
    await page.evaluate(content => window.renderReference(content), test.content)
    await page.evaluate(scope => {
      const body = document.querySelector('.markdown-body')
      const label = body.querySelector('a > span')
      const range = document.createRange()
      if (scope === 'partial') {
        range.setStart(body.querySelector('p').firstChild, 0)
        range.setEnd(label.firstChild, 3)
      } else {
        range.selectNodeContents(scope === 'label' ? label : body)
      }
      const selection = window.getSelection()
      selection.removeAllRanges()
      selection.addRange(range)
    }, test.scope)
    await app.evaluate(({ BrowserWindow, clipboard }) => {
      clipboard.writeText('copy did not run')
      const page = BrowserWindow.getAllWindows()[0].webContents
      page.focus()
      page.copy()
    })
    const copied = await app.evaluate(async ({ clipboard }) => {
      const deadline = Date.now() + 2000
      while (clipboard.readText() === 'copy did not run' && Date.now() < deadline) {
        await new Promise(resolve => setTimeout(resolve, 10))
      }
      return clipboard.readText()
    })
    assert.equal(copied.replace(/\r\n/g, '\n'), test.expected, `Case ${index + 1}: ${test.content}`)
    console.log(`PASS ${index + 1}: ${JSON.stringify(copied)}`)
  }
  assert.deepEqual(errors, [], 'Renderer errors')
} finally {
  await app?.close()
  await server?.close()
  await rm(directory, { recursive: true, force: true, maxRetries: 3, retryDelay: 200 })
}
