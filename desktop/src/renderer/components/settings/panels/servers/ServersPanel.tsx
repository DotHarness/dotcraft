import { useEffect, useState, type JSX, type ReactNode, type CSSProperties } from 'react'
import {
  Server,
  Plus,
  Pencil,
  ChevronRight,
  ArrowLeft,
  RefreshCw,
  MoreHorizontal,
  ExternalLink,
  Terminal,
  X,
  AlertTriangle,
  Trash2,
  Loader2,
  KeyRound
} from 'lucide-react'
import { SettingsPanelShell } from '../../SettingsPanelShell'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import { useRemoteServersStore } from '../../../../stores/remoteServersStore'
import type {
  RemoteHost,
  RemoteStack,
  RemoteStackStatus,
  StackHealth,
  LocalSshHostAlias,
  LocalSshIdentity
} from '../../../../../shared/remoteServers'
import * as s from './serversStyles'

function healthTone(health: StackHealth | undefined): s.StatusTone {
  switch (health) {
    case 'running':
      return 'success'
    case 'partial':
      return 'warning'
    case 'unhealthy':
      return 'error'
    default:
      return 'neutral'
  }
}

function healthLabel(status: RemoteStackStatus | undefined): string {
  if (!status) return 'Not checked'
  switch (status.health) {
    case 'running':
      return 'Running'
    case 'partial':
      return `Partial · ${status.servicesUp} of ${status.servicesTotal} services`
    case 'unhealthy':
      return 'Unhealthy'
    case 'stopped':
      return 'Stopped'
    default:
      return status.error ? 'Unavailable' : 'Unknown'
  }
}

function formatVersion(tag: string | undefined): string {
  if (!tag) return ''
  return /^\d/.test(tag) ? `v${tag}` : tag
}

function reachabilityView(host: RemoteHost): { tone: s.StatusTone; label: string } {
  const result = useRemoteServersStore.getState().testResults[host.id]
  if (useRemoteServersStore.getState().testing[host.id]) return { tone: 'info', label: 'Checking…' }
  if (!result) return { tone: 'neutral', label: 'Not tested' }
  if (result.reachable) return { tone: 'success', label: 'Reachable' }
  return { tone: 'error', label: 'Unreachable' }
}

// ── Status dot + text ────────────────────────────────────────────────────────

function StatusDot({ tone }: { tone: s.StatusTone }): JSX.Element {
  return <span style={s.dotStyle(tone)} />
}

function StatusText({ tone, children }: { tone: s.StatusTone; children: ReactNode }): JSX.Element {
  return (
    <span style={s.statusTextStyle(tone)}>
      <StatusDot tone={tone} />
      {children}
    </span>
  )
}

// ── Modal shell ──────────────────────────────────────────────────────────────

function Modal({
  title,
  onClose,
  children,
  footer,
  width
}: {
  title: string
  onClose: () => void
  children: ReactNode
  footer: ReactNode
  width?: number
}): JSX.Element {
  return (
    <div
      style={s.modalScrim}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose()
      }}
    >
      <div style={{ ...s.modal, width: width ?? s.modal.width }} onMouseDown={(e) => e.stopPropagation()}>
        <div style={s.modalHead}>
          <span style={{ fontSize: 15, fontWeight: 650 }}>{title}</span>
          <button aria-label="Close" onClick={onClose} style={{ ...s.iconBtnGhost, width: 28, height: 28 }}>
            <X size={16} />
          </button>
        </div>
        <div style={s.modalBody}>{children}</div>
        <div style={s.modalFoot}>{footer}</div>
      </div>
    </div>
  )
}

// ── Add / edit server page ───────────────────────────────────────────────────

interface ServerFormProps {
  host?: RemoteHost
  onBack: () => void
  onSaved: (host: RemoteHost) => void
}

function aliasSummary(alias: LocalSshHostAlias): string {
  const userAt = alias.user ? `${alias.user}@` : ''
  const target = alias.hostName ? `${userAt}${alias.hostName}` : ''
  const port = alias.port ? `:${alias.port}` : ''
  return target ? `${target}${port}` : 'SSH config alias'
}

