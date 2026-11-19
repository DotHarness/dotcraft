import type { JSX, ReactNode } from 'react'
import { RefreshCw } from 'lucide-react'
import type { ImportSettings } from '@dotcraft/sdk/contracts'
import { useLocale, useT } from '../../../contexts/LocaleContext'
import { importSourceLabel } from '../../../utils/sessionImport'
import { Button } from '../../ui/Button'
import { PillSwitch } from '../../ui/PillSwitch'
import { Skeleton } from '../../ui/Skeleton'
import { SettingsEmptyRow, SettingsGroup, SettingsRow } from '../SettingsGroup'
import { settingsHintStyle, settingsLabelStyle } from '../settingsTypography'
import { ImportSourceIcon } from './ImportSourceIcon'
import { syncSelectionSummary } from './importPresentation'
import styles from './ImportPanel.module.css'

export interface RunningImport {
  source: string | null
  completed: number
  total: number
}

interface SourceRow {
  source: string
  available: boolean
  items: readonly unknown[]
  importableCount: number
}

export function ImportSyncControls({ settings, settingsError, savingSync, local, onSyncChange, onCustomize }: {
  settings: ImportSettings | null
  settingsError: string | null
  savingSync: boolean
  local?: boolean
  onSyncChange: (next: boolean) => void
  onCustomize: () => void
}): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const optOut = !local && settings?.workspaceOptOut === true
  let description: ReactNode = t(local ? 'settings.import.local.syncDescription' : 'settings.import.sync.description')
  if (!settings && settingsError) {
    description = <span className={styles.error}>{t('settings.import.sync.loadFailed', { error: settingsError })}</span>
  } else if (optOut) {
    description = t('settings.import.sync.optOut')
  } else if (settings?.hasImported && !settings.syncEnabled) {
    description = t('settings.import.sync.paused')
  }

  return (
    <SettingsGroup title={t('settings.import.sync.group')}>
      <SettingsRow
        label={t('settings.import.sync.label')}
        description={description}
        control={
          <PillSwitch
            checked={settings?.syncEnabled === true && !optOut}
            disabled={!settings || optOut || savingSync}
            aria-busy={savingSync}
            aria-label={t('settings.import.sync.label')}
            onChange={onSyncChange}
          />
        }
      />
      <SettingsRow
        label={t('settings.import.setup.syncContent')}
        description={settings ? syncSelectionSummary(settings, locale, t) : undefined}
        control={
          <Button disabled={!settings?.hasImported} onClick={onCustomize}>
            {t('settings.import.setup.customize')}
          </Button>
        }
      />
    </SettingsGroup>
  )
}

export function ImportSources({ sources, detecting, detectError, running, lastSyncAt, local, onRefresh, onSelect }: {
  sources: readonly SourceRow[] | null
  detecting: boolean
  detectError: string | null
  running: RunningImport | null
  lastSyncAt?: string
  local?: boolean
  onRefresh: () => void
  onSelect: (source: string) => void
}): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const available = sources?.filter(entry => entry.available) ?? []
  const hasImportable = available.some(entry => entry.importableCount > 0)
  const runningElsewhere = running != null && !available.some(entry => entry.source === running.source)
  let status: { text: string; error?: boolean } | null = null
  if (detecting) {
    status = { text: t('settings.import.sources.checking') }
  } else if (runningElsewhere) {
    status = { text: t('settings.import.sources.importing') }
  } else if (detectError && sources) {
    status = { text: t('settings.import.sources.loadFailed', { error: detectError }), error: true }
  } else {
    const parts: string[] = []
    if (available.length > 0 && !hasImportable) parts.push(t('settings.import.sources.none'))
    if (lastSyncAt) {
      const time = new Intl.DateTimeFormat(locale, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })
        .format(new Date(lastSyncAt))
      parts.push(t('settings.import.sources.lastSynced', { time }))
    }
    if (parts.length > 0) status = { text: parts.join(' · ') }
  }

  return (
    <SettingsGroup
      title={t('settings.import.sources.group')}
      headerAction={
        <div className={styles.headerStatus}>
          <span role="status" className={styles.status} data-tone={status?.error ? 'error' : undefined} title={status?.text}>
            {status?.text}
          </span>
          <Button iconLeft={<RefreshCw size={15} aria-hidden />} disabled={detecting} onClick={onRefresh}>
            {t('settings.import.sources.checkAgain')}
          </Button>
        </div>
      }
    >
      {sources == null && detectError ? (
        <SettingsEmptyRow tone="error">{t('settings.import.sources.loadFailed', { error: detectError })}</SettingsEmptyRow>
      ) : sources == null ? (
        [0, 1].map(index => (
          <SettingsRow
            key={index}
            label={<Skeleton width={index === 0 ? '28%' : '20%'} height={13} />}
            description={<Skeleton width="40%" height={11} />}
            control={<Skeleton width={72} height={28} radius={10} />}
          />
        ))
      ) : available.length === 0 ? (
        <SettingsEmptyRow>{t('settings.import.sources.empty')}</SettingsEmptyRow>
      ) : (
        available.map(entry => {
          const label = importSourceLabel(entry.source)
          const progress = running?.source === entry.source ? running : null
          const count = entry.importableCount
          const statusText = progress
            ? local && progress.total === 0
              ? t('settings.import.sources.importing')
              : t('settings.import.source.progress', { completed: progress.completed, total: progress.total })
            : count === 0
              ? t('settings.import.source.none')
              : t(count === 1 ? 'settings.import.source.ready.one' : 'settings.import.source.ready.other', { count })

          return (
            <SettingsRow key={entry.source}>
              <div className={styles.sourceRow}>
                <ImportSourceIcon source={entry.source} />
                <div className={styles.sourceText}>
                  <span style={settingsLabelStyle()}>{label}</span>
                  <span style={settingsHintStyle()} aria-live={progress ? 'polite' : undefined}>{statusText}</span>
                </div>
                <Button
                  aria-label={t('settings.import.source.importFrom', { source: label })}
                  loading={progress != null}
                  disabled={entry.items.length === 0 || running != null || (local && (detecting || count === 0))}
                  onClick={() => onSelect(entry.source)}
                >
                  {t('settings.import.source.import')}
                </Button>
              </div>
            </SettingsRow>
          )
        })
      )}
    </SettingsGroup>
  )
}
