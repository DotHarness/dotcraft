import { useEffect, useLayoutEffect, useRef, useState, type CSSProperties } from 'react'
import { PET_RETURN_DURATION, samplePetReturn } from '../../shared/desktopPetMotion'
import { useT } from '../contexts/LocaleContext'
import { DotCraftFullLogo } from './ui/DotCraftLogo'
import { RunningShimmer } from './ui/RunningShimmer'

export interface LaunchLogoRect {
  left: number
  top: number
  width: number
  height: number
}

export type WorkspaceLaunchTransitionPhase =
  | 'welcome-hold'
  | 'welcome-to-center'
  | 'connecting'
  | 'setup-handoff'
  | 'setup-complete-to-center'
  | 'preparing'
  | 'main-reveal'
  | 'error-reveal'

interface WorkspaceLaunchTransitionProps {
  phase: WorkspaceLaunchTransitionPhase
  from: LaunchLogoRect
  to: LaunchLogoRect
  logoSrc?: string
}

interface WorkspaceSetupLogoHopProps {
  from: LaunchLogoRect
  to: LaunchLogoRect
  logoSrc?: string
  onDone: () => void
}

const LAUNCH_LOGO_BASE_SIZE = 96
export const SETUP_LOGO_HOP_MS = PET_RETURN_DURATION
const HOP_LAUNCH = 80 / PET_RETURN_DURATION
const HOP_TOUCHDOWN = 600 / PET_RETURN_DURATION
const HOP_VIEWPORT_EDGE = 8

export function elementToLaunchLogoRect(node: HTMLElement | null): LaunchLogoRect | null {
  if (!node) return null
  const rect = node.getBoundingClientRect()
  return {
    left: Math.round(rect.left),
    top: Math.round(rect.top),
    width: Math.round(rect.width),
    height: Math.round(rect.height)
  }
}

export function centeredLaunchLogoRect(size = LAUNCH_LOGO_BASE_SIZE): LaunchLogoRect {
  const viewportWidth = window.innerWidth || document.documentElement.clientWidth || 0
  const viewportHeight = window.innerHeight || document.documentElement.clientHeight || 0
  return {
    left: Math.round((viewportWidth - size) / 2),
    top: Math.round((viewportHeight - size) / 2),
    width: size,
    height: size
  }
}

export function WorkspaceLaunchTransition({
  phase,
  from,
  to,
  logoSrc
}: WorkspaceLaunchTransitionProps): JSX.Element {
  const t = useT()
  const [centerRect, setCenterRect] = useState(() => centeredLaunchLogoRect())

  useEffect(() => {
    const updateCenterRect = (): void => {
      setCenterRect(centeredLaunchLogoRect())
    }

    window.addEventListener('resize', updateCenterRect)
    return () => window.removeEventListener('resize', updateCenterRect)
  }, [])

  const centerFrom =
    phase === 'connecting' ||
    phase === 'preparing' ||
    phase === 'main-reveal' ||
    phase === 'error-reveal'
  const centerTo =
    centerFrom ||
    phase === 'welcome-to-center' ||
    phase === 'setup-complete-to-center'
  const resolvedFrom = centerFrom ? centerRect : from
  const resolvedTo = centerTo ? centerRect : to
  const style = {
    '--launch-logo-from-x': `${resolvedFrom.left}px`,
    '--launch-logo-from-y': `${resolvedFrom.top}px`,
    '--launch-logo-from-scale': String(resolvedFrom.width / LAUNCH_LOGO_BASE_SIZE),
    '--launch-logo-to-x': `${resolvedTo.left}px`,
    '--launch-logo-to-y': `${resolvedTo.top}px`,
    '--launch-logo-to-scale': String(resolvedTo.width / LAUNCH_LOGO_BASE_SIZE)
  } as CSSProperties

  return (
    <div
      aria-hidden="true"
      className={`workspace-launch-transition workspace-launch-transition--${phase}`}
      style={style}
    >
      <div className="workspace-launch-transition__scrim" />
      {logoSrc ? (
        <img
          src={logoSrc}
          alt=""
          width={LAUNCH_LOGO_BASE_SIZE}
          height={LAUNCH_LOGO_BASE_SIZE}
          draggable={false}
          className="workspace-launch-transition__logo"
        />
      ) : (
        <DotCraftFullLogo size={LAUNCH_LOGO_BASE_SIZE} className="workspace-launch-transition__logo" />
      )}
      {(phase === 'connecting' || phase === 'preparing') && (
        <RunningShimmer as="div" className="workspace-launch-transition__status">
          {phase === 'preparing'
            ? t('workspaceLaunch.preparing')
            : t('workspaceLaunch.connecting')}
        </RunningShimmer>
      )}
    </div>
  )
}

