import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { applyTheme, resolveTheme } from './utils/theme'
import { AddTabPopupWindow } from './components/detail/AddTabPopupWindow'
import './styles/tokens.css'

const initialTheme = resolveTheme(window.api?.initialTheme)
applyTheme(initialTheme, { syncTitleBarOverlay: false })

document.documentElement.style.background = 'transparent'
document.body.style.background = 'transparent'
document.body.style.overflow = 'hidden'

const rootElement = document.getElementById('root')
if (!rootElement) {
  throw new Error('Root element not found — check add-tab-popup.html for <div id="root">')
}

createRoot(rootElement).render(
  <StrictMode>
    <AddTabPopupWindow />
  </StrictMode>
)