function identitySummary(identity: LocalSshIdentity): string {
  const aliases = identity.hostAliases?.filter(Boolean) ?? []
  if (aliases.length > 0) return `Used by ${aliases.slice(0, 2).join(', ')}${aliases.length > 2 ? '…' : ''}`
  return identity.source === 'config' ? 'From SSH config' : 'Existing key'
}

function ServerFormPage({ host, onBack, onSaved }: ServerFormProps): JSX.Element {
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
            message: `Connected · server ${result.dockerOk && result.composeOk ? 'ready' : 'reachable'}${result.latencyMs != null ? ` · ${result.latencyMs}ms` : ''}`
          }
        : { ok: false, message: result.message ?? 'Could not reach this server.' }
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
    ? `Leave blank to use ${sshConfig.configExists ? '~/.ssh/config, ' : ''}ssh-agent, and keys under ${sshConfig.sshDir}.`
    : 'Leave blank to use your system SSH config, ssh-agent, and default keys.'

  return (
    <SettingsPanelShell
      title={editing ? 'Edit server' : 'Add server'}
      description="Connect through the system SSH client. Saved SSH aliases, ProxyJump, ssh-agent, and existing keys are reused."
      action={
        <button type="button" onClick={onBack} style={s.btn}>
          <ArrowLeft size={15} /> Back
        </button>
      }
    >
      <SettingsGroup title="Identity" flush>
        <div style={s.formGrid}>
          <div>
            <label style={s.fieldLabel}>Name</label>
            <input style={s.input} value={name} onChange={(e) => setName(e.target.value)} placeholder="DotCraftCloud" />
          </div>
          <div>
            <label style={s.fieldLabel}>SSH target</label>
            <input
              style={{ ...s.input, fontFamily: 'var(--font-mono)' }}
              value={sshTarget}
              onChange={(e) => setSshTarget(e.target.value)}
              placeholder="user@host or saved SSH alias"
            />
            <div style={s.fieldHint}>Use a target from your SSH config, such as a Host alias, or enter user@host.</div>
          </div>
        </div>
      </SettingsGroup>

      <SettingsGroup
        title="Saved SSH aliases"
        description={sshConfigLoading ? 'Checking your local SSH config…' : 'Aliases from ~/.ssh/config can be used directly as the SSH target.'}
        flush
      >
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
                  <span style={s.choiceSubtitle}>{aliasSummary(alias)}</span>
                </span>
              </button>
            ))}
          </div>
        ) : (
          <div style={s.mutedText}>
            {sshConfigLoading
              ? 'Checking for ~/.ssh/config…'
              : 'No concrete Host aliases found. You can still enter user@host manually.'}
          </div>
        )}
      </SettingsGroup>

      <SettingsGroup
        title="Authentication"
        description="Default mode uses your normal SSH setup. Set an identity file only when you need to override that."
        flush
      >
        <div style={s.formGrid}>
          <div>
            <label style={s.fieldLabel}>
              Identity file override <span style={{ color: 'var(--text-dimmed)', fontWeight: 400 }}>(optional)</span>
            </label>
            <input
              style={{ ...s.input, fontFamily: 'var(--font-mono)' }}
              value={identityFile}
              onChange={(e) => setIdentityFile(e.target.value)}
              placeholder="Use SSH config / agent / default keys"
            />
            <div style={s.fieldHint}>{authHint}</div>
          </div>
          {identityFile.trim() && (
            <button type="button" style={{ ...s.btn, alignSelf: 'flex-start' }} onClick={() => setIdentityFile('')}>
              Use SSH config
            </button>
          )}
        </div>

        {existingIdentities.length > 0 && (
          <div style={{ marginTop: 14 }}>
            <div style={s.fieldLabel}>Existing local keys</div>
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
                    <span style={s.choiceSubtitle}>{identitySummary(identity)}</span>
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
        <button style={s.btn} onClick={handleTest} disabled={!sshTarget.trim() || testing}>
          {testing ? <Loader2 size={15} className="animate-spin-custom" /> : <RefreshCw size={15} />}
          Test SSH
        </button>
        <span style={{ flex: 1 }} />
        <button style={s.btn} onClick={onBack}>
          Cancel
        </button>
        <button style={{ ...s.btnPrimary, opacity: canSave ? 1 : 0.5 }} onClick={handleSave} disabled={!canSave}>
          {editing ? 'Save' : 'Add server'}
        </button>
      </div>
    </SettingsPanelShell>
  )
}

