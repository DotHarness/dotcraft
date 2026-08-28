import type { DesktopPluginComposerSurfaceContext } from '@dotcraft/plugin'
import type { CSSProperties, ReactNode } from 'react'

import { DesktopPluginSurface } from '../desktopPlugins/DesktopPluginSurface'
import styles from './ComposerSurfaceSlots.module.css'

interface ComposerToolbarLeadingSlotsProps {
  context: DesktopPluginComposerSurfaceContext
  commands?: ReactNode
  voiceStatus?: ReactNode
  permissions?: ReactNode
  mode?: ReactNode
  goal?: ReactNode
  compact?: boolean
}

interface ComposerToolbarTrailingSlotsProps {
  context: DesktopPluginComposerSurfaceContext
  contextUsage?: ReactNode
  model?: ReactNode
  voice?: ReactNode
  submit?: ReactNode
  style?: CSSProperties
}

interface ComposerStatusContentProps {
  context: DesktopPluginComposerSurfaceContext
  workspace?: ReactNode
  subscription?: ReactNode
  topSpacing?: boolean
}

export function ComposerToolbarLeadingSlots({
  context,
  commands,
  voiceStatus,
  permissions,
  mode,
  goal,
  compact = false
}: ComposerToolbarLeadingSlotsProps): JSX.Element {
  return (
    <div style={{
      display: 'flex',
      alignItems: 'center',
      gap: '10px',
      minWidth: 0,
      flex: compact ? 1 : undefined,
      flexWrap: compact ? 'nowrap' : 'wrap'
    }}>
      <DesktopPluginSurface name="composer.toolbar.commands" context={context}>
        {commands}
      </DesktopPluginSurface>
      {voiceStatus}
      <DesktopPluginSurface name="composer.toolbar.permissions" context={context}>
        {permissions}
      </DesktopPluginSurface>
      <DesktopPluginSurface name="composer.toolbar.mode" context={context}>
        {mode}
      </DesktopPluginSurface>
      <DesktopPluginSurface name="composer.toolbar.goal" context={context}>
        {goal}
      </DesktopPluginSurface>
    </div>
  )
}

export function ComposerToolbarTrailingSlots({
  context,
  contextUsage,
  model,
  voice,
  submit,
  style
}: ComposerToolbarTrailingSlotsProps): JSX.Element {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: '10px', ...style }}>
      <DesktopPluginSurface name="composer.toolbar.context-usage" context={context}>
        {contextUsage}
      </DesktopPluginSurface>
      <DesktopPluginSurface name="composer.toolbar.model" context={context}>
        {model}
      </DesktopPluginSurface>
      <DesktopPluginSurface name="composer.toolbar.voice" context={context}>
        {voice}
      </DesktopPluginSurface>
      <DesktopPluginSurface name="composer.toolbar.submit" context={context}>
        {submit}
      </DesktopPluginSurface>
    </div>
  )
}

export function ComposerStatusContent({
  context,
  workspace,
  subscription,
  topSpacing = true
}: ComposerStatusContentProps): JSX.Element {
  return (
    <div className={`${styles.status}${topSpacing ? ` ${styles.topSpacing}` : ''}`}>
      <div className={styles.workspace}>
        <DesktopPluginSurface name="composer.status.workspace" context={context}>
          {workspace}
        </DesktopPluginSurface>
        <DesktopPluginSurface name="composer.status.subscription" context={context}>
          {subscription}
        </DesktopPluginSurface>
      </div>
    </div>
  )
}
