import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App'
import { LocaleProvider } from './contexts/LocaleContext'
import { applyTheme, resolveTheme } from './utils/theme'
import { installAutomationBridge } from './e2e/automationBridge'
import './styles/tokens.css'

installAutomationBridge()

const params = new URLSearchParams(window.location.search)
const initialTheme = resolveTheme(params.get('theme') ?? window.api?.initialTheme)
applyTheme(initialTheme)

const rootElement = document.getElementById('root')
if (!rootElement) {
  throw new Error('Root element not found — check index.html for <div id="root">')
}

createRoot(rootElement).render(
  <StrictMode>
    <LocaleProvider>
      <App />
    </LocaleProvider>
  </StrictMode>
)
