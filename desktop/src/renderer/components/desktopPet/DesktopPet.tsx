import { useEffect, useRef, useState } from 'react'
import { Avatar, MascotIdleStage } from '@dotcraft/avatar/react'
import { MessageSquare, PanelTop } from 'lucide-react'
import { PetActivitySurface } from './PetActivitySurface'
import { useSetUiLocale, useT } from '../../contexts/LocaleContext'
import { applyTheme } from '../../utils/theme'
import type { PetPoint, PetSnapshot } from '../../../shared/desktopPet'
import { usePetReaction } from './usePetReaction'
import { usePetGaze } from './usePetGaze'
import { usePetIdle } from './usePetIdle'
import { usePetActivityView } from './usePetActivityView'
import { petActivity } from '../../../shared/desktopPet'

export function DesktopPet(): JSX.Element | null {
  const [snapshot, setSnapshot] = useState<PetSnapshot | null>(null)
  const [position, setPosition] = useState({ x: 0, y: 0, size: 44 })
  const [phase, setPhase] = useState('leaving')
  const [pose, setPose] = useState({ scaleX: 1, scaleY: 1, rotation: 0 })
  const [text, setText] = useState('')
  const [hold, setHold] = useState(false)
  const drag = useRef(false)
  const press = useRef<{ x: number; y: number; lastX: number } | null>(null)
  const pointer = useRef<PetPoint | null>(null)
  const reaction = usePetReaction(snapshot?.reducedMotion ?? false)
  const sourceDrag = useRef<number | null>(null)
  const gaze = useRef<HTMLSpanElement>(null)
  const settled = !!snapshot && phase === 'pet'
  const activity = usePetActivityView(settled ? snapshot.status : undefined, hold || text.trim().length > 0)
  const open = settled && activity.view !== 'hidden'
  const idle = usePetIdle({
    activity: petActivity(snapshot?.activity),
    available: settled && !open && !reaction.dragging && reaction.state === 'idle',
    reduced: snapshot?.reducedMotion ?? false,
    position
  })
  const tripping = idle.activeIdle != null
  const trippingRef = useRef(tripping)
  trippingRef.current = tripping
  const gazeEnabled = settled && !snapshot.reducedMotion && !reaction.dragging && reaction.state === 'idle' && idle.pose === 'idle' && !tripping
  usePetGaze(gaze, gazeEnabled)
  const revision = useRef(0)
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
        setPose(event.pose ?? { scaleX: 1, scaleY: 1, rotation: 0 })
      }
    })
    void api.command({ type: 'ready' })
    const hitTest = (event: PointerEvent): void => {
      pointer.current = { x: event.clientX, y: event.clientY }
      const hit = !trippingRef.current && (event.target as Element).closest?.('[data-pet-interactive]')
      void api.command({ type: 'interactive', value: !!hit || drag.current })
    }
    window.addEventListener('pointermove', hitTest)
    return () => { off(); window.removeEventListener('pointermove', hitTest) }
  }, [api, setUiLocale])
  // Closing a Ready pill is the only way to have "looked" from the desktop, so it doubles as the read receipt.
  const status = snapshot?.status
  const dismiss = (): void => {
    if (status?.status === 'review') void api.command({ type: 'read', turnId: status.turnId })
    activity.dismiss()
  }
  const dismissRef = useRef(dismiss)
  dismissRef.current = dismiss
  useEffect(() => {
    const escape = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return
      if (activity.view === 'pill') dismissRef.current()
      else void api.command({ type: 'return' })
    }
    window.addEventListener('keydown', escape)
    return () => window.removeEventListener('keydown', escape)
  }, [api, activity.view])
  // A trip moves the character without any pointer event, so pass-through is re-evaluated around it.
  useEffect(() => {
    if (tripping) { void api.command({ type: 'interactive', value: false }); return }
    const at = pointer.current
    if (!at) return
    const hit = document.elementFromPoint(at.x, at.y)?.closest('[data-pet-interactive]')
    void api.command({ type: 'interactive', value: !!hit })
  }, [api, tripping])
  if (!snapshot) return null
  return <>
    <div className={idle.idleClassName ? `desktop-pet-character ${idle.idleClassName}` : 'desktop-pet-character'} {...idle.idleAttributes}
      data-pet-interactive data-dragging={reaction.dragging} data-motion={snapshot.reducedMotion ? 'off' : 'on'}
      data-actions-side={position.x + position.size + 40 > window.innerWidth ? 'left' : 'right'}
      style={{ left: position.x, top: position.y }} onAnimationEnd={idle.onAnimationEnd}>
      <MascotIdleStage>
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
      </MascotIdleStage>
      {phase === 'pet' && <div className="desktop-pet-actions">
        <button className="desktop-pet-action" aria-label={t(open ? 'desktopPet.activity.hide' : 'desktopPet.activity.show')} aria-expanded={open}
          onClick={() => open ? dismiss() : activity.show()}><MessageSquare size={16} /></button>
        <button className="desktop-pet-action" aria-label={t('desktopPet.return')}
          onClick={() => void api.command({ type: 'return' })}><PanelTop size={16} /></button>
      </div>}
    </div>
    {open && <PetActivitySurface snapshot={snapshot} position={position} text={text}
      onHold={setHold} onDismiss={dismiss}
      onStop={() => { if (status) void api.command({ type: 'stop', turnId: status.turnId }) }}
      onReturn={() => void api.command({ type: 'return' })}
      onDecide={value => { if (status?.decision) void api.command({ type: 'decision', id: status.decision.id, value }) }}
      onLayout={height => void api.command({ type: 'layout', height })}
      onChange={value => { setText(value); void api.command({ type: 'edit', text: value, submit: false, revision: ++revision.current }) }}
      onSubmit={() => void api.command({ type: 'edit', text, submit: true, revision: ++revision.current })} />}
  </>
}
