import { useEffect, useState, type JSX } from 'react'
import {
  Download,
  ExternalLink,
  Loader2,
  MoreHorizontal,
  Pencil,
  Play,
  RotateCw,
  Square,
  Terminal,
  Trash2
} from 'lucide-react'

import { Button } from '../../../ui/Button'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import { useT } from '../../../../contexts/LocaleContext'
import { useRemoteServersStore } from '../../../../stores/remoteServersStore'
import { addToast } from '../../../../stores/toastStore'
import type { RemoteHost, RemoteStack } from '../../../../../shared/remoteServers'
import { StatusText, healthLabel, healthTone } from './serversStatus'
import * as s from './serversStyles'

type StackOperationKind = 'connect' | 'start' | 'stop' | 'restart' | 'update'

function operationLabelKey(kind: StackOperationKind): string {
  switch (kind) {
    case 'connect':
      return 'settings.servers.stack.operation.connecting'
    case 'start':
      return 'settings.servers.stack.operation.starting'
    case 'stop':
      return 'settings.servers.stack.operation.stopping'
    case 'restart':
      return 'settings.servers.stack.operation.restarting'
    case 'update':
      return 'settings.servers.stack.operation.updating'
  }
}

function operationMetaKey(kind: StackOperationKind): string {
  switch (kind) {
    case 'connect':
      return 'settings.servers.stack.meta.connecting'
    case 'start':
      return 'settings.servers.stack.meta.starting'
    case 'stop':
      return 'settings.servers.stack.meta.stopping'
    case 'restart':
      return 'settings.servers.stack.meta.restarting'
    case 'update':
      return 'settings.servers.stack.meta.updating'
  }
}

function formatAppVersion(version: string | undefined): string {
  const core = version?.trim().split('+')[0]?.trim()
  return core || ''
}

