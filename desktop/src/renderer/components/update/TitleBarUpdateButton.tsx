import type { CSSProperties, JSX } from 'react'
import { Download } from 'lucide-react'

import type { AppUpdateState } from '../../../shared/appUpdate'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'
import { IconButton } from '../ui/IconButton'
import { Spinner } from '../ui/Spinner'
import styles from './TitleBarUpdateButton.module.css'

const noDrag = { WebkitAppRegion: 'no-drag' } as CSSProperties

export function isTitleBarUpdateVisible(state: AppUpdateState): boolean {
  return Boolean(state.update) && (
    state.status === 'available' ||
    state.status === 'downloading' ||
    state.status === 'downloaded' ||
    state.status === 'preparing' ||
    state.status === 'error'
  )
}

export function TitleBarUpdateButton({
  state,
  onOpen
}: {
  state: AppUpdateState
  onOpen: () => void
}): JSX.Element {
  const t = useT()
  const label = tooltipLabel(state, t)

  if (state.status === 'downloaded') {
    return (
      <ActionTooltip label={label} placement="bottom" wrapperStyle={noDrag}>
        <button type="button" className={styles.pill} aria-label={label} onClick={onOpen}>
          <Download size={13} strokeWidth={1.8} aria-hidden="true" />
          <span>{t('update.pillLabel')}</span>
        </button>
      </ActionTooltip>
    )
  }

  const busy = state.status === 'downloading' || state.status === 'preparing'
  return (
    <IconButton
      size={28}
      label={label}
      tooltipLabel={label}
      tooltipPlacement="bottom"
      onClick={onOpen}
      className={styles.icon}
      style={noDrag}
      icon={(
        <>
          {busy ? <Spinner size={16} /> : <Download size={16} aria-hidden="true" />}
          {state.status === 'error' && <span className={styles.errorDot} aria-hidden="true" />}
        </>
      )}
    />
  )
}

function tooltipLabel(
  state: AppUpdateState,
  t: (key: string, vars?: Record<string, string | number>) => string
): string {
  if (state.status === 'downloading') return t('update.downloadingTooltip')
  if (state.status === 'downloaded') return t(state.autoInstall ? 'update.autoInstallTooltip' : 'update.downloadedTooltip')
  if (state.status === 'preparing') return t('update.preparing')
  if (state.status === 'error') return t('update.failedTooltip')
  return t('update.availableTooltip')
}