// ── Add / edit stack modal ───────────────────────────────────────────────────

function StackFormModal({
  host,
  stack,
  onClose
}: {
  host: RemoteHost
  stack?: RemoteStack
  onClose: () => void
}): JSX.Element {
  const store = useRemoteServersStore()
  const [name, setName] = useState(stack?.name ?? '')
  const [composeDir, setComposeDir] = useState(stack?.composeDir ?? '')
  const [workspaceDir, setWorkspaceDir] = useState(stack?.workspaceDir ?? '')
  const [projectName, setProjectName] = useState(stack?.projectName ?? '')
  const [appServerPort, setAppServerPort] = useState(String(stack?.appServerPort ?? 9100))
  const [dashboardPort, setDashboardPort] = useState(String(stack?.dashboardPort ?? 8080))
  const [sandbox, setSandbox] = useState(stack?.sandboxProfile ?? false)

  const editing = Boolean(stack)
  const canSave = name.trim().length > 0 && composeDir.trim().length > 0

  const handleSave = async (): Promise<void> => {
    const next: RemoteStack = {
      id: stack?.id ?? '',
      name: name.trim(),
      composeDir: composeDir.trim(),
      workspaceDir: workspaceDir.trim() || undefined,
      projectName: projectName.trim() || undefined,
      appServerPort: Number(appServerPort) || 9100,
      dashboardPort: Number(dashboardPort) || 8080,
      sandboxProfile: sandbox
    }
    const stacks = editing
      ? host.stacks.map((st) => (st.id === stack!.id ? next : st))
      : [...host.stacks, next]
    await store.updateHost(host.id, { stacks })
    onClose()
  }

  const portInput = (value: string, set: (v: string) => void): JSX.Element => (
    <input style={{ ...s.input, fontFamily: 'var(--font-mono)' }} value={value} onChange={(e) => set(e.target.value)} />
  )

  return (
    <Modal
      title={editing ? 'Edit stack' : 'Add stack'}
      onClose={onClose}
      width={480}
      footer={
        <>
          <button style={s.btn} onClick={onClose}>
            Cancel
          </button>
          <button style={{ ...s.btnPrimary, opacity: canSave ? 1 : 0.5 }} onClick={handleSave} disabled={!canSave}>
            {editing ? 'Save' : 'Add stack'}
          </button>
        </>
      }
    >
      <div style={{ marginBottom: 14 }}>
        <label style={s.fieldLabel}>Name</label>
        <input style={s.input} value={name} onChange={(e) => setName(e.target.value)} placeholder="prod" />
      </div>
      <div style={{ marginBottom: 14 }}>
        <label style={s.fieldLabel}>Deployment folder</label>
        <input
          style={{ ...s.input, fontFamily: 'var(--font-mono)' }}
          value={composeDir}
          onChange={(e) => setComposeDir(e.target.value)}
          placeholder="~/dotcraft/deploy/docker"
        />
        <div style={s.fieldHint}>The folder on the server where this DotCraft stack is deployed.</div>
      </div>
      <div style={{ marginBottom: 14 }}>
        <label style={s.fieldLabel}>
          Data folder <span style={{ color: 'var(--text-dimmed)', fontWeight: 400 }}>(optional)</span>
        </label>
        <input
          style={{ ...s.input, fontFamily: 'var(--font-mono)' }}
          value={workspaceDir}
          onChange={(e) => setWorkspaceDir(e.target.value)}
          placeholder="Defaults to the stack's data folder"
        />
      </div>
      <div style={{ marginBottom: 14 }}>
        <label style={s.fieldLabel}>
          Project name <span style={{ color: 'var(--text-dimmed)', fontWeight: 400 }}>(optional)</span>
        </label>
        <input
          style={{ ...s.input, fontFamily: 'var(--font-mono)' }}
          value={projectName}
          onChange={(e) => setProjectName(e.target.value)}
        />
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginBottom: 14 }}>
        <div>
          <label style={s.fieldLabel}>App server port</label>
          {portInput(appServerPort, setAppServerPort)}
        </div>
        <div>
          <label style={s.fieldLabel}>Dashboard port</label>
          {portInput(dashboardPort, setDashboardPort)}
        </div>
      </div>
      <div style={{ ...s.switchRow, marginBottom: 14 }}>
        <div>
          <div style={{ fontSize: 12.5, fontWeight: 600 }}>Sandbox</div>
          <div style={{ ...s.fieldHint, marginTop: 3 }}>Run the optional sandbox service alongside this stack</div>
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
      <div style={s.callout}>
        DotCraft reads this stack&apos;s sign-in token automatically when you connect. You never enter or store it here.
      </div>
    </Modal>
  )
}

