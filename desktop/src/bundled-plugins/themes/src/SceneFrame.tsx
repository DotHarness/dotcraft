import { useEffect, useRef, useState, type JSX } from 'react'
import type { Theme, ThemeBackdrop } from '../../../shared/themes'
import {
  MCP_APP_SANDBOX_PROXY_READY_METHOD,
  MCP_APP_SANDBOX_PROXY_URL,
  MCP_APP_SANDBOX_BRIDGE_VIOLATION_METHOD,
  VISUALIZATION_SANDBOX_RESOURCE_READY_METHOD
} from '../../../shared/mcpAppSandbox'
import { buildSceneDocument, posterUrl, SCENE_STATUS_METHOD } from './sceneDocument'
import { reportBackdropReady, themeKey } from './themeLibrary'

const STARTUP_TIMEOUT_MS = 8000

function useSceneMotion(enabled: boolean): boolean {
  const [allowed, setAllowed] = useState(false)
  useEffect(() => {
    const media = window.matchMedia('(prefers-reduced-motion: reduce)')
    const root = document.documentElement
    const update = (): void => setAllowed(enabled && !media.matches && !document.hidden && root.dataset.reduceMotion !== 'on')
    const observer = new MutationObserver(update)
    observer.observe(root, { attributes: true, attributeFilter: ['data-reduce-motion'] })
    media.addEventListener('change', update)
    document.addEventListener('visibilitychange', update)
    update()
    return () => {
      observer.disconnect()
      media.removeEventListener('change', update)
      document.removeEventListener('visibilitychange', update)
    }
  }, [enabled])
  return allowed
}

interface SceneFrameProps {
  theme: Theme
  scene: Extract<ThemeBackdrop, { kind: 'scene' }>
  animate: boolean
}

export function SceneFrame({ theme, scene, animate }: SceneFrameProps): JSX.Element {
  const motion = useSceneMotion(animate)
  const [failed, setFailed] = useState(false)
  const [ready, setReady] = useState(false)
  const [posterReady, setPosterReady] = useState(false)
  const frame = useRef<HTMLIFrameElement>(null)
  const key = themeKey(theme)
  useEffect(() => { setFailed(false); setReady(false) }, [key, motion])
  useEffect(() => { setPosterReady(false) }, [key])
  const running = motion && !failed

  useEffect(() => {
    reportBackdropReady(theme, posterReady || (running && ready))
    return () => reportBackdropReady(theme, false)
  }, [key, running, posterReady, ready])

  useEffect(() => {
    if (!running) return
    const target = frame.current
    if (!target) return
    const nonce = crypto.randomUUID()
    const fail = (): void => { setFailed(true); setReady(false) }
    const timeout = window.setTimeout(fail, STARTUP_TIMEOUT_MS)
    let booted = false
    const receive = (event: MessageEvent): void => {
      if (event.source !== target.contentWindow || !event.data || typeof event.data !== 'object') return
      const message = event.data as { method?: string; params?: { nonce?: string; state?: string } }
      if (message.method === MCP_APP_SANDBOX_PROXY_READY_METHOD && !booted) {
        booted = true
        target.contentWindow?.postMessage({
          method: VISUALIZATION_SANDBOX_RESOURCE_READY_METHOD,
          params: { html: buildSceneDocument(scene.source, nonce) }
        }, '*')
      } else if (message.method === MCP_APP_SANDBOX_BRIDGE_VIOLATION_METHOD) {
        fail()
      } else if (message.method === SCENE_STATUS_METHOD && message.params?.nonce === nonce) {
        if (message.params.state === 'ready') {
          window.clearTimeout(timeout)
          setReady(true)
        } else if (message.params.state === 'failed') fail()
      }
    }
    window.addEventListener('message', receive)
    target.src = MCP_APP_SANDBOX_PROXY_URL
    return () => {
      window.clearTimeout(timeout)
      window.removeEventListener('message', receive)
    }
  }, [key, running])

  return (
    <>
      <img
        className="dcw-scene-poster"
        src={posterUrl(scene.poster)}
        alt=""
        draggable={false}
        onLoad={() => setPosterReady(true)}
        onError={() => setPosterReady(false)}
      />
      {running && <iframe
        key={key}
        ref={frame}
        name="dotcraft-themes"
        title={theme.name}
        sandbox="allow-scripts"
        referrerPolicy="no-referrer"
        allow=""
        tabIndex={-1}
        aria-hidden="true"
        className={`dcw-scene-frame${ready ? ' dcw-scene-frame-ready' : ''}`}
      />}
    </>
  )
}
