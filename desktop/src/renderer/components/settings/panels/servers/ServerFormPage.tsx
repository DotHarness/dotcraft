import { useEffect, useState, type JSX } from 'react'
import { KeyRound, Loader2, RefreshCw, Server } from 'lucide-react'

import { SettingsPanelShell } from '../../SettingsPanelShell'
import { SettingsBreadcrumb } from '../../SettingsBreadcrumb'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { Button } from '../../../ui/Button'
import { Input } from '../../../ui/Input'
import { useT } from '../../../../contexts/LocaleContext'
import { useRemoteServersStore } from '../../../../stores/remoteServersStore'
import type { LocalSshHostAlias, LocalSshIdentity, RemoteHost } from '../../../../../shared/remoteServers'
import { StatusText, type TFunction } from './serversStatus'
import * as s from './serversStyles'

interface ServerFormProps {
  host?: RemoteHost
  onBack: () => void
  onSaved: (host: RemoteHost) => void
}

function aliasSummary(alias: LocalSshHostAlias, t: TFunction): string {
  const userAt = alias.user ? `${alias.user}@` : ''
  const target = alias.hostName ? `${userAt}${alias.hostName}` : ''
  const port = alias.port ? `:${alias.port}` : ''
  return target ? `${target}${port}` : t('settings.servers.alias.fallback')
}

function identitySummary(identity: LocalSshIdentity, t: TFunction): string {
  const aliases = identity.hostAliases?.filter(Boolean) ?? []
  if (aliases.length > 0) {
    return t('settings.servers.identity.usedBy', {
      aliases: aliases.slice(0, 2).join(', '),
      suffix: aliases.length > 2 ? '…' : ''
    })
  }
  return identity.source === 'config'
    ? t('settings.servers.identity.fromConfig')
    : t('settings.servers.identity.existingKey')
}

