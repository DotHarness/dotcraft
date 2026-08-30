#!/usr/bin/env node
/**
 * Serves a sample's `desktop/preview/` folder so the design harnesses can be opened in
 * a browser. A plain `file://` open does not work: the harness loads its bundle and the
 * built stylesheet as separate files.
 *
 * Run: node sdk/typescript/samples/desktop-plugins/preview-server.mjs grok-mascot [port]
 */
import { createServer } from 'node:http'
import { readFile } from 'node:fs/promises'
import { dirname, extname, join, normalize } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const sample = process.argv[2]
const port = Number(process.argv[3] ?? 4180)

if (sample === undefined) {
  console.error('Usage: node preview-server.mjs <sample-id> [port]')
  process.exit(1)
}

const root = join(here, sample, 'desktop', 'preview')
const types = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.webp': 'image/webp',
  '.jpg': 'image/jpeg'
}

createServer(async (request, response) => {
  const path = decodeURIComponent((request.url ?? '/').split('?')[0])
  const file = join(root, normalize(path === '/' ? '/index.html' : path))
  try {
    const body = await readFile(file)
    response.writeHead(200, { 'content-type': types[extname(file)] ?? 'application/octet-stream' })
    response.end(body)
  } catch {
    response.writeHead(404)
    response.end('not found')
  }
}).listen(port, () => {
  console.log(`${sample} preview on http://localhost:${port}`)
})