// ── Stack card ───────────────────────────────────────────────────────────────

function StackCard({
  host,
  stack,
  onEdit
}: {
  host: RemoteHost
  stack: RemoteStack
  onEdit: () => void
}): JSX.Element {
  const store = useRemoteServersStore()
  const confirm = useConfirmDialog()
  const status = store.statuses[stack.id]
  const busy = store.busyStacks[stack.id]
  const active = store.activeStack?.hostId === host.id && store.activeStack?.stackId === stack.id

  const [menuOpen, setMenuOpen] = useState(false)
  const [logsOpen, setLogsOpen] = useState(false)
  const [logsText, setLogsText] = useState('')
  const [logsLoading, setLogsLoading] = useState(false)

  useEffect(() => {
    void store.refreshStatus(host.id, stack.id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [host.id, stack.id])

  const tone = active ? 'success' : healthTone(status?.health)
  const running = status?.health === 'running' || status?.health === 'partial'

  const toggleLogs = async (): Promise<void> => {
    const next = !logsOpen
    setLogsOpen(next)
    if (next && !logsText) {
      setLogsLoading(true)
      const result = await window.api.remoteServers.logs(host.id, stack.id, { tail: 200 })
      setLogsText(result?.text || 'No log output.')
      setLogsLoading(false)
    }
  }

  const confirmAndRun = async (
    action: 'update' | 'restart' | 'stop' | 'start',
    opts?: { title: string; message: string; danger?: boolean; confirmLabel?: string }
  ): Promise<void> => {
    setMenuOpen(false)
    if (opts) {
      const ok = await confirm({
        title: opts.title,
        message: opts.message,
        danger: opts.danger,
        confirmLabel: opts.confirmLabel
      })
      if (!ok) return
    }
    await store.runAction(host.id, stack.id, action)
  }

  return (
    <div style={s.stackCard}>
      <div style={s.stackHead}>
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
          <StatusDot tone={tone} />
          <span style={{ fontSize: 13.5, fontWeight: 600, lineHeight: 1 }}>{stack.name}</span>
          {active ? (
            <StatusText tone="success">Connected</StatusText>
          ) : (
            <span style={s.statusTextStyle(healthTone(status?.health))}>{healthLabel(status)}</span>
          )}
        </span>
        <span style={{ flex: 1 }} />
        {status?.imageTag && (
          <span style={{ color: 'var(--text-secondary)', fontSize: 12 }}>{formatVersion(status.imageTag)}</span>
        )}
        <div style={{ position: 'relative' }}>
          <button aria-label="More" style={s.iconBtnGhost} onClick={() => setMenuOpen((v) => !v)}>
            <MoreHorizontal size={16} />
          </button>
          {menuOpen && (
            <>
              <div style={{ position: 'fixed', inset: 0, zIndex: 19 }} onClick={() => setMenuOpen(false)} />
              <div style={s.overflowMenu}>
                <button
                  style={s.overflowItem}
                  onClick={() =>
                    confirmAndRun('update', {
                      title: `Update ${stack.name}?`,
                      message:
                        'This will back up the current settings, download the latest DotCraft version, and restart the stack on the new version.\n\nYour data and conversations are kept. The stack restarts for a moment while updating.',
                      confirmLabel: 'Update'
                    })
                  }
                >
                  Update
                </button>
                <button style={s.overflowItem} onClick={() => confirmAndRun('restart')}>
                  Restart
                </button>
                {running ? (
                  <button
                    style={s.overflowItem}
                    onClick={() =>
                      confirmAndRun('stop', {
                        title: `Stop ${stack.name}?`,
                        message: 'This stops the stack on the server. You can start it again at any time.',
                        danger: true,
                        confirmLabel: 'Stop'
                      })
                    }
                  >
                    Stop
                  </button>
                ) : (
                  <button style={s.overflowItem} onClick={() => confirmAndRun('start')}>
                    Start
                  </button>
                )}
                <div style={{ height: 1, background: 'var(--border-default)', margin: '4px 6px' }} />
                <button
                  style={s.overflowItem}
                  onClick={() => {
                    setMenuOpen(false)
                    onEdit()
                  }}
                >
                  Edit stack
                </button>
                <button
                  style={{ ...s.overflowItem, color: 'var(--error)' }}
                  onClick={async () => {
                    setMenuOpen(false)
                    const ok = await confirm({
                      title: `Remove ${stack.name}?`,
                      message: 'This removes the stack from DotCraft. It does not change anything on the server.',
                      danger: true,
                      confirmLabel: 'Remove'
                    })
                    if (!ok) return
                    await store.updateHost(host.id, { stacks: host.stacks.filter((st) => st.id !== stack.id) })
                  }}
                >
                  Remove
                </button>
              </div>
            </>
          )}
        </div>
      </div>

      <div style={s.stackMeta}>
        {active
          ? 'Linked to this Desktop · Dashboard ready'
          : status?.tokenPresent === false
            ? 'Sign-in token missing on the server'
            : status?.health === 'running'
              ? 'Dashboard ready · ready to connect'
              : status?.error
                ? status.error
                : 'Dashboard ready'}
      </div>

      <div style={s.stackActions}>
        {active ? (
          <button
            style={{ ...s.btnDanger, ...s.btnSm }}
            onClick={() => store.disconnect(host.id, stack.id)}
          >
            Disconnect
          </button>
        ) : (
          <button
            style={{ ...s.btnPrimary, ...s.btnSm, opacity: busy ? 0.6 : 1 }}
            disabled={busy}
            onClick={() => store.openInDesktop(host.id, stack.id)}
          >
            {busy ? <Loader2 size={14} className="animate-spin-custom" /> : null}
            Open in Desktop
          </button>
        )}
        <button style={{ ...s.btn, ...s.btnSm }} onClick={() => store.openDashboard(host.id, stack.id)}>
          <ExternalLink size={14} /> Dashboard
        </button>
        <button style={{ ...s.btn, ...s.btnSm }} onClick={toggleLogs}>
          <Terminal size={14} /> Logs
        </button>
      </div>

      {logsOpen && (
        <div style={s.logsBox}>
          <div style={s.logsBar}>
            <span>Logs</span>
            <span style={{ marginLeft: 'auto' }}>Last 200 lines</span>
          </div>
          <div style={s.logsBody}>{logsLoading ? 'Loading…' : logsText}</div>
        </div>
      )}
    </div>
  )
}

// ── Server detail ────────────────────────────────────────────────────────────

function ServerDetail({
  host,
  onBack,
  onEditServer,
  onAddStack,
  onEditStack
}: {
  host: RemoteHost
  onBack: () => void
  onEditServer: () => void
  onAddStack: () => void
  onEditStack: (stack: RemoteStack) => void
}): JSX.Element {
  const store = useRemoteServersStore()
  const confirm = useConfirmDialog()
  const reach = reachabilityView(host)
  const testing = store.testing[host.id]
  const result = store.testResults[host.id]
  const unreachable = result != null && !result.reachable

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div style={{ display: 'flex', alignItems: 'flex-start', gap: 12 }}>
        <button aria-label="Back" style={{ ...s.iconBtn, marginTop: 2 }} onClick={onBack}>
          <ArrowLeft size={16} />
        </button>
        <div>
          <div style={{ fontSize: 19, fontWeight: 650 }}>{host.name}</div>
          <div style={{ marginTop: 4, color: 'var(--text-secondary)', fontSize: 12.5, fontFamily: 'var(--font-mono)' }}>
            {host.sshTarget}
          </div>
          <div style={{ marginTop: 3 }}>
            <StatusText tone={reach.tone}>{reach.label}</StatusText>
          </div>
        </div>
        <div style={{ marginLeft: 'auto', display: 'flex', gap: 8 }}>
          <button style={s.btn} disabled={testing} onClick={() => store.testHost({ id: host.id })}>
            {testing ? <Loader2 size={15} className="animate-spin-custom" /> : <RefreshCw size={15} />}
            Test SSH
          </button>
          <button style={s.iconBtn} aria-label="Edit server" onClick={onEditServer}>
            <Pencil size={16} />
          </button>
          <button
            style={s.iconBtn}
            aria-label="Remove server"
            onClick={async () => {
              const ok = await confirm({
                title: `Remove ${host.name}?`,
                message: 'This removes the server from DotCraft. It does not change anything on the server itself.',
                danger: true,
                confirmLabel: 'Remove'
              })
              if (ok) {
                await store.deleteHost(host.id)
                onBack()
              }
            }}
          >
            <Trash2 size={16} />
          </button>
        </div>
      </div>

      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <h3 style={{ margin: 0, fontSize: 13, fontWeight: 600 }}>Stacks</h3>
        <button style={{ ...s.btn, ...s.btnSm }} onClick={onAddStack}>
          <Plus size={14} /> Add stack
        </button>
      </div>

      {unreachable ? (
        <div style={s.banner}>
          <span style={{ color: 'var(--error)', flexShrink: 0, marginTop: 1 }}>
            <AlertTriangle size={20} />
          </span>
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: 13, fontWeight: 600 }}>Can&apos;t reach this server over SSH</div>
            <div style={{ marginTop: 4, color: 'var(--text-secondary)', fontSize: 12, fontFamily: 'var(--font-mono)' }}>
              {result?.message ?? 'SSH connection failed.'}
            </div>
            <div style={{ marginTop: 10 }}>
              <button style={{ ...s.btn, ...s.btnSm }} disabled={testing} onClick={() => store.testHost({ id: host.id })}>
                <RefreshCw size={14} /> Test SSH
              </button>
            </div>
          </div>
        </div>
      ) : host.stacks.length === 0 ? (
        <div style={{ ...s.emptyBox, padding: '32px 24px' }}>
          <div style={{ fontSize: 13.5, fontWeight: 600 }}>No stacks yet</div>
          <div style={{ maxWidth: '42ch', color: 'var(--text-secondary)', fontSize: 12.5 }}>
            Add a DotCraft deployment running on this server to manage and connect to it.
          </div>
          <button style={{ ...s.btnPrimary, marginTop: 12 }} onClick={onAddStack}>
            <Plus size={15} /> Add stack
          </button>
        </div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          {host.stacks.map((stack) => (
            <StackCard key={stack.id} host={host} stack={stack} onEdit={() => onEditStack(stack)} />
          ))}
        </div>
      )}
    </div>
  )
}

