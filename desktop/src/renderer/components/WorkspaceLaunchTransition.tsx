import { useEffect, useState, type CSSProperties } from 'react'
import { useT } from '../contexts/LocaleContext'
import { DotCraftFullLogo } from './ui/DotCraftLogo'

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

export type WorkspaceSetupLogoHandoffPhase = 'hold' | 'move'

interface WorkspaceLaunchTransitionProps {
  phase: WorkspaceLaunchTransitionPhase
  from: LaunchLogoRect
  to: LaunchLogoRect
  logoSrc?: string
}

interface WorkspaceSetupLogoHandoffProps {
  phase: WorkspaceSetupLogoHandoffPhase
  from: LaunchLogoRect
  to: LaunchLogoRect
}

const LAUNCH_LOGO_BASE_SIZE = 96

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
        <div className="workspace-launch-transition__status tool-running-gradient-text">
          {phase === 'preparing'
            ? t('workspaceLaunch.preparing')
            : t('workspaceLaunch.connecting')}
        </div>
      )}
    </div>
  )
}

export function WorkspaceSetupLogoHandoff({
  phase,
  from,
  to
}: WorkspaceSetupLogoHandoffProps): JSX.Element {
  const style = {
    '--launch-logo-from-x': `${from.left}px`,
    '--launch-logo-from-y': `${from.top}px`,
    '--launch-logo-from-scale': String(from.width / LAUNCH_LOGO_BASE_SIZE),
    '--launch-logo-to-x': `${to.left}px`,
    '--launch-logo-to-y': `${to.top}px`,
    '--launch-logo-to-scale': String(to.width / LAUNCH_LOGO_BASE_SIZE)
  } as CSSProperties

  return (
    <div
      aria-hidden="true"
      className={`workspace-setup-logo-handoff workspace-setup-logo-handoff--${phase}`}
      style={style}
    >
      <DotCraftFullLogo size={LAUNCH_LOGO_BASE_SIZE} className="workspace-setup-logo-handoff__logo" />
    </div>
  )
}
