import type { JSX } from 'react'
import { ChevronRight, Plus, Server } from 'lucide-react'

import { SettingsGroup } from '../../SettingsGroup'
import { SettingsDescriptionWithLearnMore } from '../../SettingsLearnMoreLink'
import { Button } from '../../../ui/Button'
import { useT } from '../../../../contexts/LocaleContext'
import { useRemoteServersStore } from '../../../../stores/remoteServersStore'
import type { RemoteHost } from '../../../../../shared/remoteServers'
import { StatusText, reachabilityView } from './serversStatus'
import * as s from './serversStyles'

export function ServerList({
  hosts,
  onOpen,
  onAdd
}: {
  hosts: RemoteHost[]
  onOpen: (id: string) => void
  onAdd: () => void
}): JSX.Element {
  const t = useT()
  const activeStack = useRemoteServersStore((st) => st.activeStack)

  if (hosts.length === 0) {
    return (
      <div style={s.emptyBox}>
        <span
          style={{
            display: 'inline-flex',
            width: 44,
            height: 44,
            alignItems: 'center',
            justifyContent: 'center',
            borderRadius: 12,
            background: 'var(--bg-tertiary)',
            color: 'var(--text-secondary)',
            marginBottom: 6
          }}
        >
          <Server size={22} />
        </span>
        <div style={{ fontSize: 14, fontWeight: 600 }}>{t('settings.servers.list.emptyTitle')}</div>
        <div style={{ maxWidth: '44ch', color: 'var(--text-secondary)', fontSize: 12.5 }}>
          {t('settings.servers.list.emptyHint')}
        </div>
        <Button variant="primary" onClick={onAdd} iconLeft={<Plus size={15} />} style={{ marginTop: 14 }}>
          {t('settings.servers.addServer')}
        </Button>
      </div>
    )
  }

  return (
    <SettingsGroup
      title={t('settings.group.servers')}
      description={
        <SettingsDescriptionWithLearnMore topic="servers" aboutKey="settings.servers.title">
          {t('settings.servers.description')}
        </SettingsDescriptionWithLearnMore>
      }
      headerAction={
        <Button variant="primary" onClick={onAdd} iconLeft={<Plus size={15} />}>
          {t('settings.servers.addServer')}
        </Button>
      }
    >
      {hosts.map((host, index) => {
        const reach = reachabilityView(host.id, t)
        const activeHere = activeStack?.hostId === host.id
        const stackCountLabel = t(
          host.stacks.length === 1 ? 'settings.servers.list.stackCount.one' : 'settings.servers.list.stackCount.other',
          { count: host.stacks.length }
        )
        return (
          <button
            key={host.id}
            style={{ ...s.serverRow, borderTop: index === 0 ? 'none' : '1px solid var(--border-default)' }}
            onClick={() => onOpen(host.id)}
          >
            <span style={s.serverRowIcon}>
              <Server size={17} />
            </span>
            <span style={{ flex: 1, minWidth: 0 }}>
              <span style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13.5, fontWeight: 600 }}>
                {host.name}
                <StatusText tone={reach.tone}>{reach.label}</StatusText>
                {activeHere && (
                  <span style={{ fontSize: 11, fontWeight: 600, color: 'var(--accent)' }}>
                    {t('settings.servers.list.activeHere')}
                  </span>
                )}
              </span>
              <span
                style={{
                  display: 'block',
                  marginTop: 3,
                  color: 'var(--text-dimmed)',
                  fontSize: 12,
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap'
                }}
              >
                {host.sshTarget} · {stackCountLabel}
              </span>
            </span>
            <span style={{ color: 'var(--text-dimmed)', display: 'inline-flex' }}>
              <ChevronRight size={18} />
            </span>
          </button>
        )
      })}
    </SettingsGroup>
  )
}
