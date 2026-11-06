import type { WebContents } from 'electron'

interface ObservedTab {
  logs: Array<{ level: string; message: string; timestamp: string; url?: string }>
  disposeListeners?: () => void
}

export function observeBrowserUseTab(tab: ObservedTab, page: WebContents, destroyed: () => void): void {
  if (tab.disposeListeners) return
  const onConsole = (_event: unknown, level: number, message: string) => {
    const levels = ['debug', 'info', 'warn', 'error'] as const
    tab.logs.push({ level: levels[level] ?? String(level ?? 'log'), message,
      timestamp: new Date().toISOString(), url: page.getURL() })
  }
  const onDestroyed = () => {
    tab.disposeListeners?.()
    destroyed()
  }
  page.on('console-message', onConsole)
  page.once('destroyed', onDestroyed)
  tab.disposeListeners = () => {
    page.off('console-message', onConsole)
    page.off('destroyed', onDestroyed)
    tab.disposeListeners = undefined
  }
}
