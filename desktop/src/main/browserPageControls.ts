import { clipboard, Menu, shell, type WebContents } from 'electron'
import type { BrowserFeedbackEvent, BrowserFindState } from '../shared/viewer/browserFeedback'

export interface BrowserContextLabels {
  copyLink: string
  newTab: string
  external: string
  inspect: string
  quoteSelection?: string
}

export class BrowserPageControls {
  private state: BrowserFindState = { query: '', current: 0, total: 0, open: false }
  private requestId: number | null = null

  constructor(
    private readonly page: WebContents,
    private readonly tabId: string,
    private readonly emit: (event: BrowserFeedbackEvent) => void
  ) {
    page.on('found-in-page', (_event, result) => {
      if (result.requestId !== this.requestId || !this.state.query) return
      this.state.current = result.activeMatchOrdinal
      this.state.total = result.matches
      this.publish()
    })
    page.on('did-start-navigation', (_event, _url, isInPlace, isMainFrame) => {
      if (isMainFrame && !isInPlace) this.close()
    })
    page.on('before-input-event', (event, input) => {
      if (input.type !== 'keyDown') return
      if ((input.control || input.meta) && input.key.toLowerCase() === 'f') {
        event.preventDefault()
        this.state.open = true
        this.publish()
      } else if (this.state.open && input.key === 'Escape') {
        event.preventDefault()
        this.close()
      } else if (this.state.open && input.key === 'Enter') {
        event.preventDefault()
        this.find(this.state.query, input.shift ? 'previous' : 'next')
      }
    })
  }

  enableContextMenu(labels: BrowserContextLabels, newTab: (url: string) => void, quoteSelection?: () => void): void {
    this.page.on('context-menu', (_event, params) => {
      const items: Electron.MenuItemConstructorOptions[] = []
      if (params.selectionText?.trim() && labels.quoteSelection && quoteSelection) {
        items.push({ label: labels.quoteSelection, click: quoteSelection }, { type: 'separator' })
      }
      if (params.linkURL) {
        items.push({ label: labels.copyLink, click: () => clipboard.writeText(params.linkURL) })
        if (/^https?:/i.test(params.linkURL)) {
          items.push({ label: labels.newTab, click: () => newTab(params.linkURL) })
          items.push({ label: labels.external, click: () => { void shell.openExternal(params.linkURL) } })
        }
        items.push({ type: 'separator' })
      }
      items.push({ label: labels.inspect, click: () => this.page.inspectElement(params.x, params.y) })
      Menu.buildFromTemplate(items).popup()
    })
  }

  snapshot(): BrowserFindState { return { ...this.state } }

  find(query: string, direction?: 'next' | 'previous'): BrowserFindState {
    this.state.open = true
    if (!query) {
      this.page.stopFindInPage('clearSelection')
      this.requestId = null
      this.state = { query: '', current: 0, total: 0, open: true }
    } else {
      const sameQuery = this.state.query === query
      this.state = { query, current: sameQuery ? this.state.current : 0, total: sameQuery ? this.state.total : 0, open: true }
      this.requestId = this.page.findInPage(query, { forward: direction !== 'previous', findNext: sameQuery && direction !== undefined })
    }
    this.publish()
    return this.snapshot()
  }

  close(): void {
    this.requestId = null
    this.page.stopFindInPage('clearSelection')
    this.state = { query: '', current: 0, total: 0, open: false }
    this.publish()
  }

  zoom(action: 'in' | 'out' | 'reset'): number {
    const percent = action === 'reset' ? 100 : Math.min(500, Math.max(25, Math.round(this.page.getZoomFactor() * 100) + (action === 'in' ? 10 : -10)))
    this.page.setZoomFactor(percent / 100)
    this.emit({ type: 'zoom', tabId: this.tabId, percent })
    return percent
  }

  private publish(): void { this.emit({ type: 'find', tabId: this.tabId, state: this.snapshot() }) }
}
