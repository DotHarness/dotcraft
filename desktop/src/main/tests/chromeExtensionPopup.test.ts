import { readFile } from 'node:fs/promises'
import { afterEach, expect, it, vi } from 'vitest'

async function importPopup() {
  const source = await readFile(
    new URL('../../../resources/plugins/dotcraft-bundled/plugins/chrome/extension/popup.js', import.meta.url),
    'utf8'
  )
  return await import(`data:text/javascript;base64,${Buffer.from(source, 'utf8').toString('base64')}`)
}

afterEach(() => {
  vi.unstubAllGlobals()
})

it('reports connection problems without native pipe paths', async () => {
  vi.stubGlobal('chrome', { runtime: { getManifest: () => ({ version: '0.2.0' }) } })
  const { statusViewModel } = await importPopup()
  const pipeError = 'Failed to open \\\\.\\pipe\\dotcraft-chrome-123-secret'

  for (const state of ['reconnecting', 'disconnected']) {
    const view = statusViewModel({ state, connected: false, error: pipeError })
    expect(view.message).toContain('[Chrome backend pipe]')
    expect(view.message).not.toContain('dotcraft-chrome-123-secret')
  }
})