export function WorkspaceSetupLogoHandoff({ from }: { from: LaunchLogoRect }): JSX.Element {
  const style = {
    '--launch-logo-from-x': `${from.left}px`,
    '--launch-logo-from-y': `${from.top}px`,
    '--launch-logo-from-scale': String(from.width / LAUNCH_LOGO_BASE_SIZE)
  } as CSSProperties

  return (
    <div aria-hidden="true" className="workspace-setup-logo-handoff" style={style}>
      <DotCraftFullLogo size={LAUNCH_LOGO_BASE_SIZE} className="workspace-setup-logo-handoff__logo" />
    </div>
  )
}

function ballistic(start: number, end: number, rise: number): (flight: number) => number {
  const apex = Math.min(start, end) - rise
  const up = Math.sqrt(start - apex)
  const down = Math.sqrt(end - apex)
  const peakAt = up / (up + down)
  const curve = (start - apex) / (peakAt * peakAt)
  return (flight) => apex + curve * (flight - peakAt) * (flight - peakAt)
}

export function WorkspaceSetupLogoHop({ from, to, logoSrc, onDone }: WorkspaceSetupLogoHopProps): JSX.Element {
  const node = useRef<HTMLDivElement | null>(null)
  const done = useRef(onDone)
  done.current = onDone

  useLayoutEffect(() => {
    const element = node.current
    if (!element) return
    const reduced = typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches
    const start = { x: from.left, y: from.top }
    const end = { x: to.left, y: to.top }
    const distance = Math.hypot(end.x - start.x, end.y - start.y)
    const rise = Math.max(4, Math.min(Math.min(start.y, end.y) - HOP_VIEWPORT_EDGE, Math.max(28, distance * 0.12)))
    const height = ballistic(start.y, end.y, rise)
    const began = performance.now()
    let frame = 0
    const paint = (now: number): void => {
      const progress = reduced ? 1 : Math.min(1, (now - began) / SETUP_LOGO_HOP_MS)
      const pose = samplePetReturn(start, end, progress)
      const flight = Math.max(0, Math.min(1, (progress - HOP_LAUNCH) / (HOP_TOUCHDOWN - HOP_LAUNCH)))
      const x = start.x + (end.x - start.x) * flight
      const y = progress <= HOP_LAUNCH ? start.y : progress >= HOP_TOUCHDOWN ? end.y : height(flight)
      const size = from.width + (to.width - from.width) * flight
      element.style.width = `${size}px`
      element.style.height = `${size}px`
      element.style.transform = `translate(${x}px, ${y}px) rotate(${pose.rotation}deg) scale(${pose.scaleX}, ${pose.scaleY})`
      if (progress < 1) frame = requestAnimationFrame(paint)
      else done.current()
    }
    paint(began)
    return () => cancelAnimationFrame(frame)
  }, [from, to])

  return (
    <div ref={node} className="workspace-setup-logo-hop" aria-hidden="true">
      {logoSrc ? (
        <img src={logoSrc} alt="" draggable={false} className="workspace-setup-logo-hop__logo" />
      ) : (
        <DotCraftFullLogo size={LAUNCH_LOGO_BASE_SIZE} className="workspace-setup-logo-hop__logo" />
      )}
    </div>
  )
}
