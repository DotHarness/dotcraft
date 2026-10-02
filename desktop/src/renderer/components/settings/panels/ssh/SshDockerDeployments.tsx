import { useCallback, useEffect, useRef, useState, type JSX } from 'react'
import { Container } from 'lucide-react'

import { Button } from '../../../ui/Button'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import { ContextMenu, type ContextMenuEntry, type ContextMenuPosition } from '../../../ui/ContextMenu'
import { MoreActionsButton } from '../../../ui/MoreActionsButton'
import { StatusIndicator, type StatusTone } from '../../../ui/StatusIndicator'
import { useT } from '../../../../contexts/LocaleContext'
import { addToast } from '../../../../stores/toastStore'
import type { MessageKey } from '../../../../../shared/locales'
import type {
  RemoteStack,
  RemoteStackAction,
  RemoteStackStatus,
  StackHealth
} from '../../../../../shared/dockerDeployments'
import type { SshMachineView } from '../../../../../shared/sshMachines'

const HEALTH: Record<StackHealth, { tone: StatusTone; key: MessageKey }> = {
  running: { tone: 'success', key: 'settings.ssh.docker.health.running' },
  partial: { tone: 'warning', key: 'settings.ssh.docker.health.partial' },
  stopped: { tone: 'neutral', key: 'settings.ssh.docker.health.stopped' },
  unhealthy: { tone: 'error', key: 'settings.ssh.docker.health.unhealthy' },
  unknown: { tone: 'neutral', key: 'settings.ssh.docker.health.unknown' }
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

function sameDeployment(a: { composeDir: string }, b: { composeDir: string }): boolean {
  return a.composeDir.replace(/\/+$/, '') === b.composeDir.replace(/\/+$/, '')
}

export function useDockerDeployments(machine: SshMachineView, reachable: boolean): RemoteStack[] {
  const discovered = useRef(false)
  useEffect(() => {
    if (!reachable || !machine.system?.hasDocker || discovered.current) return
    discovered.current = true
    void (async () => {
      try {
        const found = await window.api.sshMachines.docker.discover(machine.id)
        for (const stack of found) {
          if (machine.stacks.some((recorded) => sameDeployment(recorded, stack))) continue
          await window.api.sshMachines.docker.save(machine.id, stack)
        }
      } catch {
        discovered.current = false
      }
    })()
  }, [machine.id, machine.stacks, machine.system?.hasDocker, reachable])
  return machine.stacks
}

function DockerRow({ machineId, stack }: { machineId: string; stack: RemoteStack }): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const [status, setStatus] = useState<RemoteStackStatus | null>(null)
  const [busy, setBusy] = useState(false)
  const [logs, setLogs] = useState<string | null>(null)
  const [menu, setMenu] = useState<ContextMenuPosition | null>(null)
  const more = useRef<HTMLButtonElement>(null)

  const refresh = useCallback(async () => {
    try {
      setStatus(await window.api.sshMachines.docker.status(machineId, stack.id))
    } catch {
      setStatus(null)
    }
  }, [machineId, stack.id])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const running = status?.health === 'running' || status?.health === 'partial'
  const health = HEALTH[status?.health ?? 'unknown']

  async function run(action: RemoteStackAction): Promise<void> {
    if (action === 'stop') {
      const ok = await confirm({
        title: t('settings.ssh.docker.stopTitle', { name: stack.name }),
        message: t('settings.ssh.docker.stopMessage'),
        confirmLabel: t('settings.ssh.docker.stop'),
        cancelLabel: t('common.cancel'),
        danger: true
      })
      if (!ok) return
    }
    setBusy(true)
    try {
      const result = await window.api.sshMachines.docker.action(machineId, stack.id, action)
      if (result.status) setStatus(result.status)
      else await refresh()
      if (!result.ok) addToast(result.message || t('settings.ssh.docker.failed'), 'error')
      else if (action === 'update') addToast(t(result.changed ? 'settings.ssh.docker.updated' : 'settings.ssh.docker.upToDate'), 'success')
    } catch (error) {
      addToast(messageOf(error), 'error')
    } finally {
      setBusy(false)
    }
  }

  async function guarded(task: () => Promise<unknown>): Promise<void> {
    try {
      await task()
    } catch (error) {
      addToast(messageOf(error), 'error')
    }
  }

  async function toggleLogs(): Promise<void> {
    if (logs != null) {
      setLogs(null)
      return
    }
    setLogs('')
    try {
      const result = await window.api.sshMachines.docker.logs(machineId, stack.id, { tail: 200 })
      setLogs(result.text || t('settings.ssh.docker.noLogs'))
    } catch (error) {
      setLogs(messageOf(error))
    }
  }

  const items: ContextMenuEntry[] = [
    { label: t('settings.ssh.docker.restart'), disabled: busy, onClick: () => void run('restart') },
    running
      ? { label: t('settings.ssh.docker.stop'), disabled: busy, onClick: () => void run('stop') }
      : { label: t('settings.ssh.docker.start'), disabled: busy, onClick: () => void run('start') },
    { label: t('settings.ssh.docker.update'), disabled: busy, onClick: () => void run('update') },
    { type: 'separator' },
    {
      label: t('settings.ssh.docker.dashboard'),
      disabled: !running,
      onClick: () => void guarded(() => window.api.sshMachines.docker.openDashboard(machineId, stack.id))
    }
  ]

  const details = [
    t(health.key),
    status?.appVersion ? `DotCraft ${status.appVersion}` : undefined,
    t('settings.ssh.docker.port', { port: stack.appServerPort })
  ].filter(Boolean).join(' · ')

  return (
    <div className="dc-ssh-docker">
      <div className="dc-ssh-docker__row">
        <span className="dc-ssh-docker__icon" aria-hidden>
          <Container size={16} />
        </span>
        <span className="dc-ssh-docker__body">
          <span className="dc-ssh-docker__name">{stack.name}</span>
          <span className="dc-ssh-status">
            <StatusIndicator tone={busy || status == null ? 'pending' : health.tone} />
            <span className="dc-ssh-status__text">{details}</span>
          </span>
        </span>
        <span className="dc-ssh-row__actions">
          <Button
            variant="secondary"
            disabled={!running}
            onClick={() => void guarded(() => window.api.sshMachines.docker.open(machineId, stack.id))}
          >
            {t('settings.ssh.open')}
          </Button>
          <Button variant="ghost" aria-expanded={logs != null} onClick={() => void toggleLogs()}>
            {t('settings.ssh.docker.logs')}
          </Button>
          <MoreActionsButton
            ref={more}
            label={t('settings.ssh.moreActionsFor', { name: stack.name })}
            open={menu != null}
            onClick={() => {
              const rect = more.current?.getBoundingClientRect()
              if (rect) setMenu({ x: rect.right - 200, y: rect.bottom + 4 })
            }}
          />
        </span>
      </div>
      {logs != null && (
        <pre className="dc-ssh-docker__logs" aria-busy={logs === ''}>
          {logs}
        </pre>
      )}
      {menu && <ContextMenu items={items} position={menu} onClose={() => setMenu(null)} />}
    </div>
  )
}

export function SshDockerDeployments({ machine, stacks }: { machine: SshMachineView; stacks: RemoteStack[] }): JSX.Element {
  const t = useT()
  return (
    <section className="dc-ssh-section">
      <header className="dc-ssh-section__head">
        <div>
          <h3>{t('settings.ssh.docker.title')}</h3>
          <p className="dc-ssh-section__description">
            {t(stacks.length === 1 ? 'settings.ssh.docker.found.one' : 'settings.ssh.docker.found.other', {
              count: stacks.length
            })}
          </p>
        </div>
      </header>
      <div className="dc-ssh-docker-list">
        {stacks.map((stack) => (
          <DockerRow key={stack.id} machineId={machine.id} stack={stack} />
        ))}
      </div>
    </section>
  )
}
