import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { closeSync, openSync } from 'node:fs'
import { copyFile, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { build } from 'esbuild'

if (process.platform !== 'win32' || process.arch !== 'x64') {
  throw new Error('Browser focus acceptance requires Windows x64.')
}

const require = createRequire(import.meta.url)
const desktop = fileURLToPath(new URL('..', import.meta.url))
const directory = await mkdtemp(join(tmpdir(), 'dotcraft-browser-focus-'))
const logFile = join(directory, 'acceptance.log')
const outputFile = join(directory, 'electron.log')
let output

try {
  const entry = join(directory, 'main.cjs')
  await writeFile(join(directory, 'config.json'), JSON.stringify({ directory, logFile }))
  await mkdir(join(directory, 'resources', 'native'), { recursive: true })
  await copyFile(join(desktop, 'resources', 'native', 'browser-focus.node'), join(directory, 'resources', 'native', 'browser-focus.node'))
  await build({
    entryPoints: [fileURLToPath(new URL('./browser-focus/main.ts', import.meta.url))],
    bundle: true,
    platform: 'node',
    format: 'cjs',
    external: ['electron'],
    outfile: entry,
    define: { 'import.meta.url': '__bundleUrl' },
    banner: { js: "const __bundleUrl = require('node:url').pathToFileURL(__filename).href;" }
  })
  const env = { ...process.env }
  delete env.ELECTRON_RUN_AS_NODE
  output = openSync(outputFile, 'w')
  const child = spawn(require('electron'), [entry], { env, windowsHide: true, stdio: ['ignore', output, output] })
  const stop = () => {
    if (child.pid && child.exitCode === null) {
      spawn('taskkill', ['/pid', String(child.pid), '/t', '/f'], { windowsHide: true, stdio: 'ignore' })
    }
  }
  const timer = setTimeout(stop, 60_000)
  process.once('SIGINT', stop)
  process.once('SIGTERM', stop)
  let code
  try {
    code = await new Promise((accept, reject) => {
      child.once('error', reject)
      child.once('exit', accept)
    })
  } finally {
    clearTimeout(timer)
    process.removeListener('SIGINT', stop)
    process.removeListener('SIGTERM', stop)
  }
  const report = await readFile(logFile, 'utf8').catch(() => '')
  process.stdout.write(report)
  if (code !== 0) process.stderr.write(await readFile(outputFile, 'utf8'))
  assert.equal(code, 0, 'Browser focus acceptance failed')
  assert.equal(JSON.parse(report.trim().split('\n').at(-1)).status, 'complete')
} finally {
  if (output !== undefined) closeSync(output)
  await rm(directory, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 })
}
