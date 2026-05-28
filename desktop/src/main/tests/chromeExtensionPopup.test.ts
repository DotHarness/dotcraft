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

  it('renders connected and disconnected status view models', async () => {
    const { statusViewModel } = await importPopup()

    expect(statusViewModel({ connected: true, bridgeReady: true, version: '0.1.0' })).toMatchObject({
      label: 'Connected',
      className: 'is-connected',
      message: 'Chrome backend ready. Control Chrome with DotCraft.',
      version: '0.1.0'
    })
    expect(statusViewModel({ connected: false, bridgeReady: false })).toMatchObject({
      label: 'Disconnected',
      className: 'is-disconnected',
      message: 'Click the extension icon to start the DotCraft Chrome backend, then refresh status in DotCraft settings.'
    })
  })

  it('does not render native pipe paths in connected popup text', async () => {
    const { statusViewModel } = await importPopup()
    const view = statusViewModel({
      connected: true,
      bridgeReady: true,
      pipePath: '\\\\.\\pipe\\dotcraft-chrome-123-secret',
      version: '0.1.0'
    })

    expect(view.message).toBe('Chrome backend ready. Control Chrome with DotCraft.')
    expect(JSON.stringify(view)).not.toContain('dotcraft-chrome-123-secret')
  })

  it('redacts backend pipe paths from disconnected popup errors', async () => {
    const { statusViewModel } = await importPopup()
    const view = statusViewModel({
      connected: false,
      bridgeReady: false,
      error: 'Failed to open \\\\.\\pipe\\dotcraft-chrome-123-secret'
    })

    expect(view.message).toContain('[Chrome backend pipe]')
    expect(view.message).not.toContain('dotcraft-chrome-123-secret')
  })

  it('asks the service worker for status when refreshed', async () => {
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

    const { refreshStatus } = await importPopup()
    const view = createRoot()
    await refreshStatus(view.root)

    expect(sendMessage).toHaveBeenCalledWith({ type: 'dotcraft-popup-status' }, expect.any(Function))
    expect(view.classes.has('is-connected')).toBe(true)
    expect(view.label.textContent).toBe('Connected')
    expect(view.message.textContent).toBe('Chrome backend ready. Control Chrome with DotCraft.')
    expect(view.version.textContent).toBe('Version 0.1.0')
  })

  it('sends an open settings request through the service worker', async () => {
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

    const { openSettings } = await importPopup()
    await openSettings(createRoot().root)

    expect(sendMessage).toHaveBeenCalledWith({ type: 'dotcraft-popup-open-settings' }, expect.any(Function))
  })
})
