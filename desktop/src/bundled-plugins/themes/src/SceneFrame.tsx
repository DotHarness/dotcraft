import { useEffect, useMemo, useRef, useState, type JSX } from 'react'
import type { Theme, ThemeBackdrop } from '../../../shared/themes'
import {
  MCP_APP_SANDBOX_PROXY_READY_METHOD,
  MCP_APP_SANDBOX_PROXY_URL,
  MCP_APP_SANDBOX_BRIDGE_VIOLATION_METHOD,
  VISUALIZATION_SANDBOX_RESOURCE_READY_METHOD
} from '../../../shared/mcpAppSandbox'
import { buildSceneDocument, SCENE_STATUS_METHOD, svgDataUrl } from './sceneDocument'
import { reportBackdropReady, themeKey } from './themeLibrary'

const STARTUP_TIMEOUT_MS = 8000
const REDUCED_MOTION_QUERY = '(prefers-reduced-motion: reduce)'

function readSceneMotion(enabled: boolean, media = window.matchMedia(REDUCED_MOTION_QUERY)): boolean {
  return enabled && document.documentElement.dataset.reduceMotion !== 'on' && !media.matches
}

function useSceneMotion(enabled: boolean): boolean {
  const [allowed, setAllowed] = useState(() => readSceneMotion(enabled))
  useEffect(() => {
    const media = window.matchMedia(REDUCED_MOTION_QUERY)
    const update = (): void => setAllowed(readSceneMotion(enabled, media))
    const observer = new MutationObserver(update)
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-reduce-motion'] })
    media.addEventListener('change', update)
    update()
    return () => {
      observer.disconnect()
      media.removeEventListener('change', update)
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
  const [frameReady, setFrameReady] = useState(false)
  const [imageReady, setImageReady] = useState(false)
  const frame = useRef<HTMLIFrameElement>(null)
  const key = themeKey(theme)
  const isHtml = scene.format === 'html'
  useEffect(() => { setFailed(false); setFrameReady(false) }, [key, motion])
  useEffect(() => { setImageReady(false) }, [key])
  const running = motion && !failed
  const animatedSvg = !isHtml && running
  const htmlRunning = isHtml && running
  const imageSrc = useMemo(() => svgDataUrl(animatedSvg ? scene.source : scene.poster), [animatedSvg, scene.source, scene.poster])
  const drawn = imageReady || (htmlRunning && frameReady)

  useEffect(() => {
    reportBackdropReady(theme, drawn)
  }, [key, drawn])

  useEffect(() => () => reportBackdropReady(theme, false), [key])

  useEffect(() => {
    if (!htmlRunning) return
    const target = frame.current
    if (!target) return
    const nonce = crypto.randomUUID()
    const fail = (): void => { setFailed(true); setFrameReady(false) }
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
          setFrameReady(true)
        } else if (message.params.state === 'failed') fail()
      }
    }
    window.addEventListener('message', receive)
    target.src = MCP_APP_SANDBOX_PROXY_URL
    return () => {
      window.clearTimeout(timeout)
      window.removeEventListener('message', receive)
    }
  }, [key, htmlRunning])

  return (
    <>
      <img
        className="dcw-scene-poster"
        src={imageSrc}
        alt=""
        draggable={false}
        onLoad={() => setImageReady(true)}
        onError={() => {
          setImageReady(false)
          if (animatedSvg) setFailed(true)
        }}
      />
      {htmlRunning && <iframe
        key={key}
        ref={frame}
        name="dotcraft-themes"
        title={theme.name}
        sandbox="allow-scripts"
        referrerPolicy="no-referrer"
        allow=""
        tabIndex={-1}
        aria-hidden="true"
        className={`dcw-scene-frame${frameReady ? ' dcw-scene-frame-ready' : ''}`}
      />}
    </>
  )
}
