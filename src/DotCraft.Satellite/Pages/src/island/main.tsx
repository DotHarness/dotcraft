import { useEffect, useRef, useState } from 'react'
import { createRoot } from 'react-dom/client'
import '@dotcraft/avatar/styles.css'
import '@dotcraft/island/styles.css'
import './island-page.css'
import { listen, post } from '../host'
import { SatelliteIsland } from './SatelliteIsland'
import type { IslandState } from './types'

declare global {
  interface Window {
    island?: { focusDecision: () => void }
  }
}

function IslandPage() {
  const [state, setState] = useState<IslandState | null>(null)
  const decision = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    window.island = { focusDecision: () => decision.current?.focus() }
    return listen<IslandState>(setState)
  }, [])

  return state ? <SatelliteIsland state={state} onMessage={post} decisionRef={decision} /> : null
}

createRoot(document.getElementById('root')!).render(<IslandPage />)
