import { useCallback, useEffect, useId, useRef, useState, type JSX } from 'react'
import { ChevronRight, Folder, FolderPlus } from 'lucide-react'

import { Button } from '../../../ui/Button'
import { ModalHeader } from '../../../ui/ModalHeader'
import { Select } from '../../../ui/Select'
import { Skeleton, SkeletonList } from '../../../ui/Skeleton'
import { useT } from '../../../../contexts/LocaleContext'
import { useSshMachinesStore } from '../../../../stores/sshMachinesStore'
import { addToast } from '../../../../stores/toastStore'
import type { RemoteFolderListing, RemoteProject } from '../../../../../shared/sshMachines'
import { SshDialogFrame } from './SshDialogFrame'
import { canAddProject } from './sshMachinePresentation'

interface Crumb {
  label: string
  path: string
}

function crumbsFor(listing: RemoteFolderListing): Crumb[] {
  const { path, home } = listing
  const underHome = path === home || path.startsWith(`${home}/`)
  const root: Crumb = underHome ? { label: '~', path: home } : { label: '/', path: '/' }
  const rest = underHome ? path.slice(home.length) : path
  const crumbs = [root]
  let current = root.path
  for (const segment of rest.split('/').filter(Boolean)) {
    current = current === '/' ? `/${segment}` : `${current}/${segment}`
    crumbs.push({ label: segment, path: current })
  }
  return crumbs
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

export function RemoteProjectPickerDialog({
  initialMachineId,
  onClose,
  onAdded
}: {
  initialMachineId: string
  onClose: () => void
  onAdded: (machineId: string, project: RemoteProject) => void
}): JSX.Element {
  const t = useT()
  const titleId = useId()
  const machines = useSshMachinesStore((s) => s.machines)
  const addProject = useSshMachinesStore((s) => s.addProject)
  const [machineId, setMachineId] = useState(initialMachineId)
  const [listing, setListing] = useState<RemoteFolderListing | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)
  const request = useRef(0)
  const machine = machines.find((candidate) => candidate.id === machineId)
  const ready = machine != null && canAddProject(machine)

  const browse = useCallback(
    async (path?: string) => {
      const token = ++request.current
      setLoading(true)
      setError(null)
      try {
        const next = await window.api.sshMachines.listFolders(machineId, path)
        if (token !== request.current) return
        setListing(next)
      } catch (err) {
        if (token !== request.current) return
        setError(messageOf(err))
      } finally {
        if (token === request.current) setLoading(false)
      }
    },
    [machineId]
  )

  useEffect(() => {
    setListing(null)
    if (ready) void browse()
    else setLoading(false)
  }, [browse, ready])

  const already = listing != null && (machine?.projects.some((project) => project.path === listing.path) ?? false)

  async function use(): Promise<void> {
    if (!listing) return
    setAdding(true)
    try {
      const project = await addProject(machineId, listing.path)
      onAdded(machineId, project)
    } catch (err) {
      addToast(messageOf(err), 'error')
    } finally {
      setAdding(false)
    }
  }

  return (
    <SshDialogFrame titleId={titleId} wide onClose={onClose}>
      <ModalHeader
        icon={<FolderPlus size={18} aria-hidden />}
        title={t('settings.ssh.picker.title')}
        titleId={titleId}
        description={t('settings.ssh.picker.description')}
        onClose={onClose}
        closeLabel={t('common.close')}
      />
      <div className="dc-ssh-picker__machine">
        <span className="dc-ssh-field__label">{t('settings.ssh.picker.machine')}</span>
        <Select
          ariaLabel={t('settings.ssh.picker.machine')}
          value={machineId}
          options={machines.map((candidate) => ({
            value: candidate.id,
            label: candidate.name,
            disabled: !canAddProject(candidate),
            ...(canAddProject(candidate) ? {} : { description: t('settings.ssh.picker.connectFirst') })
          }))}
          onValueChange={(value) => setMachineId(value)}
        />
      </div>
      {listing && (
        <nav className="dc-ssh-crumbs" aria-label={t('settings.ssh.picker.path')}>
          {crumbsFor(listing).map((crumb, index, all) => (
            <span key={crumb.path} className="dc-ssh-crumbs__item">
              {index > 0 && <ChevronRight size={13} aria-hidden className="dc-ssh-crumbs__sep" />}
              {index === all.length - 1 ? (
                <span className="dc-ssh-crumbs__current" aria-current="location">
                  {crumb.label}
                </span>
              ) : (
                <button type="button" className="dc-ssh-crumbs__link" onClick={() => void browse(crumb.path)}>
                  {crumb.label}
                </button>
              )}
            </span>
          ))}
        </nav>
      )}
      <div className="dc-ssh-folders" aria-busy={loading}>
        {loading ? (
          <SkeletonList
            count={4}
            ariaLabel={t('settings.ssh.picker.loading')}
            rowProps={{ media: 16, mediaRadius: 4, lines: ['34%'], lineHeight: 12 }}
            rowStyle={{ padding: '9px 12px' }}
          />
        ) : !ready ? (
          <div className="dc-ssh-folders__empty">{t('settings.ssh.picker.connectFirst')}</div>
        ) : error ? (
          <div className="dc-ssh-folders__empty" role="alert">
            {error}
          </div>
        ) : listing && listing.folders.length === 0 ? (
          <div className="dc-ssh-folders__empty">
            {t('settings.ssh.picker.noFolders', { folder: crumbsFor(listing).slice(-1)[0].label })}
          </div>
        ) : (
          listing?.folders.map((folder) => (
            <button key={folder.path} type="button" className="dc-ssh-folder" onClick={() => void browse(folder.path)}>
              <Folder size={15} aria-hidden className="dc-ssh-folder__icon" />
              <span className="dc-ssh-folder__name">{folder.name}</span>
              {folder.saved && <span className="dc-ssh-folder__meta">{t('settings.ssh.added')}</span>}
              <ChevronRight size={15} aria-hidden className="dc-ssh-folder__chevron" />
            </button>
          ))
        )}
      </div>
      <div className="dc-ssh-footer">
        <span className="dc-ssh-footer__note">
          {loading ? <Skeleton width={120} height={10} /> : already ? t('settings.ssh.picker.already') : null}
        </span>
        <span className="dc-ssh-footer__spacer" />
        <Button
          variant="primary"
          disabled={loading || already || !listing || !ready || error != null}
          loading={adding}
          onClick={() => void use()}
        >
          {t('settings.ssh.picker.use')}
        </Button>
      </div>
    </SshDialogFrame>
  )
}
