import { useEffect, useRef, useState, type JSX } from 'react'
import { AlertTriangle, Loader2, Pencil, Plus, RefreshCw, Trash2 } from 'lucide-react'

import { SettingsPageHeader } from '../../SettingsPageHeader'
import { SettingsBreadcrumb } from '../../SettingsBreadcrumb'
import { Button } from '../../../ui/Button'
import { IconButton } from '../../../ui/IconButton'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import { useT } from '../../../../contexts/LocaleContext'
import { useRemoteServersStore } from '../../../../stores/remoteServersStore'
import type { RemoteHost, RemoteStack } from '../../../../../shared/remoteServers'
import { StackCard } from './StackCard'
import { StatusText, reachabilityView } from './serversStatus'
import * as s from './serversStyles'

const REACHABILITY_FLASH_MS = 4000

export function ServerDetail({
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
  const t = useT()
  const store = useRemoteServersStore()
  const confirm = useConfirmDialog()
  const reach = reachabilityView(host.id, t)
  const testing = store.testing[host.id]
  const result = store.testResults[host.id]
  const unreachable = result != null && !result.reachable
  const [manualReachVisible, setManualReachVisible] = useState(false)
  const reachHideTimerRef = useRef<number | null>(null)

  const clearReachHideTimer = (): void => {
    if (reachHideTimerRef.current != null) {
      window.clearTimeout(reachHideTimerRef.current)
      reachHideTimerRef.current = null
    }
  }

  const scheduleReachHide = (): void => {
    clearReachHideTimer()
    reachHideTimerRef.current = window.setTimeout(() => {
      reachHideTimerRef.current = null
      setManualReachVisible(false)
    }, REACHABILITY_FLASH_MS)
  }

  useEffect(() => {
    clearReachHideTimer()
    setManualReachVisible(false)
    return clearReachHideTimer
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [host.id])

  useEffect(() => {
    if (unreachable) clearReachHideTimer()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [unreachable])

  const handleTestSsh = async (): Promise<void> => {
    clearReachHideTimer()
    setManualReachVisible(true)
    const nextResult = await store.testHost({ id: host.id })
    if (nextResult?.reachable) {
      scheduleReachHide()
    } else if (!nextResult) {
      scheduleReachHide()
    }
  }

  const showReachability = unreachable || manualReachVisible

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <SettingsPageHeader
        title={host.name}
        breadcrumb={
          <SettingsBreadcrumb
            parentLabel={t('settings.servers.title')}
            currentLabel={host.name}
            onBack={onBack}
          />
        }
        description={<span style={{ fontFamily: 'var(--font-mono)' }}>{host.sshTarget}</span>}
        action={
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            {showReachability && (
              <span aria-live="polite" style={{ display: 'inline-flex', alignItems: 'center', minHeight: 32 }}>
                <StatusText tone={reach.tone}>{reach.label}</StatusText>
              </span>
            )}
            <Button
              disabled={testing}
              onClick={handleTestSsh}
              iconLeft={testing ? <Loader2 size={15} className="animate-spin-custom" /> : <RefreshCw size={15} />}
            >
              {t('settings.servers.test.button')}
            </Button>
            <IconButton
              icon={<Pencil size={16} />}
              label={t('settings.servers.detail.editAria')}
              onClick={onEditServer}
            />
            <IconButton
              icon={<Trash2 size={16} />}
              label={t('settings.servers.detail.removeAria')}
              onClick={async () => {
                const ok = await confirm({
                  title: t('settings.servers.confirm.removeServerTitle', { name: host.name }),
                  message: t('settings.servers.confirm.removeServerMessage'),
                  danger: true,
                  confirmLabel: t('settings.servers.stack.remove')
                })
                if (ok) {
                  await store.deleteHost(host.id)
                  onBack()
                }
              }}
            />
          </div>
        }
      />

      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <h3 style={{ margin: 0, fontSize: 13, fontWeight: 600 }}>{t('settings.servers.detail.stacks')}</h3>
        <Button onClick={onAddStack} iconLeft={<Plus size={14} />}>
          {t('settings.servers.stack.addButton')}
        </Button>
      </div>

      {unreachable ? (
        <div style={s.banner}>
          <span style={{ color: 'var(--error)', flexShrink: 0, marginTop: 1 }}>
            <AlertTriangle size={20} />
          </span>
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: 13, fontWeight: 600 }}>{t('settings.servers.detail.unreachableTitle')}</div>
            <div style={{ marginTop: 4, color: 'var(--text-secondary)', fontSize: 12, fontFamily: 'var(--font-mono)' }}>
              {result?.message ?? t('settings.servers.detail.sshFailed')}
            </div>
            <div style={{ marginTop: 10 }}>
              <Button disabled={testing} onClick={handleTestSsh} iconLeft={<RefreshCw size={14} />}>
                {t('settings.servers.test.button')}
              </Button>
            </div>
          </div>
        </div>
      ) : host.stacks.length === 0 ? (
        <div style={{ ...s.emptyBox, padding: '32px 24px' }}>
          <div style={{ fontSize: 13.5, fontWeight: 600 }}>{t('settings.servers.detail.emptyTitle')}</div>
          <div style={{ maxWidth: '42ch', color: 'var(--text-secondary)', fontSize: 12.5 }}>
            {t('settings.servers.detail.emptyHint')}
          </div>
          <Button variant="primary" onClick={onAddStack} iconLeft={<Plus size={15} />} style={{ marginTop: 12 }}>
            {t('settings.servers.stack.addButton')}
          </Button>
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
