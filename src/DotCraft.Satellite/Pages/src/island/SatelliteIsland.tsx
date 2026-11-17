import { useLayoutEffect, useRef, useState, type Ref } from 'react'
import type { AvatarPose } from '@dotcraft/avatar'
import {
  Island,
  IslandBadge,
  IslandBody,
  IslandButton,
  IslandChoices,
  IslandCountdown,
  IslandDetail,
  IslandHead,
  IslandRobots,
  type IslandBodyContent,
  type IslandRobot,
} from '@dotcraft/island/react'
import { Dish, Eye, Folder, Pause, Resume, Unplug } from './icons'
import type { IslandAction, IslandBounds, IslandMessage, IslandState } from './types'

export interface SatelliteIslandProps {
  state: IslandState
  onMessage: (message: IslandMessage) => void
  decisionRef?: Ref<HTMLButtonElement>
  defaultOpen?: boolean
}

const QUIET = new Set(['standby', 'paused', 'offline'])

function robots(state: IslandState): IslandRobot[] {
  const active = state.approval?.peerId ?? state.running?.peerId ?? state.peers[0]?.id
  return state.peers.map((peer) => ({ id: peer.id, name: peer.name, pose: pose(state, peer.id), lead: peer.id === active }))
}

function pose(state: IslandState, peerId: string): AvatarPose {
  if (state.approval?.peerId === peerId) return 'waiting'
  if (state.running?.peerId === peerId) return 'working'
  return 'idle'
}

function box(element: HTMLElement | null): IslandBounds | null {
  if (!element) return null
  const rect = element.getBoundingClientRect()
  return { x: rect.x, y: rect.y, width: rect.width, height: rect.height }
}

export function SatelliteIsland({ state: incoming, onMessage, decisionRef, defaultOpen = false }: SatelliteIslandProps) {
  const [held, setHeld] = useState(incoming)
  if (incoming.visible && incoming !== held) setHeld(incoming)
  const state = incoming.visible ? incoming : held
  const leaving = !incoming.visible
  const [open, setOpen] = useState(defaultOpen)
  const pill = useRef<HTMLDivElement>(null)
  const { running, approval, reduceMotion } = state
  const quiet = QUIET.has(state.mode)
  const animate = !reduceMotion
  const name = running ? state.peers.find((peer) => peer.id === running.peerId)?.name ?? state.label : state.label
  const send = (action: IslandAction, peer?: string) => () => onMessage({ type: 'action', name: action, peer })

  // The window polls the cursor against this box to decide where clicks pass through.
  useLayoutEffect(() => {
    const report = () => onMessage({ type: 'bounds', pill: leaving ? null : box(pill.current) })
    report()
    const node = pill.current
    if (!node) return undefined
    const observer = new ResizeObserver(report)
    observer.observe(node)
    return () => observer.disconnect()
  })

  const body: IslandBodyContent | null = approval
    ? {
        key: `approval:${approval.title}:${approval.targetFull}`,
        node: (
          <IslandChoices
            title={approval.title}
            detail={(
              <>
                <IslandCountdown fraction={approval.fraction} label={approval.countdown} />
                <IslandDetail lead={approval.operation} title={approval.targetFull}>{approval.target}</IslandDetail>
                {approval.queued > 0 && <IslandBadge>+{approval.queued}</IslandBadge>}
              </>
            )}
            choices={[
              { id: 'deny', label: approval.deny },
              { id: 'allow', label: approval.allow, primary: true },
            ]}
            onChoose={(id) => onMessage({ type: 'action', name: id as IslandAction })}
            firstRef={decisionRef}
          />
        ),
      }
    : open && !quiet
      ? {
          key: 'machines',
          node: (
            <ul className="sat-peers">
              {state.peers.map((peer) => (
                <li key={peer.id}>
                  <IslandRobots robots={[{ id: peer.id, name: peer.name, pose: pose(state, peer.id) }]} size={24} animate={animate} />
                  <span className="sat-peer">
                    <span className="sat-peer__name" title={peer.name}>{peer.name}</span>
                    <span className="sat-peer__meta" title={peer.meta}>{peer.meta}</span>
                  </span>
                  <IslandButton variant="icon" aria-label={`${state.disconnect} · ${peer.name}`} title={state.disconnect} onClick={send('disconnect', peer.id)}><Unplug /></IslandButton>
                  <IslandButton variant="icon" aria-label={`${state.openFolder} · ${peer.name}`} title={state.openFolder} disabled={!peer.canOpenFolder} onClick={send('openFolder', peer.id)}><Folder /></IslandButton>
                </li>
              ))}
            </ul>
          ),
        }
      : null

  return (
    <div className="sat-top">
      <Island
        ref={pill}
        className="sat-island"
        theme={state.theme}
        reduceMotion={reduceMotion}
        card={Boolean(body)}
        leaving={leaving}
        aria-label={state.summary}
        onPointerDown={() => onMessage({ type: 'press' })}
        onKeyDown={(event) => { if (event.key === 'Escape' && open) { event.preventDefault(); setOpen(false) } }}
      >
        <div className="sat-line">
          <IslandHead open={open} onToggle={() => setOpen((value) => !value)} disabled={quiet || Boolean(approval)} expandLabel={state.expand} collapseLabel={state.collapse}>
            {quiet
              ? <span className="sat-glyph"><Dish /></span>
              : <IslandRobots robots={robots(state)} animate={animate} />}
            <span className="sat-text" key={`${state.mode}:${running?.command ?? ''}`}>
              {quiet
                ? <span className="sat-quiet">{state.label}</span>
                : <strong className="sat-name" title={name}>{name}</strong>}
              {running && (
                <span className="sat-step">
                  <span className={animate ? 'dci-live' : undefined}>{running.operation}</span>
                  <code title={running.commandFull}>{running.command}</code>
                </span>
              )}
              {!running && !quiet && state.since && <span className="sat-step">{state.since}</span>}
            </span>
            {running && running.concurrency > 0 && <IslandBadge>+{running.concurrency}</IslandBadge>}
            {state.watching && <span className="sat-eye" title={state.watchingLabel}><Eye /></span>}
          </IslandHead>
          {!quiet && !approval && (
            <IslandButton variant="icon" aria-label={state.pause} title={state.pause} onClick={send('pause')}>
              <Pause />
            </IslandButton>
          )}
          {state.resume && (
            <IslandButton className="sat-resume" onClick={send('resume')}>
              <Resume />{state.resume}
            </IslandButton>
          )}
        </div>
        <IslandBody body={body} />
      </Island>
      <p className="sat-announce" role="status" aria-live="polite">{state.summary}</p>
    </div>
  )
}
