import { readFile } from 'node:fs/promises'
import { afterEach, describe, expect, it, vi } from 'vitest'

async function importPopup() {
  const source = await readFile(
    new URL('../../../resources/plugins/dotcraft-bundled/plugins/chrome/extension/popup.js', import.meta.url),
    'utf8'
  )
  return await import(`data:text/javascript;base64,${Buffer.from(source, 'utf8').toString('base64')}`)
}

function createRoot() {
  const classes = new Set<string>(['is-loading'])
  const pill = {
    classList: {
      add: (...names: string[]) => names.forEach((name) => classes.add(name)),
      remove: (...names: string[]) => names.forEach((name) => classes.delete(name))
    }
  }
  const label = { textContent: '' }
  const message = { textContent: '' }
  const version = { textContent: '' }
  const elements = new Map<string, unknown>([
    ['[data-status-pill]', pill],
    ['[data-status-label]', label],
    ['[data-status-message]', message],
    ['[data-version]', version]
  ])
  return {
    classes,
    label,
    message,
    version,
    root: {
      querySelector(selector: string) {
        return elements.get(selector) ?? null
      }
    }
  }
}

describe('Chrome extension popup', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.resetModules()
  })

  it('never shows native pipe paths in connected or disconnected status text', async () => {
    const { statusViewModel } = await importPopup()
    const connected = statusViewModel({
      connected: true,
      bridgeReady: true,
      pipePath: '\\\\.\\pipe\\dotcraft-chrome-123-secret',
      version: '0.1.0'
    })
    const disconnected = statusViewModel({
      connected: false,
      bridgeReady: false,
      error: 'Failed to open \\\\.\\pipe\\dotcraft-chrome-123-secret'
    })

    expect(connected.message).toBe('Chrome backend ready. Control Chrome with DotCraft.')
    expect(JSON.stringify(connected)).not.toContain('dotcraft-chrome-123-secret')
    expect(disconnected.message).toContain('[Chrome backend pipe]')
    expect(disconnected.message).not.toContain('dotcraft-chrome-123-secret')
  })

  it('asks the service worker for status and to open settings', async () => {
    const sendMessage = vi.fn((_message, callback) => {
      callback({
        ok: true,
        status: { connected: true, bridgeReady: true, version: '0.1.0' }
      })
    })
    vi.stubGlobal('chrome', {
      runtime: {
        sendMessage,
        lastError: null,
        getManifest: () => ({ version: '0.1.0' })
      }
    })

    const { refreshStatus, openSettings } = await importPopup()
    await refreshStatus(createRoot().root)
    await openSettings(createRoot().root)

    expect(sendMessage.mock.calls.map(([message]) => message)).toEqual([
      { type: 'dotcraft-popup-status' },
      { type: 'dotcraft-popup-open-settings' }
    ])
  })
})