export function ServerFormPage({ host, onBack, onSaved }: ServerFormProps): JSX.Element {
  const t = useT()
  const store = useRemoteServersStore()
  const [name, setName] = useState(host?.name ?? '')
  const [sshTarget, setSshTarget] = useState(host?.sshTarget ?? '')
  const [identityFile, setIdentityFile] = useState(host?.identityFile ?? '')
  const [testResult, setTestResult] = useState<{ ok: boolean; message: string } | null>(null)
  const testing = store.testing[host?.id ?? 'draft']
  const sshConfig = store.sshConfig
  const sshConfigLoading = store.sshConfigLoading

  const editing = Boolean(host)
  const existingIdentities = (sshConfig?.identities ?? []).filter((identity) => identity.exists).slice(0, 8)
  const aliases = (sshConfig?.aliases ?? []).slice(0, 8)

  useEffect(() => {
    if (!store.sshConfig && !store.sshConfigLoading) void store.loadSshConfig()
  }, [store.sshConfig, store.sshConfigLoading, store.loadSshConfig])

  const handleTest = async (): Promise<void> => {
    setTestResult(null)
    const result = await store.testHost({
      draft: { name, sshTarget, identityFile: identityFile.trim() || undefined }
    })
    if (!result) return
    setTestResult(
      result.reachable
        ? {
            ok: true,
            message: t('settings.servers.test.success', {
              state: result.dockerOk && result.composeOk
                ? t('settings.servers.test.ready')
                : t('settings.servers.test.online'),
              latency: result.latencyMs != null
                ? t('settings.servers.test.latency', { latency: result.latencyMs })
                : ''
            })
          }
        : { ok: false, message: result.message ?? t('settings.servers.test.failed') }
    )
  }

  const handleSave = async (): Promise<void> => {
    let saved: RemoteHost | null
    const identity = identityFile.trim() || undefined
    if (editing && host) {
      saved = await store.updateHost(host.id, { name, sshTarget, identityFile: identity })
    } else {
      saved = await store.createHost({ name, sshTarget, identityFile: identity })
    }
    if (saved) onSaved(saved)
  }

  const canSave = name.trim().length > 0 && sshTarget.trim().length > 0
  const authHint = sshConfig
    ? sshConfig.configExists
      ? t('settings.servers.auth.hintWithConfig', { sshDir: sshConfig.sshDir })
      : t('settings.servers.auth.hintWithoutConfig', { sshDir: sshConfig.sshDir })
    : t('settings.servers.auth.hintGeneric')

  return (
    <SettingsPanelShell
      title={editing ? t('settings.servers.form.editTitle') : t('settings.servers.form.addTitle')}
      description={t('settings.servers.form.description')}
      breadcrumb={
        <SettingsBreadcrumb
          parentLabel={host?.name ?? t('settings.servers.title')}
          currentLabel={editing ? t('settings.servers.form.editTitle') : t('settings.servers.form.addTitle')}
          onBack={onBack}
        />
      }
    >
      <SettingsGroup title={t('settings.servers.form.identity')} flush>
        <div style={s.formGrid}>
          <div>
            <label style={s.fieldLabel}>{t('settings.servers.form.name')}</label>
            <Input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder={t('settings.servers.form.namePlaceholder')}
            />
          </div>
          <div>
            <label style={s.fieldLabel}>{t('settings.servers.form.sshTarget')}</label>
            <Input
              mono
              value={sshTarget}
              onChange={(e) => setSshTarget(e.target.value)}
              placeholder={t('settings.servers.form.sshTargetPlaceholder')}
            />
            <div style={s.fieldHint}>{t('settings.servers.form.sshTargetHint')}</div>
          </div>
        </div>
      </SettingsGroup>

      <SettingsGroup title={t('settings.servers.aliases.title')} flush>
        {aliases.length > 0 ? (
          <div style={s.choiceGrid}>
            {aliases.map((alias) => (
              <button
                key={alias.alias}
                type="button"
                style={s.choiceButton}
                onClick={() => {
                  setSshTarget(alias.alias)
                  if (!name.trim()) setName(alias.alias)
                  setIdentityFile('')
                }}
              >
                <span style={s.choiceIcon}>
                  <Server size={15} />
                </span>
                <span style={{ minWidth: 0 }}>
                  <span style={s.choiceTitle}>{alias.alias}</span>
                  <span style={s.choiceSubtitle}>{aliasSummary(alias, t)}</span>
                </span>
              </button>
            ))}
          </div>
        ) : (
          <div style={s.mutedText}>
            {sshConfigLoading
              ? t('settings.servers.aliases.checking')
              : t('settings.servers.aliases.empty')}
          </div>
        )}
      </SettingsGroup>

      <SettingsGroup title={t('settings.servers.auth.title')} flush>
        <div style={s.formGrid}>
          <div>
            <label style={s.fieldLabel}>
              {t('settings.servers.auth.identityOverride')}{' '}
              <span style={{ color: 'var(--text-dimmed)', fontWeight: 400 }}>
                ({t('settings.servers.optional')})
              </span>
            </label>
            <Input
              mono
              value={identityFile}
              onChange={(e) => setIdentityFile(e.target.value)}
              placeholder={t('settings.servers.auth.placeholder')}
            />
            <div style={s.fieldHint}>{authHint}</div>
          </div>
          {identityFile.trim() && (
            <Button style={{ alignSelf: 'flex-start' }} onClick={() => setIdentityFile('')}>
              {t('settings.servers.auth.useSshConfig')}
            </Button>
          )}
        </div>

        {existingIdentities.length > 0 && (
          <div style={{ marginTop: 14 }}>
            <div style={s.fieldLabel}>{t('settings.servers.auth.existingKeys')}</div>
            <div style={s.choiceGrid}>
              {existingIdentities.map((identity) => (
                <button
                  key={identity.path}
                  type="button"
                  style={s.choiceButton}
                  onClick={() => setIdentityFile(identity.path)}
                >
                  <span style={s.choiceIcon}>
                    <KeyRound size={15} />
                  </span>
                  <span style={{ minWidth: 0 }}>
                    <span style={s.choiceTitle}>{identity.path}</span>
                    <span style={s.choiceSubtitle}>{identitySummary(identity, t)}</span>
                  </span>
                </button>
              ))}
            </div>
          </div>
        )}

        {sshConfig?.error && <div style={{ ...s.fieldHint, color: 'var(--warning)' }}>{sshConfig.error}</div>}
      </SettingsGroup>

      {testResult && (
        <SettingsGroup flush>
          <SettingsRow>
            <StatusText tone={testResult.ok ? 'success' : 'error'}>{testResult.message}</StatusText>
          </SettingsRow>
        </SettingsGroup>
      )}

      <div style={s.formActions}>
        <Button
          onClick={handleTest}
          disabled={!sshTarget.trim() || testing}
          iconLeft={testing ? <Loader2 size={15} className="animate-spin-custom" /> : <RefreshCw size={15} />}
        >
          {t('settings.servers.test.button')}
        </Button>
        <span style={{ flex: 1 }} />
        <Button onClick={onBack}>
          {t('settings.servers.cancel')}
        </Button>
        <Button variant="primary" onClick={handleSave} disabled={!canSave}>
          {editing ? t('settings.servers.save') : t('settings.servers.addServer')}
        </Button>
      </div>
    </SettingsPanelShell>
  )
}