export function StackCard({
  host,
  stack,
  onEdit
}: {
  host: RemoteHost
  stack: RemoteStack
  onEdit: () => void
}): JSX.Element {
  const t = useT()
  const store = useRemoteServersStore()
  const confirm = useConfirmDialog()
  const status = store.statuses[stack.id]
  const operationKind = store.stackOperations[stack.id]?.kind
  const operationBusy = operationKind != null
  const connectBusy = operationKind === 'connect'
  const active = store.activeStack?.hostId === host.id && store.activeStack?.stackId === stack.id

  const [menuOpen, setMenuOpen] = useState(false)
  const [logsOpen, setLogsOpen] = useState(false)
  const [logsText, setLogsText] = useState('')
  const [logsLoading, setLogsLoading] = useState(false)

  useEffect(() => {
    void store.refreshStatus(host.id, stack.id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [host.id, stack.id])

  useEffect(() => {
    if (operationBusy) setMenuOpen(false)
  }, [operationBusy])

  const tone: s.StatusIndicatorTone = operationBusy
    ? 'pending'
    : active
      ? 'success'
      : healthTone(status?.health)
  const statusLabel = operationKind
    ? t(operationLabelKey(operationKind))
    : active
      ? t('settings.servers.stack.connected')
      : healthLabel(status, t)
  const running = status?.health === 'running' || status?.health === 'partial'
  const appVersion = formatAppVersion(status?.appVersion)
  const stackMeta = operationKind
    ? t(operationMetaKey(operationKind))
    : active
      ? t('settings.servers.stack.meta.active')
      : status?.tokenPresent === false
        ? t('settings.servers.stack.meta.tokenMissing')
        : status?.health === 'running'
          ? t('settings.servers.stack.meta.ready')
          : status?.error
            ? status.error
            : t('settings.servers.stack.meta.dashboardReady')

  const toggleLogs = async (): Promise<void> => {
    const next = !logsOpen
    setLogsOpen(next)
    if (next && !logsText) {
      setLogsLoading(true)
      const result = await window.api.remoteServers.logs(host.id, stack.id, { tail: 200 })
      setLogsText(result?.text || t('settings.servers.logs.empty'))
      setLogsLoading(false)
    }
  }

  const confirmAndRun = async (
    action: 'update' | 'restart' | 'stop' | 'start',
    opts?: { title: string; message: string; danger?: boolean; confirmLabel?: string }
  ): Promise<void> => {
    if (operationBusy) return
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
    const result = await store.runAction(host.id, stack.id, action)
    if (!result.ok) {
      addToast(result.message || t('settings.servers.stack.actionFailed'), 'error')
    }
  }

  return (
    <div style={s.stackCard}>
      <div style={s.stackHead}>
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
          <span style={{ fontSize: 13.5, fontWeight: 600, lineHeight: 1 }}>{stack.name}</span>
          <StatusText tone={tone}>{statusLabel}</StatusText>
        </span>
        <span style={{ flex: 1 }} />
        {appVersion && (
          <span style={{ color: 'var(--text-secondary)', fontSize: 12 }}>{appVersion}</span>
        )}
        <div style={{ position: 'relative' }}>
          <Button
            variant="ghost"
            size="icon"
            aria-label={t('settings.servers.stack.more')}
            disabled={operationBusy}
            onClick={() => setMenuOpen((v) => !v)}
          >
            <MoreHorizontal size={16} />
          </Button>
          {menuOpen && (
            <>
              <div style={{ position: 'fixed', inset: 0, zIndex: 19 }} onClick={() => setMenuOpen(false)} />
              <div style={s.overflowMenu}>
                <button
                  style={s.overflowItem}
                  onClick={() =>
                    confirmAndRun('update', {
                      title: t('settings.servers.confirm.updateTitle', { name: stack.name }),
                      message: t('settings.servers.confirm.updateMessage'),
                      confirmLabel: t('settings.servers.stack.update')
                    })
                  }
                >
                  <Download size={14} />
                  {t('settings.servers.stack.update')}
                </button>
                <button style={s.overflowItem} onClick={() => confirmAndRun('restart')}>
                  <RotateCw size={14} />
                  {t('settings.servers.stack.restart')}
                </button>
                {running ? (
                  <button
                    style={s.overflowItem}
                    onClick={() =>
                      confirmAndRun('stop', {
                        title: t('settings.servers.confirm.stopTitle', { name: stack.name }),
                        message: t('settings.servers.confirm.stopMessage'),
                        danger: true,
                        confirmLabel: t('settings.servers.stack.stop')
                      })
                    }
                  >
                    <Square size={14} />
                    {t('settings.servers.stack.stop')}
                  </button>
                ) : (
                  <button style={s.overflowItem} onClick={() => confirmAndRun('start')}>
                    <Play size={14} />
                    {t('settings.servers.stack.start')}
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
                  <Pencil size={14} />
                  {t('settings.servers.stack.edit')}
                </button>
                <button
                  style={{ ...s.overflowItem, color: 'var(--error)' }}
                  onClick={async () => {
                    setMenuOpen(false)
                    const ok = await confirm({
                      title: t('settings.servers.confirm.removeStackTitle', { name: stack.name }),
                      message: t('settings.servers.confirm.removeStackMessage'),
                      danger: true,
                      confirmLabel: t('settings.servers.stack.remove')
                    })
                    if (!ok) return
                    await store.updateHost(host.id, { stacks: host.stacks.filter((st) => st.id !== stack.id) })
                  }}
                >
                  <Trash2 size={14} />
                  {t('settings.servers.stack.remove')}
                </button>
              </div>
            </>
          )}
        </div>
      </div>

      <div style={s.stackMeta} aria-live="polite">
        {stackMeta}
      </div>

      <div style={s.stackActions}>
        {active ? (
          <Button
            variant="danger"
            disabled={operationBusy}
            onClick={() => store.disconnect(host.id, stack.id)}
          >
            {t('settings.servers.stack.disconnect')}
          </Button>
        ) : (
          <Button
            variant="primary"
            disabled={operationBusy}
            onClick={() => store.openInDesktop(host.id, stack.id)}
            iconLeft={connectBusy ? <Loader2 size={14} className="animate-spin-custom" /> : undefined}
          >
            {t('settings.servers.stack.openInDesktop')}
          </Button>
        )}
        <Button
          disabled={operationBusy}
          onClick={() => store.openDashboard(host.id, stack.id)}
          iconLeft={<ExternalLink size={14} />}
        >
          {t('settings.servers.stack.dashboard')}
        </Button>
        <Button onClick={toggleLogs} iconLeft={<Terminal size={14} />}>
          {t('settings.servers.stack.logs')}
        </Button>
      </div>

      {logsOpen && (
        <div style={s.logsBox}>
          <div style={s.logsBar}>
            <span>{t('settings.servers.logs.title')}</span>
            <span style={{ marginLeft: 'auto' }}>{t('settings.servers.logs.tail')}</span>
          </div>
          <div style={s.logsBody}>{logsLoading ? t('settings.servers.logs.loading') : logsText}</div>
        </div>
      )}
    </div>
  )
}
