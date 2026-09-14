import { EventEmitter } from 'node:events'
import { JSDOM } from 'jsdom'
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { DownloadItem, WebContents } from 'electron'
import { BrowserDownloads } from '../browserDownloads'
import { BrowserPageControls } from '../browserPageControls'
import { capturePageReference } from '../browserPageSelection'
import { selectionScript } from '../../shared/viewer/browserSelectionScript'

const menu = vi.hoisted(() => ({ buildFromTemplate: vi.fn(() => ({ popup: vi.fn() })) }))
vi.mock('electron', () => ({ Menu: menu, clipboard: { writeText: vi.fn() }, shell: { openExternal: vi.fn() } }))

const directories: string[] = []
afterEach(() => { for (const directory of directories.splice(0)) rmSync(directory, { recursive: true, force: true }) })

function download() {
  return Object.assign(new EventEmitter(), {
    getFilename: () => 'report.txt', getURL: () => 'https://example.com/report',
    getTotalBytes: () => 100, getReceivedBytes: () => 60, setSavePath: vi.fn(), cancel: vi.fn()
  })
}

describe('browser downloads', () => {
  it('numbers collisions, preserves origins and progress, and opens only completed records', async () => {
    const directory = mkdtempSync(join(tmpdir(), 'browser-download-'))
    directories.push(directory)
    writeFileSync(join(directory, 'report.txt'), 'existing')
    const open = vi.fn(async () => '')
    const recordsPath = join(directory, 'records.json')
    const manager = new BrowserDownloads(directory, recordsPath, vi.fn(), open)
    const first = download()
    const second = download()
    manager.start(first as unknown as DownloadItem, { tabId: 'a', threadId: 'task-a' })
    manager.start(second as unknown as DownloadItem, { tabId: 'b', threadId: 'task-b' })
    expect(first.setSavePath).toHaveBeenCalledWith(join(directory, 'report (1).txt'))
    expect(second.setSavePath).toHaveBeenCalledWith(join(directory, 'report (2).txt'))
    first.emit('updated', {}, 'progressing')
    const record = manager.snapshot().find((item) => item.tabId === 'a')!
    expect(record).toMatchObject({ threadId: 'task-a', receivedBytes: 60, totalBytes: 100 })
    await expect(manager.open(record.id)).rejects.toThrow('not complete')
    first.emit('done', {}, 'completed')
    await manager.open(record.id)
    expect(open).toHaveBeenCalledWith(record.path)
    manager.cancel(manager.snapshot()[0].id)
    expect(second.cancel).toHaveBeenCalledOnce()
    const restored = new BrowserDownloads(directory, recordsPath, vi.fn(), open)
    expect(restored.snapshot().map((item) => item.state)).toEqual(['interrupted', 'completed'])
    expect(JSON.parse(readFileSync(recordsPath, 'utf8'))[0].state).toBe('interrupted')
  })

  it('persists the chosen directory and clears history without deleting files or active transfers', async () => {
    const directory = mkdtempSync(join(tmpdir(), 'browser-download-'))
    directories.push(directory)
    const recordsPath = join(directory, 'records.json')
    const manager = new BrowserDownloads(directory, recordsPath, vi.fn(), vi.fn(async () => ''))
    const first = download()
    manager.start(first as unknown as DownloadItem, { tabId: 'a' })
    const original = manager.snapshot()[0]
    writeFileSync(original.path, 'downloaded')
    first.emit('done', {}, 'completed')
    manager.setLocation(join(directory, 'chosen'))
    const second = download()
    manager.start(second as unknown as DownloadItem, { tabId: 'b' })
    expect(second.setSavePath).toHaveBeenCalledWith(join(directory, 'chosen', 'report.txt'))
    expect(manager.snapshot().find(record => record.id === original.id)?.path).toBe(original.path)
    manager.remove()
    expect(manager.snapshot()).toHaveLength(1)
    expect(second.cancel).not.toHaveBeenCalled()
    expect(readFileSync(original.path, 'utf8')).toBe('downloaded')
    const restored = new BrowserDownloads(directory, recordsPath, vi.fn(), vi.fn())
    expect(restored.location()).toBe(join(directory, 'chosen'))
    expect(restored.snapshot()[0].state).toBe('interrupted')
    restored.remove(restored.snapshot()[0].id)
    expect(restored.snapshot()).toEqual([])
  })

  it('records cancellation and unknown total size', () => {
    const directory = mkdtempSync(join(tmpdir(), 'browser-download-'))
    directories.push(directory)
    const manager = new BrowserDownloads(directory, join(directory, 'records.json'), vi.fn(), vi.fn())
    const item = download()
    item.getTotalBytes = () => 0
    manager.start(item as unknown as DownloadItem, { tabId: 'a' })
    item.emit('done', {}, 'cancelled')
    expect(manager.snapshot()[0]).toMatchObject({ state: 'cancelled', totalBytes: 0 })
  })
})

