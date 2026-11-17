import { useEffect, useState } from 'react'
import { createRoot } from 'react-dom/client'
import type { GlowPoint } from '@dotcraft/island'
import { EdgeGlow } from '@dotcraft/island/react'
import '@dotcraft/island/styles.css'
import { listen } from '../host'
import './glow-page.css'

/** `origin` is the island's centre in this display's page pixels, or null when the island is on another display. */
interface GlowState {
  reduceMotion: boolean
  origin: GlowPoint | null
}

function GlowPage() {
  const [state, setState] = useState<GlowState | null>(null)
  useEffect(() => listen<GlowState>(setState), [])
  return state ? <EdgeGlow origin={state.origin} reduceMotion={state.reduceMotion} /> : null
}

createRoot(document.getElementById('root')!).render(<GlowPage />)
