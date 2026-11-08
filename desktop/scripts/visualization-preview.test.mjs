import assert from 'node:assert/strict'
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import test from 'node:test'
import { render, serve } from '../../src/DotCraft.Core/Skills/BuiltIn/visualize/scripts/render.mjs'

const script = fileURLToPath(new URL('../../src/DotCraft.Core/Skills/BuiltIn/visualize/scripts/render.mjs', import.meta.url))

async function fixture(t, content) {
  const directory = await mkdtemp(join(tmpdir(), 'dotcraft-visualization-preview-'))
  t.after(() => rm(directory, { recursive: true, force: true }))
  const source = join(directory, 'fragment.html')
  await writeFile(source, content)
  return { directory, source }
}

test('renders literal fragment text in a sandbox without interpreting replacement tokens', async t => {
  const fragment = '<div data-test="literal">$& &amp; &lt;/iframe&gt;</div><script>window.example = 1</script>'
  const { source } = await fixture(t, fragment)
  const document = await render(source)
  assert.match(document, /sandbox="allow-scripts"/)
  assert.doesNotMatch(document, /allow-same-origin/)
  const encoded = /srcdoc="([^"]*)"/.exec(document)[1]
  const decoded = encoded.replaceAll('&quot;', '"').replaceAll('&lt;', '<').replaceAll('&gt;', '>').replaceAll('&amp;', '&')
  assert.ok(decoded.includes(fragment))
  assert.match(decoded, /connect-src 'none'/)
})

test('rejects oversized input using the runtime byte limit', async t => {
  const { source } = await fixture(t, 'x'.repeat(1_000_001))
  await assert.rejects(render(source), /1 MB size limit/)
})

test('serves only the current fragment document and reflects edits', async t => {
  const { source } = await fixture(t, '<div>first-state</div>')
  const server = await serve(source)
  t.after(() => new Promise(resolve => server.close(resolve)))
  const url = `http://127.0.0.1:${server.address().port}`
  const first = await fetch(url)
  assert.equal(first.headers.get('cache-control'), 'no-store')
  assert.ok((await first.text()).includes('first-state'))
  await writeFile(source, '<div>second-state</div>')
  assert.ok((await (await fetch(url)).text()).includes('second-state'))
  assert.equal((await fetch(url + '/fragment.html')).status, 404)
})

test('CLI writes a standalone document without overwriting its source or existing output', async t => {
  const { directory, source } = await fixture(t, '<div>source-content</div>')
  const destination = join(directory, 'preview.html')
  const run = args => spawnSync(process.execPath, [script, ...args], { cwd: directory, encoding: 'utf8' })
  assert.equal(run(['fragment.html', 'preview.html']).status, 0)
  assert.ok((await readFile(destination, 'utf8')).includes('source-content'))
  assert.notEqual(run([source, destination]).status, 0)
  assert.notEqual(run([source, source, '--force']).status, 0)
  assert.equal(await readFile(source, 'utf8'), '<div>source-content</div>')
})
