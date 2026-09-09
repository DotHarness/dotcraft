import { useEffect, useRef, useState } from 'react'
import { Avatar } from '@dotcraft/avatar/react'
import { MessageSquare, PanelTop } from 'lucide-react'
import { PetQuickChat } from './PetQuickChat'
import { useSetUiLocale, useT } from '../../contexts/LocaleContext'
import { applyTheme } from '../../utils/theme'
import type { PetPoint, PetSnapshot } from '../../../shared/desktopPet'
import { usePetReaction } from './usePetReaction'
import { usePetGaze } from './usePetGaze'
import { usePetIdle } from './usePetIdle'
import { petActivity } from '../../../shared/desktopPet'

export function DesktopPet(): JSX.Element | null {
  const [snapshot, setSnapshot] = useState<PetSnapshot | null>(null)
  const [position, setPosition] = useState({ x: 0, y: 0, size: 44 })
  const [phase, setPhase] = useState('leaving')
  const [landing, setLanding] = useState<PetPoint>()
  const [pose, setPose] = useState({ scaleX: 1, scaleY: 1, rotation: 0 })
  const [chat, setChat] = useState(false)
  const [text, setText] = useState('')
  const drag = useRef(false)
  const press = useRef<{ x: number; y: number; lastX: number } | null>(null)
  const reaction = usePetReaction(snapshot?.reducedMotion ?? false)
  const sourceDrag = useRef<number | null>(null)
  const gaze = useRef<HTMLSpanElement>(null)
  const idle = usePetIdle(petActivity(snapshot?.activity), !!snapshot && phase === 'pet' && !chat && !reaction.dragging && reaction.state === 'idle', snapshot?.reducedMotion ?? false)
  const gazeEnabled = !!snapshot && !snapshot.reducedMotion && phase === 'pet' && !reaction.dragging && reaction.state === 'idle' && idle.pose === 'idle'
  usePetGaze(gaze, gazeEnabled)
  const revision = useRef(0)
  const chatRef = useRef(chat)
  chatRef.current = chat
  const t = useT()
  const setUiLocale = useSetUiLocale()
  const api = window.api.desktopPet
  useEffect(() => {
    const off = api.onEvent(event => {
      if (event.type === 'snapshot') {
        setSnapshot(event.snapshot)
        setUiLocale(event.snapshot.locale)
        if ((event.snapshot.editRevision ?? 0) >= revision.current) setText(event.snapshot.text)
        applyTheme(event.snapshot.theme, { syncTitleBarOverlay: false })
      } else if (event.type === 'position') {
        if (event.held) {
          reaction.move(sourceDrag.current === null ? 0 : event.point.x - sourceDrag.current)
          sourceDrag.current = event.point.x
        } else if (sourceDrag.current !== null) {
          sourceDrag.current = null
          reaction.land()
        }
        setPosition({ ...event.point, size: event.size })
        setPhase(event.phase)
        setLanding(event.landing)
        setPose(event.pose ?? { scaleX: 1, scaleY: 1, rotation: 0 })
      }
    })
    void api.command({ type: 'ready' })
    const hitTest = (event: PointerEvent): void => {
      const hit = (event.target as Element).closest?.('[data-pet-interactive]')
      void api.command({ type: 'interactive', value: !!hit || drag.current })
    }
    const escape = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        if (chatRef.current) { setChat(false); void api.command({ type: 'chat', open: false }) }
        else void api.command({ type: 'return' })
      }
    }
    window.addEventListener('pointermove', hitTest)
    window.addEventListener('keydown', escape)
    return () => { off(); window.removeEventListener('pointermove', hitTest); window.removeEventListener('keydown', escape) }
  }, [api, setUiLocale])
  if (!snapshot) return null
  const chatWidth = Math.min(360, window.innerWidth - 24)
  const chatX = Math.max(12, Math.min(
    position.x + position.size / 2 - chatWidth / 2,
    window.innerWidth - chatWidth - 12
  ))
  const chatTop = Math.min(position.y + position.size + 12, window.innerHeight - 60)
  return <>
    {landing && <span className="desktop-pet-landing" style={{ left: landing.x, top: landing.y }} />}
    <div className="desktop-pet-character" data-pet-interactive data-dragging={reaction.dragging} data-motion={snapshot.reducedMotion ? 'off' : 'on'}
      data-actions-side={position.x + position.size + 40 > window.innerWidth ? 'left' : 'right'}
      style={{ left: position.x, top: position.y }}>
      <button className="desktop-pet-drag" aria-label={t('desktopPet.drag')}
        style={{ transform: `rotate(${pose.rotation}deg) scale(${pose.scaleX}, ${pose.scaleY})` }}
        disabled={phase !== 'pet'}
        onDoubleClick={() => void api.command({ type: 'return' })}
        onPointerDown={event => {
          if (event.button !== 0) return
          press.current = { x: event.screenX, y: event.screenY, lastX: event.screenX }
          event.currentTarget.setPointerCapture(event.pointerId)
        }} onPointerMove={event => {
          const start = press.current
          if (!start) return
          if (!drag.current && Math.hypot(event.screenX - start.x, event.screenY - start.y) < 5) return
          if (!drag.current) { drag.current = true; void api.command({ type: 'drag', stage: 'start' }) }
          reaction.move(event.screenX - start.lastX)
          start.lastX = event.screenX
          void api.command({ type: 'drag', stage: 'move' })
        }}
        onPointerUp={event => {
          const moved = drag.current
          drag.current = false
          press.current = null
          if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId)
          if (moved) { reaction.land(); void api.command({ type: 'drag', stage: 'end' }) }
          else reaction.greet()
        }} onClick={event => { if (event.detail === 0) reaction.greet() }}
        onPointerCancel={() => { press.current = null; drag.current = false; reaction.land(); void api.command({ type: 'drag', stage: 'end' }) }}
        onLostPointerCapture={() => {
          press.current = null
          if (drag.current) { drag.current = false; reaction.land(); void api.command({ type: 'drag', stage: 'end' }) }
        }}>
        <span ref={gaze} className="desktop-pet-reaction" data-gaze={gazeEnabled} style={{ transform: `rotate(${reaction.tilt}deg) scale(${reaction.dragging && !snapshot.reducedMotion ? 1.04 : 1})` }}>
          <Avatar name={snapshot.name} size={position.size} state={reaction.dragging ? 'idle' : reaction.state !== 'idle' ? reaction.state : idle.pose} eventSequence={reaction.sequence}
            expression={reaction.dragging ? 'operator' : reaction.state === 'greeting' ? 'happy' : undefined}
            gesture={reaction.dragging ? reaction.direction : idle.gesture} gestureSequence={reaction.dragging ? reaction.sequence : idle.gestureSequence}
            onGestureComplete={idle.completeGesture}
            motion={snapshot.reducedMotion ? 'off' : 'on'} />
        </span>
      </button>
      {phase === 'pet' && <div className="desktop-pet-actions">
        <button className="desktop-pet-action" aria-label={t('desktopPet.chat')} aria-expanded={chat}
          onClick={() => {
            if (!snapshot.canChat) { void api.command({ type: 'return' }); return }
            setChat(!chat)
            void api.command({ type: 'chat', open: !chat })
          }}><MessageSquare size={16} /></button>
        <button className="desktop-pet-action" aria-label={t('desktopPet.return')}
          onClick={() => void api.command({ type: 'return' })}><PanelTop size={16} /></button>
      </div>}
    </div>
    {chat && phase === 'pet' && snapshot.canChat && <div className="desktop-pet-chat-position" data-pet-interactive style={{ left: chatX, top: chatTop, maxHeight: window.innerHeight - chatTop - 12 }}>
      <PetQuickChat text={text} onChange={value => { setText(value); void api.command({ type: 'edit', text: value, submit: false, revision: ++revision.current }) }}
        onSubmit={() => void api.command({ type: 'edit', text, submit: true, revision: ++revision.current })} />
    </div>}
  </>
}
