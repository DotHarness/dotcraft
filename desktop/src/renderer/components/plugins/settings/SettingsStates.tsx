import type { JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { SettingsGroup, SettingsRow } from '../../settings/SettingsGroup'
import { settingsErrorTextStyle } from '../../settings/settingsTypography'
import { Button } from '../../ui/Button'
import { Skeleton, SkeletonRow } from '../../ui/Skeleton'
import styles from './PluginSettings.module.css'

const SKELETON_ROWS: Array<[string, string]> = [['28%', '46%'], ['22%', '58%'], ['34%', '40%']]

export function SettingsSkeleton({ title }: { title?: string }): JSX.Element {
  const t = useT()
  return (
    <div role="status" aria-busy="true" aria-label={t('plugins.settings.loading')}>
      <SettingsGroup title={title}>
        {SKELETON_ROWS.map(([label, hint]) => (
          <SettingsRow key={label}>
            <SkeletonRow lines={[label, hint]} style={{ flex: 1 }} />
            <Skeleton width={96} height={28} radius={8} />
          </SettingsRow>
        ))}
      </SettingsGroup>
    </div>
  )
}

export function FailedNotice({ loading, onRetry }: { loading: boolean; onRetry: () => void }): JSX.Element {
  const t = useT()
  return (
    <div className={styles.failed} role="alert">
      <span style={settingsErrorTextStyle(false)}>{t('plugins.settings.failed')}</span>
      <Button size="sm" disabled={loading} onClick={onRetry}>{t('plugins.settings.retry')}</Button>
    </div>
  )
}
