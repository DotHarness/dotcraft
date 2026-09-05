import { useEffect, useState, type JSX, type ReactNode } from 'react'
import { AlertTriangle, X } from 'lucide-react'

import { SettingsPanelShell } from '../../SettingsPanelShell'
import { Button } from '../../../ui/Button'
import { useT } from '../../../../contexts/LocaleContext'
import { useRemoteServersStore } from '../../../../stores/remoteServersStore'
import { ServerDetail } from './ServerDetail'
import { ServerFormPage } from './ServerFormPage'
import { ServerList } from './ServerList'
import { StackFormPage } from './StackFormPage'
import * as s from './serversStyles'

type ServerFormState =
  | { kind: 'addServer' }
  | { kind: 'editServer'; hostId: string }
  | null

type StackFormState =
  | { kind: 'addStack'; hostId: string }
  | { kind: 'editStack'; hostId: string; stackId: string }
  | null

interface ServersPanelProps {
  /** Set when the Connections page already owns the page header. */
  embedded?: boolean
  /** Reports whether a server detail or form page is open, so a host can yield the surface. */
  onSubPageChange?: (open: boolean) => void
}

export function ServersPanel({ embedded = false, onSubPageChange }: ServersPanelProps = {}): JSX.Element {
  const t = useT()
  const store = useRemoteServersStore()
  const [serverForm, setServerForm] = useState<ServerFormState>(null)
  const [stackForm, setStackForm] = useState<StackFormState>(null)
  const [autoTestedHostIds, setAutoTestedHostIds] = useState<Set<string>>(() => new Set())

  useEffect(() => {
    if (!store.loaded) void store.load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    if (!store.loaded || store.hosts.length === 0) return
    const pending = store.hosts.filter((host) => !autoTestedHostIds.has(host.id) && !store.testing[host.id])
    if (pending.length === 0) return
    setAutoTestedHostIds((prev) => {
      const next = new Set(prev)
      for (const host of pending) next.add(host.id)
      return next
    })
    for (const host of pending) void store.testHost({ id: host.id })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [store.loaded, store.hosts, store.testing, autoTestedHostIds])

  const selectedHost = store.hosts.find((h) => h.id === store.selectedHostId) ?? null
  const editingHost =
    serverForm?.kind === 'editServer'
      ? store.hosts.find((h) => h.id === serverForm.hostId) ?? null
      : null
  const stackFormHost = stackForm ? store.hosts.find((h) => h.id === stackForm.hostId) ?? null : null
  const editingStack =
    stackForm?.kind === 'editStack'
      ? stackFormHost?.stacks.find((st) => st.id === stackForm.stackId) ?? null
      : null

  const subPageOpen = serverForm != null || stackForm != null || selectedHost != null
  useEffect(() => {
    onSubPageChange?.(subPageOpen)
  }, [onSubPageChange, subPageOpen])

  if (serverForm?.kind === 'addServer') {
    return (
      <ServerFormPage
        onBack={() => setServerForm(null)}
        onSaved={(host) => {
          store.selectHost(host.id)
          setServerForm(null)
        }}
      />
    )
  }

  if (serverForm?.kind === 'editServer' && editingHost) {
    return (
      <ServerFormPage
        host={editingHost}
        onBack={() => setServerForm(null)}
        onSaved={(host) => {
          store.selectHost(host.id)
          setServerForm(null)
        }}
      />
    )
  }

  if (stackForm?.kind === 'addStack' && stackFormHost) {
    return (
      <StackFormPage
        host={stackFormHost}
        onBack={() => setStackForm(null)}
        onSaved={(host) => {
          store.selectHost(host.id)
          setStackForm(null)
        }}
      />
    )
  }

  if (stackForm?.kind === 'editStack' && stackFormHost && editingStack) {
    return (
      <StackFormPage
        host={stackFormHost}
        stack={editingStack}
        onBack={() => setStackForm(null)}
        onSaved={(host) => {
          store.selectHost(host.id)
          setStackForm(null)
        }}
      />
    )
  }

  if (selectedHost) {
    return (
      <ServerDetail
        host={selectedHost}
        onBack={() => store.selectHost(null)}
        onEditServer={() => setServerForm({ kind: 'editServer', hostId: selectedHost.id })}
        onAddStack={() => setStackForm({ kind: 'addStack', hostId: selectedHost.id })}
        onEditStack={(stack) => setStackForm({ kind: 'editStack', hostId: selectedHost.id, stackId: stack.id })}
      />
    )
  }

  const list: ReactNode = (
    <>
      {store.error && (
        <div style={s.banner}>
          <span style={{ color: 'var(--error)', marginTop: 1 }}>
            <AlertTriangle size={18} />
          </span>
          <div style={{ flex: 1, fontSize: 12.5, color: 'var(--text-secondary)' }}>{store.error}</div>
          <Button variant="ghost" size="icon" aria-label={t('settings.servers.error.dismiss')} onClick={() => store.clearError()}>
            <X size={16} />
          </Button>
        </div>
      )}
      <ServerList
        hosts={store.hosts}
        onOpen={(id) => store.selectHost(id)}
        onAdd={() => setServerForm({ kind: 'addServer' })}
      />
    </>
  )

  if (embedded) {
    return <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>{list}</div>
  }

  return <SettingsPanelShell title={t('settings.servers.title')}>{list}</SettingsPanelShell>
}
