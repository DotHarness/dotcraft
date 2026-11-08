import { createServer } from 'node:http'
import { lstat, mkdir, readFile, realpath, writeFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { pathToFileURL } from 'node:url'

const MAX_FRAGMENT_BYTES = 1_000_000
const FRAGMENT = '<!--__VISUALIZATION_FRAGMENT__-->'
const templatePath = new URL('../assets/preview.html', import.meta.url)
const escape = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')

function previewHost() {
  const frame = document.querySelector('iframe')
  const status = document.querySelector('output')
  let picker = null
  const post = (method, params) => frame.contentWindow.postMessage({ method, params: { ...params, viewId: 'preview' } }, '*')
  const reply = (id, result, error) => post('visualization/result', { id, ok: !error, result, error })
  const theme = matchMedia('(prefers-color-scheme: dark)')
  const context = () => {
    post('visualization/theme', { theme: theme.matches ? 'dark' : 'light' })
    post('visualization/context', {
      locale: navigator.language,
      timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
      reducedMotion: matchMedia('(prefers-reduced-motion: reduce)').matches,
      pointer: matchMedia('(pointer: coarse)').matches ? 'coarse' : 'fine',
      width: frame.clientWidth
    })
  }
  const choose = (params, color) => {
    if (picker) return reply(params.id, undefined, 'Another picker is open')
    const dialog = document.createElement('dialog')
    picker = dialog
    const form = document.createElement('form')
    form.method = 'dialog'
    const label = document.createElement('label')
    label.textContent = params.label || 'Choose a value'
    const input = document.createElement(color ? 'input' : 'select')
    if (color) {
      input.type = 'color'
      input.value = params.color
    } else {
      for (const [index, entry] of params.options.entries()) {
        const option = new Option(entry.label, String(index), false, index === params.selectedIndex)
        option.disabled = entry.disabled
        input.append(option)
      }
    }
    label.append(input)
    form.append(label)
    for (const [text, value] of [['Apply', 'apply'], ['Cancel', 'cancel']]) {
      const button = document.createElement('button')
      button.textContent = text
      button.value = value
      form.append(button)
    }
    dialog.append(form)
    document.body.append(dialog)
    dialog.addEventListener('close', () => {
      if (dialog.returnValue === 'apply') reply(params.id, color ? { color: input.value } : { index: Number(input.value) })
      else reply(params.id, undefined, 'Cancelled')
      dialog.remove()
      picker = null
    }, { once: true })
    dialog.showModal()
  }
  addEventListener('message', event => {
    if (event.source !== frame.contentWindow || event.data?.params?.viewId !== 'preview') return
    const { method, params } = event.data
    if (method === 'visualization/ready') context()
    else if (method === 'visualization/resize' && Number.isFinite(params.height)) frame.style.height = Math.min(10_000, Math.max(120, params.height)) + 'px'
    else if (method === 'visualization/error') {
      status.textContent = String(params.message).slice(0, 1000)
      status.hidden = false
    } else if (method === 'visualization/setWidgetState') reply(params.id)
    else if (method === 'visualization/requestDisplayMode') reply(params.id, { mode: 'inline' })
    else if (method === 'visualization/selectMenu') choose(params, false)
    else if (method === 'visualization/pickColor') choose(params, true)
    else if (params.id) reply(params.id, undefined, 'This action is unavailable in a standalone preview')
  })
  theme.addEventListener('change', context)
  new ResizeObserver(context).observe(frame)
}

export async function render(source) {
  const metadata = await lstat(source)
  if (!metadata.isFile() || metadata.isSymbolicLink()) throw new Error('The fragment must be a regular file')
  if (metadata.size > MAX_FRAGMENT_BYTES) throw new Error('The fragment exceeds the 1 MB size limit')
  const fragment = await readFile(source, 'utf8')
  const template = await readFile(templatePath, 'utf8')
  const frame = template.replace(FRAGMENT, () => fragment)
  const policy = /http-equiv="Content-Security-Policy" content="([^"]+)"/.exec(template)[1].replace("frame-src 'none'", "frame-src 'self'")
  const shell = previewHost.toString().replaceAll('</script', '<\\/script')
  return `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="referrer" content="no-referrer">
<meta http-equiv="Content-Security-Policy" content="${policy}">
<title>Visualization preview</title>
<style>:root{color-scheme:light dark;background:light-dark(rgb(255 255 255),rgb(24 24 24))}html,body{margin:0}body{padding:1rem}iframe{display:block;width:100%;max-width:736px;height:260px;margin:0 auto;border:0}output{display:block;max-width:736px;margin:1rem auto}output[hidden]{display:none}dialog form{display:grid;gap:1rem}dialog label{display:grid;gap:.5rem}</style>
</head><body><output hidden role="status"></output><iframe sandbox="allow-scripts" referrerpolicy="no-referrer" title="Visualization preview" srcdoc="${escape(frame)}"></iframe><script>(${shell})()</script></body></html>`
}

export async function serve(source, port = 0) {
  await render(source)
  const server = createServer(async (request, response) => {
    if (request.url !== '/' && request.url !== '/index.html') {
      response.writeHead(404).end()
      return
    }
    try {
      const document = await render(source)
      response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' }).end(document)
    } catch (error) {
      response.writeHead(500, { 'Content-Type': 'text/plain; charset=utf-8' }).end(error.message)
    }
  })
  await new Promise((resolve, reject) => {
    server.once('error', reject)
    server.listen(port, '127.0.0.1', resolve)
  })
  return server
}

async function main() {
  const args = process.argv.slice(2)
  const source = args.shift()
  if (!source || source === '--help') {
    process.stdout.write('Usage: node render.mjs <fragment-path> [destination.html] [--serve] [--port <number>] [--force]\n')
    return
  }
  let destination
  let port = 0
  let serving = false
  let force = false
  while (args.length) {
    const value = args.shift()
    if (value === '--serve') serving = true
    else if (value === '--force') force = true
    else if (value === '--port') {
      port = Number(args.shift())
      if (!Number.isInteger(port) || port < 0 || port > 65535) throw new Error('Port must be between 0 and 65535')
    } else if (value.startsWith('--') || destination) throw new Error(`Unexpected argument: ${value}`)
    else destination = resolve(value)
  }
  if (destination) {
    if (resolve(source) === destination || await realpath(destination).catch(() => destination) === await realpath(source)) throw new Error('Source and destination must be different files')
    const document = await render(source)
    if ((await lstat(destination).catch(() => null))?.isSymbolicLink()) throw new Error('The destination cannot be a symbolic link')
    await mkdir(dirname(destination), { recursive: true })
    await writeFile(destination, document, { flag: force ? 'w' : 'wx' })
  }
  if (serving) {
    const server = await serve(source, port)
    process.stdout.write(`http://127.0.0.1:${server.address().port}/\n`)
  } else if (!destination) process.stdout.write(await render(source))
}

if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) {
  main().catch(error => { process.stderr.write(`${error.message}\n`); process.exitCode = 1 })
}
