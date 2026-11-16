import { useEffect, type JSX, type ReactNode } from 'react'
import { RefreshCw, Trash2 } from 'lucide-react'

import { SettingsPanelShell, SettingsBreadcrumb, Menu, Button } from '@dotcraft/plugin'
import type { MenuEntry } from '@dotcraft/plugin'
import { satelliteHost } from '../runtime'
import { useLocale, useT } from '../i18n'
import { useSatellitesStore } from '../state'
import type { AppLocale, Satellite, SatelliteEvent } from '../types'
import { satelliteState } from '../model'
import { SATELLITE_STATE_KEY, SatelliteStatusText } from './Status'
import { folderLabel, formatClock, formatDay } from './format'

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
  const confirm = satelliteHost().ui.confirm
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

  const menuItems: MenuEntry[] = [
    {
      label: t('settings.connections.refresh'),
      icon: <RefreshCw size={14} aria-hidden />,
      onSelect: () => {
        onRefresh()
        void loadActivity(satellite.peerId)
      }
    },
    { type: 'separator' },
    {
      label: t('settings.satellites.confirm.remove'),
      icon: <Trash2 size={14} aria-hidden />,
      danger: true,
      onSelect: () => {
        void (async () => {
          const confirmed = await confirm({
            title: t('settings.satellites.confirm.removeTitle', { name: satellite.displayName }),
            message: t('settings.satellites.confirm.removeMessage'),
            confirmLabel: t('settings.satellites.confirm.remove'),
            danger: true,
            cancelLabel: t('common.cancel')
          })
          if (!confirmed) return
          const removed = await revoke(satellite.peerId)
          if (removed) onBack()
        })()
      }
    }
  ]

  return (
    <SettingsPanelShell
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
          <Menu items={menuItems}>
            <Button variant="ghost" disabled={revoking === satellite.peerId} aria-label={t(SATELLITE_STATE_KEY[state])}>
              <SatelliteStatusText state={state}>{t(SATELLITE_STATE_KEY[state])}</SatelliteStatusText>
              <span aria-hidden>⋯</span>
            </Button>
          </Menu>
        }
      >
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
    </SettingsPanelShell>
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
