import { useEffect, type JSX, type ReactNode } from 'react'
import { RefreshCw, Trash2 } from 'lucide-react'

import { SettingsPageHeader } from '../../SettingsPageHeader'
import { SettingsBreadcrumb } from '../../SettingsBreadcrumb'
import { StatusMenuButton } from '../../../ui/StatusMenuButton'
import type { ContextMenuEntry } from '../../../ui/ContextMenu'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import { useLocale, useT } from '../../../../contexts/LocaleContext'
import { useSatellitesStore } from '../../../../stores/satellitesStore'
import type { AppLocale } from '../../../../../shared/locales'
import { satelliteState, type Satellite, type SatelliteEvent } from '../../../../../shared/satellites'
import { SATELLITE_MENU_TONE, SATELLITE_STATE_KEY } from './satellitesStatus'
import { folderLabel, formatClock, formatDay } from './satellitesFormat'

function DetailSection({ title, children }: { title: string; children: ReactNode }): JSX.Element {
  return (
    <section className="dc-satellite-section">
      <h3>{title}</h3>
      <div className="dc-satellite-section__body">{children}</div>
    </section>
  )
}

export function SatelliteDetail({
  satellite,
  onBack,
  onRefresh
}: {
  satellite: Satellite
  onBack: () => void
  onRefresh: () => void
}): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const confirm = useConfirmDialog()
  const loadActivity = useSatellitesStore((state) => state.loadActivity)
  const revoke = useSatellitesStore((state) => state.revoke)
  const revoking = useSatellitesStore((state) => state.revoking)
  const activity = useSatellitesStore((state) => state.activity[satellite.peerId])
  const state = satelliteState(satellite)

  useEffect(() => {
    void loadActivity(satellite.peerId)
  }, [loadActivity, satellite.peerId])

  const identity = [
    satellite.userName,
    satellite.osName,
    formatDay(satellite.enrolledAt, locale)
      ? t('settings.satellites.detail.joinedOn', { date: formatDay(satellite.enrolledAt, locale) as string })
      : null
  ].filter((part): part is string => Boolean(part))

  const menuItems: ContextMenuEntry[] = [
    {
      label: t('settings.connections.refresh'),
      icon: <RefreshCw size={14} aria-hidden />,
      onClick: () => {
        onRefresh()
        void loadActivity(satellite.peerId)
      }
    },
    { type: 'separator' },
    {
      label: t('settings.satellites.confirm.remove'),
      icon: <Trash2 size={14} aria-hidden />,
      danger: true,
      onClick: () => {
        void (async () => {
          const confirmed = await confirm({
            title: t('settings.satellites.confirm.removeTitle', { name: satellite.displayName }),
            message: t('settings.satellites.confirm.removeMessage'),
            confirmLabel: t('settings.satellites.confirm.remove'),
            danger: true
          })
          if (!confirmed) return
          const removed = await revoke(satellite.peerId)
          if (removed) onBack()
        })()
      }
    }
  ]

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <SettingsPageHeader
        title={satellite.displayName}
        breadcrumb={
          <SettingsBreadcrumb
            parentLabel={t('settings.connections.segments.satellites')}
            currentLabel={satellite.displayName}
            onBack={onBack}
          />
        }
        description={
          <span className="dc-satellite-detail__identity">
            {identity.map((part, index) => (
              <span key={`${index}-${part}`}>
                {index > 0 ? ' · ' : null}
                {index === 0 ? <span className="dc-satellite-detail__account">{part}</span> : part}
              </span>
            ))}
          </span>
        }
        action={
          <StatusMenuButton
            label={t(SATELLITE_STATE_KEY[state])}
            tone={SATELLITE_MENU_TONE[state]}
            items={menuItems}
            loading={revoking === satellite.peerId}
          />
        }
      />

      <div className="dc-satellite-detail">
        <DetailSection title={t('settings.satellites.detail.folders')}>
          {satellite.workspaces.length === 0 ? (
            <p className="dc-satellite-detail__empty">{t('settings.satellites.detail.noFolders')}</p>
          ) : (
            satellite.workspaces.map((workspace) => (
              <div className="dc-satellite-folder" key={workspace.workspaceId}>
                <span className="dc-satellite-folder__text">
                  <span className="dc-satellite-folder__name">
                    {folderLabel(workspace.path, workspace.workspaceId)}
                  </span>
                  {workspace.path ? (
                    <span className="dc-satellite-folder__path">{workspace.path}</span>
                  ) : null}
                </span>
                {workspace.busy ? (
                  <span className="dc-satellite-folder__note">
                    {t(`settings.satellites.detail.inUse.${workspace.busyOwner === 'self' ? 'self' : 'other'}`)}
                  </span>
                ) : null}
              </div>
            ))
          )}
        </DetailSection>

        <DetailSection title={t('settings.satellites.detail.activity')}>
          <ActivityList events={activity ?? []} locale={locale} />
        </DetailSection>
      </div>
    </div>
  )
}

function ActivityList({ events, locale }: { events: SatelliteEvent[]; locale: AppLocale }): JSX.Element {
  const t = useT()
  if (events.length === 0) {
    return <p className="dc-satellite-detail__empty">{t('settings.satellites.detail.noActivity')}</p>
  }
  return (
    <ul className="dc-satellite-activity">
      {events.slice(0, 10).map((event, index) => (
        <li key={`${event.kind}-${event.at}-${index}`}>
          <span>{formatClock(event.at, locale)}</span>
          <span aria-hidden>·</span>
          <span>{t(`settings.satellites.detail.event.${event.kind}`)}</span>
        </li>
      ))}
    </ul>
  )
}
