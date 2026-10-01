import { useEffect, useId, useState, type JSX } from 'react'
import { RefreshCw, Server } from 'lucide-react'

import { Button } from '../../../ui/Button'
import { Checkbox } from '../../../ui/Checkbox'
import { IconButton } from '../../../ui/IconButton'
import { ModalHeader } from '../../../ui/ModalHeader'
import { SkeletonList } from '../../../ui/Skeleton'
import { useT } from '../../../../contexts/LocaleContext'
import { useSshMachinesStore } from '../../../../stores/sshMachinesStore'
import { addToast } from '../../../../stores/toastStore'
import type { SshMachineValidation } from '../../../../../shared/sshMachines'
import { SshDialogFrame } from './SshDialogFrame'
import { EMPTY_MACHINE_DRAFT, SshMachineForm, parseMachineDraft, type MachineDraft } from './SshMachineForm'

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

export function AddSshConnectionDialog({ onClose }: { onClose: () => void }): JSX.Element {
  const t = useT()
  const titleId = useId()
  const machines = useSshMachinesStore((s) => s.machines)
  const discovery = useSshMachinesStore((s) => s.discovery)
  const discovering = useSshMachinesStore((s) => s.discovering)
  const discoverHosts = useSshMachinesStore((s) => s.discoverHosts)
  const add = useSshMachinesStore((s) => s.add)
  const [view, setView] = useState<'hosts' | 'manual'>('hosts')
  const [selected, setSelected] = useState<Set<string>>(() => new Set())
  const [adding, setAdding] = useState(false)
  const [serverErrors, setServerErrors] = useState<SshMachineValidation | undefined>()

  useEffect(() => {
    void discoverHosts()
  }, [discoverHosts])

  const hosts = discovery?.hosts ?? []
  const loading = discovering || discovery == null

  function toggle(alias: string, on: boolean): void {
    setSelected((current) => {
      const next = new Set(current)
      if (on) next.add(alias)
      else next.delete(alias)
      return next
    })
  }

  async function addSelected(): Promise<void> {
    setAdding(true)
    try {
      const result = await add(
        hosts.filter((host) => selected.has(host.alias)).map((host) => ({ source: 'sshConfig' as const, alias: host.alias }))
      )
      if (result.ok) onClose()
      else addToast(t('settings.ssh.add.failed'), 'error')
    } catch (error) {
      addToast(messageOf(error), 'error')
    } finally {
      setAdding(false)
    }
  }

  async function addManual(draft: MachineDraft): Promise<void> {
    const parsed = parseMachineDraft(draft)
    if ('portError' in parsed) return
    try {
      const result = await add([{ source: 'manual', ...parsed }])
      if (result.ok) onClose()
      else setServerErrors(result.errors)
    } catch (error) {
      addToast(messageOf(error), 'error')
    }
  }

  return (
    <SshDialogFrame titleId={titleId} onClose={onClose}>
      <ModalHeader
        icon={<Server size={18} aria-hidden />}
        title={t('settings.ssh.add.title')}
        titleId={titleId}
        description={t(view === 'hosts' ? 'settings.ssh.add.description' : 'settings.ssh.add.manualDescription')}
        onClose={onClose}
        closeLabel={t('common.close')}
        actions={
          view === 'hosts' ? (
            <IconButton
              icon={<RefreshCw size={15} aria-hidden />}
              label={t('settings.ssh.add.refresh')}
              size={30}
              disabled={loading}
              onClick={() => void discoverHosts()}
            />
          ) : null
        }
      />
      {view === 'manual' ? (
        <SshMachineForm
          initial={EMPTY_MACHINE_DRAFT}
          machines={machines}
          submitLabel={t('settings.ssh.add.submit')}
          serverErrors={serverErrors}
          onBack={() => setView('hosts')}
          onSubmit={addManual}
        />
      ) : (
        <>
          <div className="dc-ssh-hosts" aria-busy={loading}>
            {loading ? (
              <SkeletonList
                count={3}
                ariaLabel={t('settings.ssh.add.reading')}
                rowProps={{ lines: ['38%'], lineHeight: 12 }}
                rowStyle={{ padding: '10px 12px' }}
              />
            ) : hosts.length === 0 ? (
              <div className="dc-ssh-hosts__empty">{discovery?.error || t('settings.ssh.add.empty')}</div>
            ) : (
              <>
                <div className="dc-ssh-hosts__source">~/.ssh/config</div>
                {hosts.map((host) => (
                  <div key={host.alias} className="dc-ssh-host" data-added={host.added || undefined}>
                    <Checkbox
                      checked={host.added || selected.has(host.alias)}
                      disabled={host.added}
                      onChange={(on) => toggle(host.alias, on)}
                      ariaLabel={host.alias}
                      label={
                        <span className="dc-ssh-host__label">
                          <span className="dc-ssh-host__alias">{host.alias}</span>
                          {host.resolvedHost && <span className="dc-ssh-host__target">{host.resolvedHost}</span>}
                        </span>
                      }
                    />
                    {host.added && <span className="dc-ssh-host__added">{t('settings.ssh.added')}</span>}
                  </div>
                ))}
              </>
            )}
          </div>
          <div className="dc-ssh-footer">
            <Button variant="ghost" onClick={() => setView('manual')}>
              {t('settings.ssh.add.manual')}
            </Button>
            <span className="dc-ssh-footer__spacer" />
            <Button
              variant="primary"
              disabled={selected.size === 0}
              loading={adding}
              onClick={() => void addSelected()}
            >
              {t('settings.ssh.add.submit')}
            </Button>
          </div>
        </>
      )}
    </SshDialogFrame>
  )
}
