/**
 * Loaded dynamically by main.tsx after the mock preload bridge is installed,
 * so no Desktop renderer module can evaluate before `window.api` exists.
 */
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { LocaleProvider } from '@renderer/contexts/LocaleContext'
import { applyTheme } from '@renderer/utils/theme'
import { DemoApp } from './DemoApp'
import { bootstrapDemo } from './demoController'
import { demoTheme } from './mockApi'

export function start(): void {
  applyTheme(demoTheme)
  bootstrapDemo()

  // Host pages (docs homepage embed) switch the demo theme in place.
  window.addEventListener('message', (event: MessageEvent) => {
    const data = event.data as { type?: string; theme?: string } | null
    if (data?.type === 'dotcraft-demo:set-theme' && (data.theme === 'dark' || data.theme === 'light')) {
      applyTheme(data.theme)
    }
  })

  const rootElement = document.getElementById('root')
  if (!rootElement) {
    throw new Error('Root element not found — check index.html for <div id="root">')
  }

  createRoot(rootElement).render(
    <StrictMode>
      <LocaleProvider>
        <DemoApp />
      </LocaleProvider>
    </StrictMode>
  )
}
