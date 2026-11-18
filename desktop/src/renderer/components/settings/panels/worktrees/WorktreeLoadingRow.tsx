import type { JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { useDelayedFlag } from '../../../../hooks/useDelayedFlag'
import { SkeletonList } from '../../../ui/Skeleton'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'

const PLACEHOLDER_DELAY_MS = 300

export function WorktreeLoadingRow({ standalone = false }: { standalone?: boolean }): JSX.Element | null {
  const t = useT()
  const visible = useDelayedFlag(true, PLACEHOLDER_DELAY_MS)
  if (!visible) return null

  const row = (
    <SettingsRow orientation="block">
      <SkeletonList count={1} ariaLabel={t('settings.loading')} />
    </SettingsRow>
  )
  return standalone ? <SettingsGroup>{row}</SettingsGroup> : row
}