// ── Server list ──────────────────────────────────────────────────────────────

function ServerList({
  hosts,
  onOpen,
  onAdd
}: {
  hosts: RemoteHost[]
  onOpen: (id: string) => void
  onAdd: () => void
}): JSX.Element {
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
        <div style={{ fontSize: 14, fontWeight: 600 }}>No servers yet</div>
        <div style={{ maxWidth: '44ch', color: 'var(--text-secondary)', fontSize: 12.5 }}>
          Connect to a server you can reach over SSH and manage the DotCraft stacks running on it. Works with your
          existing SSH keys.
        </div>
        <button style={{ ...s.btnPrimary, marginTop: 14 }} onClick={onAdd}>
          <Plus size={15} /> Add server
        </button>
      </div>
    )
  }

  return (
    <div style={s.card}>
      <div style={s.groupHead}>
        <span style={{ fontSize: 13, fontWeight: 600 }}>Servers</span>
        <button style={{ ...s.btn, ...s.btnSm }} onClick={onAdd}>
          <Plus size={14} /> Add
        </button>
      </div>
      {hosts.map((host, index) => {
        const reach = reachabilityView(host)
        const activeHere = activeStack?.hostId === host.id
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
                {activeHere && <span style={{ fontSize: 11, fontWeight: 600, color: 'var(--accent)' }}>· Active here</span>}
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
                {host.sshTarget} · {host.stacks.length} {host.stacks.length === 1 ? 'stack' : 'stacks'}
              </span>
            </span>
            <span style={{ color: 'var(--text-dimmed)', display: 'inline-flex' }}>
              <ChevronRight size={18} />
            </span>
          </button>
        )
      })}
    </div>
  )
}

