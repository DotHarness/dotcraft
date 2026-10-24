import { FolderCog } from 'lucide-react'
import { useCallback, useEffect, useId, useMemo, useRef, useState, type JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import {
  documentFromDraft,
  draftFromDocument,
  isDraftDirty,
  saveBlockReason,
  type EnvironmentDraft
} from '../settings/panels/worktrees/environmentDraft'
import { EnvironmentDraftFields } from '../settings/panels/worktrees/EnvironmentDraftFields'
import { projectFolderName } from '../settings/panels/worktrees/environmentPresentation'
import { Button } from '../ui/Button'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { DisclosureButton } from '../ui/DisclosureButton'
import { Textarea } from '../ui/Input'
import { ModalDialog, ModalFooter } from '../ui/ModalDialog'
import { ModalHeader } from '../ui/ModalHeader'
import { setUpProject } from './projectSetup'
import styles from './SetUpProjectDialog.module.css'

export interface SetUpProjectTarget {
  workspacePath: string
  threadId: string
}

interface ActiveRequest {
  id: number
  target: SetUpProjectTarget
  returnFocus: HTMLElement | null
  resolve(saved: boolean): void
}

let activeRequest: ActiveRequest | null = null
let requestListener: ((request: ActiveRequest | null) => void) | null = null
let nextRequestId = 0

function SetUpProjectDialog({
  target,
  onFinish
}: {
  target: SetUpProjectTarget
  onFinish(saved: boolean): void
}): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const titleId = useId()
  const scriptId = useId()
  const scriptRef = useRef<HTMLTextAreaElement>(null)
  const initial = useMemo(() => draftFromDocument(null, projectFolderName(target.workspacePath)), [target.workspacePath])
  const [draft, setDraft] = useState<EnvironmentDraft>(initial)
  const [advanced, setAdvanced] = useState(false)
  const [busy, setBusy] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const dirty = isDraftDirty(draft, initial, false)
  const canSave = !busy
    && draft.setup.default.trim() !== ''
    && saveBlockReason({ saving: false, readable: true, draft, dirty: true }) === null

  const update = useCallback((change: (current: EnvironmentDraft) => EnvironmentDraft) => {
    setDraft((current) => change(current))
  }, [])

  const close = useCallback(async (): Promise<void> => {
    if (busy || confirming) return
    if (dirty) {
      setConfirming(true)
      const discard = await confirm({
        title: t('projectActions.setup.discardTitle'),
        message: t('projectActions.setup.discardMessage'),
        confirmLabel: t('projectActions.setup.discard'),
        cancelLabel: t('projectActions.setup.keepEditing'),
        danger: true
      })
      setConfirming(false)
      if (!discard) return
    }
    onFinish(false)
  }, [busy, confirm, confirming, dirty, onFinish, t])

  useEffect(() => {
    scriptRef.current?.focus()
  }, [])

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key !== 'Escape' || confirming) return
      event.stopPropagation()
      void close()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [close, confirming])

  async function save(): Promise<void> {
    if (!canSave) return
    setBusy(true)
    setError(null)
    const result = await setUpProject({ ...target, document: documentFromDraft(draft), t })
    setBusy(false)
    if (result.ok) onFinish(true)
    else setError(t(result.reason === 'conflict' ? 'projectActions.setup.conflict' : 'projectActions.setup.failed'))
  }

  return (
    <ModalDialog
      titleId={titleId}
      width={560}
      onDismiss={() => void close()}
      onSubmit={() => void save()}
      header={(
        <ModalHeader
          icon={<FolderCog size={18} aria-hidden />}
          title={t('projectActions.setup.title')}
          titleId={titleId}
          description={t('projectActions.setup.description')}
          onClose={busy ? undefined : () => void close()}
          closeLabel={t('common.cancel')}
        />
      )}
      error={error}
      footer={(
        <ModalFooter>
          <Button variant="secondary" disabled={busy} onClick={() => void close()}>
            {t('common.cancel')}
          </Button>
          <Button type="submit" variant="primary" loading={busy} disabled={!canSave}>
            {t('projectActions.setup.submit')}
          </Button>
        </ModalFooter>
      )}
    >
      {advanced ? null : (
        <div className={styles.field}>
          <label className={styles.label} htmlFor={scriptId}>{t('projectActions.setup.script')}</label>
          <Textarea
            ref={scriptRef}
            id={scriptId}
            mono
            rows={4}
            spellCheck={false}
            className={styles.script}
            value={draft.setup.default}
            disabled={busy}
            placeholder={t('projectActions.setup.scriptPlaceholder')}
            onChange={(event) => {
              const value = event.target.value
              update((current) => ({ ...current, setup: { ...current.setup, default: value } }))
            }}
          />
        </div>
      )}
      <DisclosureButton expanded={advanced} onClick={() => setAdvanced((value) => !value)}>
        {t('projectActions.setup.advanced')}
      </DisclosureButton>
      {advanced ? (
        <div className={styles.advanced}>
          <EnvironmentDraftFields
            draft={draft}
            update={update}
            disabled={busy}
            nameInvalid={draft.name.trim() === ''}
          />
        </div>
      ) : null}
    </ModalDialog>
  )
}

export function SetUpProjectDialogHost(): JSX.Element | null {
  const [request, setRequest] = useState<ActiveRequest | null>(activeRequest)

  useEffect(() => {
    requestListener = setRequest
    setRequest(activeRequest)
    return () => {
      if (requestListener === setRequest) requestListener = null
      if (activeRequest) finishRequest(activeRequest, false)
    }
  }, [])

  if (!request) return null
  return (
    <SetUpProjectDialog
      key={request.id}
      target={request.target}
      onFinish={(saved) => finishRequest(request, saved)}
    />
  )
}

export function requestSetUpProjectDialog(target: SetUpProjectTarget): {
  result: Promise<boolean>
  dismiss(): void
} {
  if (activeRequest) return { result: Promise.resolve(false), dismiss() {} }
  let resolve!: (saved: boolean) => void
  const result = new Promise<boolean>((settle) => {
    resolve = settle
  })
  const request: ActiveRequest = {
    id: ++nextRequestId,
    target,
    returnFocus: document.activeElement instanceof HTMLElement ? document.activeElement : null,
    resolve
  }
  activeRequest = request
  requestListener?.(request)
  return { result, dismiss: () => finishRequest(request, false) }
}

function finishRequest(request: ActiveRequest, saved: boolean): void {
  if (activeRequest?.id !== request.id) return
  activeRequest = null
  requestListener?.(null)
  request.returnFocus?.focus()
  request.resolve(saved)
}