function page() {
  return Object.assign(new EventEmitter(), {
    findInPage: vi.fn().mockReturnValueOnce(1).mockReturnValue(2), stopFindInPage: vi.fn(),
    getZoomFactor: vi.fn(() => 1.5), setZoomFactor: vi.fn(), inspectElement: vi.fn(),
    getURL: vi.fn(() => 'https://example.com/a'), getTitle: () => 'Original page',
    executeJavaScript: vi.fn(), isDestroyed: () => false,
    capturePage: vi.fn(async () => ({ toDataURL: () => 'data:image/png;base64,AA==' }))
  })
}

describe('page controls', () => {
  it('ignores stale find results and routes native keyboard input', () => {
    const wc = page()
    const emit = vi.fn()
    const controls = new BrowserPageControls(wc as unknown as WebContents, 'a', emit)
    controls.find('first')
    controls.find('second')
    wc.emit('found-in-page', {}, { requestId: 1, activeMatchOrdinal: 9, matches: 10 })
    expect(controls.snapshot()).toMatchObject({ query: 'second', current: 0, total: 0 })
    wc.emit('found-in-page', {}, { requestId: 2, activeMatchOrdinal: 1, matches: 3 })
    expect(controls.snapshot()).toMatchObject({ current: 1, total: 3 })
    const event = { preventDefault: vi.fn() }
    wc.emit('before-input-event', event, { type: 'keyDown', key: 'Enter', shift: true })
    expect(wc.findInPage).toHaveBeenLastCalledWith('second', { forward: false, findNext: true })
    wc.emit('before-input-event', event, { type: 'keyDown', key: 'Escape' })
    expect(controls.snapshot().open).toBe(false)
    expect(controls.zoom('reset')).toBe(100)
    expect(wc.setZoomFactor).toHaveBeenCalledWith(1)
  })

  it('inspects the embedded page at the context menu point', () => {
    const wc = page()
    const controls = new BrowserPageControls(wc as unknown as WebContents, 'a', vi.fn())
    controls.enableContextMenu({ copyLink: 'Copy', newTab: 'New', external: 'External', inspect: 'Inspect' }, vi.fn())
    wc.emit('context-menu', {}, { x: 15, y: 22, linkURL: '' })
    const entries = menu.buildFromTemplate.mock.calls.at(-1)![0] as unknown as Array<{ click(): void }>
    entries[0].click()
    expect(wc.inspectElement).toHaveBeenCalledWith(15, 22)
  })
})

