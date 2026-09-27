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
    <div className={styles.leadingControls} data-compact={compact || undefined}>
      <DesktopPluginSurface name="composer.toolbar.commands" context={context}>
        {commands && <div className={styles.control} data-adaptive-control="command">{commands}</div>}
      </DesktopPluginSurface>
      {voiceStatus}
      <DesktopPluginSurface name="composer.toolbar.permissions" context={context}>
        {permissions && <div className={styles.control} data-adaptive-control="permissions">{permissions}</div>}
      </DesktopPluginSurface>
      <DesktopPluginSurface name="composer.toolbar.mode" context={context}>
        {mode && <div className={styles.control} data-adaptive-control="mode">{mode}</div>}
      </DesktopPluginSurface>
      <DesktopPluginSurface name="composer.toolbar.goal" context={context}>
        {goal && <div className={styles.control} data-adaptive-control="goal">{goal}</div>}
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
    <div className={styles.trailingControls} style={style}>
      <DesktopPluginSurface name="composer.toolbar.context-usage" context={context}>
        {contextUsage && <div className={styles.control} data-adaptive-control="context">{contextUsage}</div>}
      </DesktopPluginSurface>
      <DesktopPluginSurface name="composer.toolbar.model" context={context}>
        {model && <div className={styles.control} data-adaptive-control="model">{model}</div>}
      </DesktopPluginSurface>
      <DesktopPluginSurface name="composer.toolbar.voice" context={context}>
        {voice && <div className={styles.control} data-adaptive-control="voice">{voice}</div>}
      </DesktopPluginSurface>
      <DesktopPluginSurface name="composer.toolbar.submit" context={context}>
        {submit && <div className={styles.control} data-adaptive-control="submit">{submit}</div>}
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
          {subscription && <div className={styles.subscription}>{subscription}</div>}
        </DesktopPluginSurface>
      </div>
      <div className={styles.trailing}>
        <DesktopPluginSurface name="composer.status.trailing" context={context} />
      </div>
    </div>
  )
}
