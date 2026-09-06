import { useState, type JSX } from 'react'
import { Loader2, Search, Server } from 'lucide-react'

import { SettingsPanelShell } from '../../SettingsPanelShell'
import { SettingsBreadcrumb } from '../../SettingsBreadcrumb'
import { SettingsGroup } from '../../SettingsGroup'
import { Button } from '../../../ui/Button'
import { Input } from '../../../ui/Input'
import { useT } from '../../../../contexts/LocaleContext'
import { useRemoteServersStore } from '../../../../stores/remoteServersStore'
import {
  isValidComposeProjectName,
  type DiscoveredStack,
  type RemoteHost,
  type RemoteStack
} from '../../../../../shared/remoteServers'
import * as s from './serversStyles'

interface StackFormProps {
  host: RemoteHost
  stack?: RemoteStack
  onBack: () => void
  onSaved: (host: RemoteHost) => void
}

export function StackFormPage({
  host,
  stack,
  onBack,
  onSaved
}: StackFormProps): JSX.Element {
  const t = useT()
  const store = useRemoteServersStore()
  const [name, setName] = useState(stack?.name ?? '')
  const [composeDir, setComposeDir] = useState(stack?.composeDir ?? '')
  const [workspaceDir, setWorkspaceDir] = useState(stack?.workspaceDir ?? '')
  const [appServerWorkspacePath, setAppServerWorkspacePath] = useState(stack?.appServerWorkspacePath ?? '')
  const [composeProjectName, setComposeProjectName] = useState(stack?.composeProjectName ?? '')
  const [appServerPort, setAppServerPort] = useState(String(stack?.appServerPort ?? 9100))
  const [oratorioPort, setOratorioPort] = useState(String(stack?.oratorioPort ?? 5087))
  const [dashboardPort, setDashboardPort] = useState(String(stack?.dashboardPort ?? 8080))
  const [sandbox, setSandbox] = useState(stack?.sandboxProfile ?? false)
  const [discoveredStacks, setDiscoveredStacks] = useState<DiscoveredStack[]>([])
  const [discoveryRan, setDiscoveryRan] = useState(false)

  const editing = Boolean(stack)
  const canSave =
    name.trim().length > 0 &&
    composeDir.trim().length > 0 &&
    (!composeProjectName.trim() || isValidComposeProjectName(composeProjectName))
  const discovering = store.discovering[host.id]

  const applyDiscoveredStack = (candidate: DiscoveredStack): void => {
    setName(candidate.name)
    setComposeDir(candidate.composeDir)
    setWorkspaceDir(candidate.workspaceDir ?? '')
    setAppServerWorkspacePath(candidate.appServerWorkspacePath ?? '')
    setComposeProjectName(candidate.composeProjectName ?? '')
    setAppServerPort(String(candidate.appServerPort || 9100))
    setOratorioPort(String(candidate.oratorioPort || 5087))
    setDashboardPort(String(candidate.dashboardPort || 8080))
    setSandbox(candidate.sandboxProfile)
  }

  const discoveryKey = (candidate: Pick<DiscoveredStack, 'composeDir' | 'composeProjectName'>): string =>
    `${candidate.composeProjectName ?? ''}\u0000${candidate.composeDir}`

  const handleDiscover = async (): Promise<void> => {
    setDiscoveryRan(true)
    const existing = new Set(
      host.stacks
        .filter((st) => st.id !== stack?.id)
        .map((st) => discoveryKey({ composeDir: st.composeDir, composeProjectName: st.composeProjectName }))
    )
    const candidates = (await store.discoverStacks(host.id)).filter((candidate) => !existing.has(discoveryKey(candidate)))
    setDiscoveredStacks(candidates)
    if (candidates.length === 1 && !editing && !name.trim() && !composeDir.trim()) {
      applyDiscoveredStack(candidates[0])
    }
  }

  const handleSave = async (): Promise<void> => {
    const next: RemoteStack = {
      id: stack?.id ?? '',
      name: name.trim(),
      composeDir: composeDir.trim(),
      workspaceDir: workspaceDir.trim() || undefined,
      appServerWorkspacePath: appServerWorkspacePath.trim() || undefined,
      composeProjectName: composeProjectName.trim() || undefined,
      appServerPort: Number(appServerPort) || 9100,
      oratorioPort: Number(oratorioPort) || 5087,
      dashboardPort: Number(dashboardPort) || 8080,
      sandboxProfile: sandbox
    }
    const stacks = editing
      ? host.stacks.map((st) => (st.id === stack!.id ? next : st))
      : [...host.stacks, next]
    const updated = await store.updateHost(host.id, { stacks })
    if (updated) onSaved(updated)
  }

  const portInput = (value: string, set: (v: string) => void): JSX.Element => (
    <Input mono value={value} onChange={(e) => set(e.target.value)} />
  )

  return (
    <SettingsPanelShell
      title={editing ? t('settings.servers.stack.editTitle') : t('settings.servers.stack.addTitle')}
      description={t('settings.servers.stack.description', { server: host.name })}
      breadcrumb={
        <SettingsBreadcrumb
          parentLabel={host.name}
          currentLabel={editing ? t('settings.servers.stack.editTitle') : t('settings.servers.stack.addTitle')}
          onBack={onBack}
        />
      }
    >
      <SettingsGroup title={t('settings.servers.stack.deployment')} flush>
        <div style={s.discoveryPanel}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
            <div style={{ flex: 1, minWidth: 220 }}>
              <div style={{ fontSize: 12.5, fontWeight: 600 }}>{t('settings.servers.stack.discoverTitle')}</div>
              <div style={s.fieldHint}>{t('settings.servers.stack.discoverHint')}</div>
            </div>
            <Button
              disabled={discovering}
              onClick={handleDiscover}
              iconLeft={discovering ? <Loader2 size={15} className="animate-spin-custom" /> : <Search size={15} />}
            >
              {t('settings.servers.stack.discover')}
            </Button>
          </div>

          {discoveredStacks.length > 0 && (
            <div style={{ ...s.choiceGrid, marginTop: 10 }}>
              {discoveredStacks.map((candidate) => (
                <button
                  key={discoveryKey(candidate)}
                  type="button"
                  style={s.choiceButton}
                  onClick={() => applyDiscoveredStack(candidate)}
                >
                  <span style={s.choiceIcon}>
                    <Server size={15} />
                  </span>
                  <span style={{ minWidth: 0 }}>
                    <span style={s.choiceTitle}>{candidate.name}</span>
                    <span style={s.choiceSubtitle}>
                      {candidate.composeDir}
                      {candidate.composeProjectName ? ` · ${candidate.composeProjectName}` : ''}
                    </span>
                  </span>
                </button>
              ))}
            </div>
          )}

          {discoveryRan && !discovering && discoveredStacks.length === 0 && (
            <div style={{ ...s.mutedText, marginTop: 10 }}>{t('settings.servers.stack.discoverEmpty')}</div>
          )}
        </div>

        <div style={s.formGrid}>
          <div>
            <label style={s.fieldLabel}>{t('settings.servers.stack.name')}</label>
            <Input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder={t('settings.servers.stack.namePlaceholder')}
            />
          </div>
          <div>
            <label style={s.fieldLabel}>{t('settings.servers.stack.deploymentFolder')}</label>
            <Input
              mono
              value={composeDir}
              onChange={(e) => setComposeDir(e.target.value)}
              placeholder={t('settings.servers.stack.deploymentPlaceholder')}
            />
            <div style={s.fieldHint}>{t('settings.servers.stack.deploymentHint')}</div>
          </div>
          <div>
            <label style={s.fieldLabel}>
              {t('settings.servers.stack.dataFolder')}{' '}
              <span style={{ color: 'var(--text-dimmed)', fontWeight: 400 }}>
                ({t('settings.servers.optional')})
              </span>
            </label>
            <Input
              mono
              value={workspaceDir}
              onChange={(e) => setWorkspaceDir(e.target.value)}
              placeholder={t('settings.servers.stack.dataPlaceholder')}
            />
          </div>
          <div>
            <label style={s.fieldLabel}>
              {t('settings.servers.stack.composeProjectName')}{' '}
              <span style={{ color: 'var(--text-dimmed)', fontWeight: 400 }}>
                ({t('settings.servers.optional')})
              </span>
            </label>
            <Input
              mono
              value={composeProjectName}
              onChange={(e) => setComposeProjectName(e.target.value)}
            />
            <div style={s.fieldHint}>{t('settings.servers.stack.composeProjectNameHint')}</div>
          </div>
        </div>
      </SettingsGroup>

      <SettingsGroup title={t('settings.servers.stack.ports')} flush>
        <div style={s.twoColumnGrid}>
          <div>
            <label style={s.fieldLabel}>{t('settings.servers.stack.appServerPort')}</label>
            {portInput(appServerPort, setAppServerPort)}
          </div>
          <div>
            <label style={s.fieldLabel}>{t('settings.servers.stack.oratorioPort')}</label>
            {portInput(oratorioPort, setOratorioPort)}
          </div>
          <div>
            <label style={s.fieldLabel}>{t('settings.servers.stack.dashboardPort')}</label>
            {portInput(dashboardPort, setDashboardPort)}
          </div>
        </div>
      </SettingsGroup>

      <SettingsGroup title={t('settings.servers.stack.runtime')} flush>
        <div style={s.switchRow}>
          <div>
            <div style={{ fontSize: 12.5, fontWeight: 600 }}>{t('settings.servers.stack.sandbox')}</div>
            <div style={{ ...s.fieldHint, marginTop: 3 }}>{t('settings.servers.stack.sandboxHint')}</div>
          </div>
          <button
            role="switch"
            aria-checked={sandbox}
            onClick={() => setSandbox((v) => !v)}
            style={{
              width: 38,
              height: 22,
              borderRadius: 999,
              position: 'relative',
              cursor: 'pointer',
              border: sandbox ? '1px solid var(--accent)' : '1px solid var(--border-default)',
              background: sandbox ? 'var(--accent)' : 'var(--bg-active)'
            }}
          >
            <span
              style={{
                position: 'absolute',
                top: 2,
                left: 2,
                width: 16,
                height: 16,
                borderRadius: '50%',
                background: sandbox ? 'var(--on-accent)' : 'var(--text-secondary)',
                transform: sandbox ? 'translateX(16px)' : 'translateX(0)',
                transition: 'transform 120ms ease'
              }}
            />
          </button>
        </div>
      </SettingsGroup>

      <div style={s.formActions}>
        <span style={{ flex: 1 }} />
        <Button onClick={onBack}>
          {t('settings.servers.cancel')}
        </Button>
        <Button variant="primary" onClick={handleSave} disabled={!canSave}>
          {editing ? t('settings.servers.save') : t('settings.servers.stack.addButton')}
        </Button>
      </div>
    </SettingsPanelShell>
  )
}