describe('page references', () => {
  it('collects an element without activating it and restores ordinary clicks afterward', async () => {
    const dom = new JSDOM('<button>Submit order</button>', { runScripts: 'outside-only' })
    const { window } = dom
    const button = window.document.querySelector('button')!
    const activated = vi.fn()
    button.addEventListener('click', activated)
    const selected = window.eval(selectionScript('element'))
    window.document.elementsFromPoint = () => [button]
    const delivered = vi.fn()
    selected.then(delivered)
    button.dispatchEvent(new window.MouseEvent('pointerdown', { bubbles: true, cancelable: true }))
    await Promise.resolve()
    expect(delivered).not.toHaveBeenCalled()
    button.dispatchEvent(new window.MouseEvent('pointerup', { bubbles: true, cancelable: true }))
    const click = new window.MouseEvent('click', { bubbles: true, cancelable: true })
    expect(button.dispatchEvent(click)).toBe(false)
    expect(click.defaultPrevented).toBe(true)
    expect(activated).not.toHaveBeenCalled()
    expect(await selected).toMatchObject({ text: 'Submit order' })
    await new Promise<void>((resolve) => window.setTimeout(resolve, 0))
    button.click()
    expect(activated).toHaveBeenCalledOnce()
    dom.window.close()
  })

  it('clips partly visible elements without moving their content and removes its selection box', async () => {
    const dom = new JSDOM('<button>Pick me</button>', { runScripts: 'outside-only' })
    const { window } = dom
    const button = window.document.querySelector('button')!
    button.getBoundingClientRect = () => ({ x: -20, y: -10, width: 100, height: 60 }) as DOMRect
    const selected = window.eval(selectionScript('element'))
    window.document.elementsFromPoint = () => [button]
    button.dispatchEvent(new window.MouseEvent('pointerdown', { bubbles: true, cancelable: true, clientX: 5, clientY: 5 }))
    button.dispatchEvent(new window.MouseEvent('pointerup', { bubbles: true, cancelable: true, clientX: 5, clientY: 5 }))
    expect(await selected).toEqual({ text: 'Pick me', rect: { x: 0, y: 0, width: 80, height: 50 } })
    expect(window.document.querySelectorAll('div')).toHaveLength(0)
    dom.window.close()
  })

  it('captures the dragged rectangle and Escape cancels without a leftover overlay', async () => {
    const dom = new JSDOM('<main>Content</main>', { runScripts: 'outside-only' })
    const { window } = dom
    const selected = window.eval(selectionScript('region'))
    window.document.dispatchEvent(new window.MouseEvent('pointerdown', { clientX: 120, clientY: 90 }))
    window.document.dispatchEvent(new window.MouseEvent('pointermove', { clientX: 20, clientY: 30 }))
    window.document.dispatchEvent(new window.MouseEvent('pointerup', { clientX: 20, clientY: 30 }))
    expect(await selected).toEqual({ text: '', rect: { x: 20, y: 30, width: 100, height: 60 }, kind: 'region' })
    const cancelled = window.eval(selectionScript('element'))
    window.document.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape' }))
    expect(await cancelled).toBeNull()
    expect(window.document.querySelectorAll('div')).toHaveLength(0)
    dom.window.close()
  })

  it('outlines the hovered element at its own radius under a crosshair layer', async () => {
    const dom = new JSDOM('<button style="border-radius: 12px">Pick me</button>', { runScripts: 'outside-only' })
    const { window } = dom
    const button = window.document.querySelector('button')!
    button.getBoundingClientRect = () => ({ x: 10, y: 20, width: 100, height: 40 }) as DOMRect
    const selected = window.eval(selectionScript('element', 'rgb(1, 2, 3)'))
    const layer = window.document.querySelector<HTMLElement>('[data-dotcraft-selection-layer]')!
    const box = layer.nextElementSibling as HTMLElement
    expect(layer.style.cursor).toBe('crosshair')
    expect(box.style.display).toBe('none')
    window.document.elementsFromPoint = () => [layer, button]
    window.document.dispatchEvent(new window.MouseEvent('pointermove', { clientX: 30, clientY: 30 }))
    expect(box.style.display).toBe('block')
    expect(box.style.borderStyle).toBe('solid')
    expect(box.style.borderRadius).toBe('12px')
    expect([box.style.left, box.style.top, box.style.width, box.style.height]).toEqual(['10px', '20px', '100px', '40px'])
    window.document.elementsFromPoint = () => [layer, window.document.body]
    window.document.dispatchEvent(new window.MouseEvent('pointermove', { clientX: 300, clientY: 300 }))
    expect(box.style.display).toBe('none')
    window.document.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape' }))
    expect(await selected).toBeNull()
    expect(window.document.querySelectorAll('div')).toHaveLength(0)
    dom.window.close()
  })

  it('switches to a dashed region with the bubble pointer past 4px and Escape cancels only the drag', async () => {
    const dom = new JSDOM('<main>Content</main>', { runScripts: 'outside-only' })
    const { window } = dom
    const main = window.document.querySelector('main')!
    const selected = window.eval(selectionScript('element'))
    const delivered = vi.fn()
    selected.then(delivered)
    const layer = window.document.querySelector<HTMLElement>('[data-dotcraft-selection-layer]')!
    const box = layer.nextElementSibling as HTMLElement
    main.dispatchEvent(new window.MouseEvent('pointerdown', { bubbles: true, cancelable: true, clientX: 100, clientY: 100 }))
    window.document.dispatchEvent(new window.MouseEvent('pointermove', { clientX: 103, clientY: 102 }))
    expect(box.style.display).toBe('none')
    expect(layer.style.cursor).toBe('crosshair')
    window.document.dispatchEvent(new window.MouseEvent('pointermove', { clientX: 140, clientY: 130 }))
    expect(box.style.borderStyle).toBe('dashed')
    expect([box.style.left, box.style.top, box.style.width, box.style.height]).toEqual(['100px', '100px', '40px', '30px'])
    expect(layer.style.cursor).toContain('data:image/svg+xml')
    window.document.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape' }))
    await Promise.resolve()
    expect(delivered).not.toHaveBeenCalled()
    expect(box.style.display).toBe('none')
    expect(layer.style.cursor).toBe('crosshair')
    window.document.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape' }))
    expect(await selected).toBeNull()
    expect(window.document.querySelectorAll('div')).toHaveLength(0)
    dom.window.close()
  })

  it('keeps the mode when a click lands on nothing', async () => {
    const dom = new JSDOM('<main>Content</main>', { runScripts: 'outside-only' })
    const { window } = dom
    const selected = window.eval(selectionScript('element'))
    const delivered = vi.fn()
    selected.then(delivered)
    const layer = window.document.querySelector<HTMLElement>('[data-dotcraft-selection-layer]')!
    window.document.elementsFromPoint = () => [layer, window.document.body]
    window.document.dispatchEvent(new window.MouseEvent('pointerdown', { clientX: 5, clientY: 5 }))
    window.document.dispatchEvent(new window.MouseEvent('pointerup', { clientX: 5, clientY: 5 }))
    await Promise.resolve()
    expect(delivered).not.toHaveBeenCalled()
    expect(window.document.querySelector('[data-dotcraft-selection-layer]')).not.toBeNull()
    window.document.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape' }))
    expect(await selected).toBeNull()
    dom.window.close()
  })

  it('paints the page-side outline with the requested accent', async () => {
    const wc = page()
    wc.executeJavaScript.mockResolvedValue(null)
    await capturePageReference(wc as unknown as WebContents, { tabId: 'a' }, 'element', 'rgb(1, 2, 3)')
    expect(wc.executeJavaScript.mock.calls[0][0]).toContain('rgb(1, 2, 3)')
  })

  it('captures fixed origin and scales the selected region to page coordinates', async () => {
    const wc = page()
    wc.executeJavaScript.mockResolvedValue({ text: '', rect: { x: 10, y: 20, width: 100, height: 80 } })
    const result = await capturePageReference(wc as unknown as WebContents, { tabId: 'a', threadId: 'task-a' }, 'region')
    expect(wc.capturePage).toHaveBeenCalledWith({ x: 15, y: 30, width: 150, height: 120 })
    expect(result).toMatchObject({ kind: 'region', threadId: 'task-a', title: 'Original page', url: 'https://example.com/a', imageDataUrl: 'data:image/png;base64,AA==' })
  })

  it('does not deliver cancelled or navigated selections', async () => {
    const wc = page()
    wc.executeJavaScript.mockResolvedValue(null)
    expect(await capturePageReference(wc as unknown as WebContents, { tabId: 'a' }, 'element')).toBeNull()
    wc.executeJavaScript.mockResolvedValue({ text: 'Selected' })
    wc.getURL.mockReturnValueOnce('https://example.com/a').mockReturnValue('https://example.com/b')
    expect(await capturePageReference(wc as unknown as WebContents, { tabId: 'a' }, 'text')).toBeNull()
  })
})