// ── Panel root ───────────────────────────────────────────────────────────────

type ModalState =
  | { kind: 'addStack'; host: RemoteHost }
  | { kind: 'editStack'; host: RemoteHost; stack: RemoteStack }
  | null

type ServerFormState =
  | { kind: 'addServer' }
  | { kind: 'editServer'; hostId: string }
  | null

export function ServersPanel(): JSX.Element {
  const store = useRemoteServersStore()
  const [modal, setModal] = useState<ModalState>(null)
  const [serverForm, setServerForm] = useState<ServerFormState>(null)

  useEffect(() => {
    if (!store.loaded) void store.load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const selectedHost = store.hosts.find((h) => h.id === store.selectedHostId) ?? null
  const editingHost =
    serverForm?.kind === 'editServer'
      ? store.hosts.find((h) => h.id === serverForm.hostId) ?? null
      : null

  const closeModal = (): void => setModal(null)

  const detailHeader: CSSProperties = { display: 'flex', flexDirection: 'column', gap: 16 }

  return (
    <>
      {serverForm?.kind === 'addServer' ? (
        <ServerFormPage
          onBack={() => setServerForm(null)}
          onSaved={(host) => {
            store.selectHost(host.id)
            setServerForm(null)
          }}
        />
      ) : serverForm?.kind === 'editServer' && editingHost ? (
        <ServerFormPage
          host={editingHost}
          onBack={() => setServerForm(null)}
          onSaved={(host) => {
            store.selectHost(host.id)
            setServerForm(null)
          }}
        />
      ) : selectedHost ? (
        <div style={detailHeader}>
          <ServerDetail
            host={selectedHost}
            onBack={() => store.selectHost(null)}
            onEditServer={() => setServerForm({ kind: 'editServer', hostId: selectedHost.id })}
            onAddStack={() => setModal({ kind: 'addStack', host: selectedHost })}
            onEditStack={(stack) => setModal({ kind: 'editStack', host: selectedHost, stack })}
          />
        </div>
      ) : (
        <SettingsPanelShell
          title="Servers"
          description="Manage DotCraft running on your remote servers."
          action={
            store.hosts.length > 0 ? (
              <button style={s.btnPrimary} onClick={() => setServerForm({ kind: 'addServer' })}>
                <Plus size={15} /> Add server
              </button>
            ) : undefined
          }
        >
          {store.error && (
            <div style={{ ...s.banner, marginBottom: 4 }}>
              <span style={{ color: 'var(--error)', marginTop: 1 }}>
                <AlertTriangle size={18} />
              </span>
              <div style={{ flex: 1, fontSize: 12.5, color: 'var(--text-secondary)' }}>{store.error}</div>
              <button style={s.iconBtnGhost} aria-label="Dismiss" onClick={() => store.clearError()}>
                <X size={16} />
              </button>
            </div>
          )}
          <ServerList
            hosts={store.hosts}
            onOpen={(id) => store.selectHost(id)}
            onAdd={() => setServerForm({ kind: 'addServer' })}
          />
        </SettingsPanelShell>
      )}

      {modal?.kind === 'addStack' && <StackFormModal host={modal.host} onClose={closeModal} />}
      {modal?.kind === 'editStack' && <StackFormModal host={modal.host} stack={modal.stack} onClose={closeModal} />}
    </>
  )
}
