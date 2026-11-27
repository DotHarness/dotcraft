import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { MascotOutfit, PersonalMascot } from '@dotcraft/plugin'
import type { DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import { CLEF } from './clef'
import { findConductor } from './conductor'
import { CONDUCTOR_OUTFIT } from './conductorOutfit'
import kickoffPrompt from './prompts/kickoff.md?raw'
import { useOratorioI18n, type OratorioMessageKey } from './oratorio-i18n'
import './oratorio-intro.css'

const KICKOFF_PROMPT = kickoffPrompt.trimEnd()

const GREETINGS: readonly OratorioMessageKey[] = ['introGreetingHello', 'introGreetingAsk', 'introGreetingChat', 'introGreetingReport']
const MASCOT_SIZE = 112
const SCALE = 0.9
// Where the baton tip sits in the mascot's box, from the avatar's rig geometry.
const TIP = { x: 0.007, y: 0.39 }
const RISE_OFFSET = 10

interface Geometry {
  width: number
  height: number
  originX: number
  originY: number
  tipX: number
  tipY: number
}

function Greeting(): JSX.Element {
  const { t } = useOratorioI18n()
  const [index, setIndex] = useState(0)
  useEffect(() => {
    const timer = window.setTimeout(() => setIndex((value) => (value + 1) % GREETINGS.length), index === 0 ? 2400 : 3200)
    return () => window.clearTimeout(timer)
  }, [index])
  return (
    <button type="button" className="ora-intro__thought" aria-label={t('introNextGreeting')} onClick={() => setIndex((value) => (value + 1) % GREETINGS.length)}>
      <span key={index} className="ora-intro__line">{t(GREETINGS[index]!)}</span>
    </button>
  )
}

function Trace({ geometry }: { geometry: Geometry }): JSX.Element {
  const tipX = (geometry.tipX - geometry.originX) / SCALE
  const tipY = (geometry.tipY - geometry.originY) / SCALE
  const d = `M 70 150 ${CLEF} C 82 216, ${tipX + 30} ${tipY - 40}, ${tipX} ${tipY}`
  return (
    <svg className="ora-intro__trace" viewBox={`0 0 ${geometry.width} ${geometry.height}`} aria-hidden="true">
      <g transform={`translate(${geometry.originX} ${geometry.originY}) scale(${SCALE})`}>
        <path className="ora-intro__stroke" d={d} pathLength={1} />
        <circle className="ora-intro__spark" r="5" style={{ offsetPath: `path('${d}')` }} />
      </g>
    </svg>
  )
}

export function ConductorIntro({ host, context }: DesktopPluginSurfaceProps<'conversation.empty'>): JSX.Element | null {
  const { t } = useOratorioI18n()
  const [starting, setStarting] = useState(false)
  const [dismissed, setDismissed] = useState(false)
  const open = !dismissed && context.threadId === findConductor(host)?.threadId
  const rootRef = useRef<HTMLDivElement>(null)
  const mascotRef = useRef<HTMLSpanElement>(null)
  const [geometry, setGeometry] = useState<Geometry | null>(null)
  const [greeting, setGreeting] = useState(false)
  const { cover, threadId } = context

  useLayoutEffect(() => (open ? cover() : undefined), [open, cover])

  useLayoutEffect(() => {
    const root = rootRef.current?.getBoundingClientRect()
    const mascot = mascotRef.current?.getBoundingClientRect()
    if (!root || !mascot) return
    const tipX = mascot.left - root.left + TIP.x * mascot.width
    const tipY = mascot.top - root.top + TIP.y * mascot.height - RISE_OFFSET
    setGeometry({ width: root.width, height: root.height, originX: root.width / 2 + 10, originY: tipY - 210, tipX, tipY })
  }, [open])

  useEffect(() => {
    if (!open) return
    const wave = window.setTimeout(() => setGreeting(true), 2600)
    const settle = window.setTimeout(() => setGreeting(false), 4400)
    return () => { window.clearTimeout(wave); window.clearTimeout(settle) }
  }, [open])

  if (!open) return null
  return (
    <div ref={rootRef} className="ora-intro">
      {geometry && <Trace geometry={geometry} />}
      <div className="ora-intro__content">
        <div className="ora-intro__figure">
          <Greeting />
          <span className="ora-intro__trail" aria-hidden="true" />
          <span ref={mascotRef} className="ora-intro__mascot">
            <MascotOutfit {...CONDUCTOR_OUTFIT}>
              <PersonalMascot size={MASCOT_SIZE} greeting={greeting} />
            </MascotOutfit>
          </span>
        </div>
        <h2 className="ora-intro__title">{t('introTitle')}</h2>
        <p className="ora-intro__description">{t('introDescription')}</p>
        <button type="button" className="ora-intro__continue" disabled={starting} onClick={() => {
          setStarting(true)
          const text = KICKOFF_PROMPT.replace('{locale}', host.environment.locale)
          host.appServer.request('turn/startOrEnqueue', {
            threadId,
            kickoff: true,
            input: [{ type: 'text', text }]
          }).then((result) => {
            if (!result.turn) setDismissed(true)
          }, (error: unknown) => {
            setDismissed(true)
            host.ui.showToast({ message: error instanceof Error ? error.message : String(error), tone: 'error' })
          })
        }}>
          {t('introContinue')}
        </button>
      </div>
    </div>
  )
}
