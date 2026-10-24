import { useEffect, useId, useRef, useState, type JSX } from 'react'
import { Bug, FlaskConical, Play, SquareTerminal, Wrench, type LucideIcon } from 'lucide-react'
import type {
  AddProjectActionResult,
  ProjectActionIcon,
  ProjectActionPlatform
} from '../../../shared/projectActions'
import { useT } from '../../contexts/LocaleContext'
import { openEnvironmentSettings } from '../../stores/appNavigationStore'
import { Button } from '../ui/Button'
import { Input } from '../ui/Input'
import { ModalDialog, ModalFooter } from '../ui/ModalDialog'
import { ModalHeader } from '../ui/ModalHeader'
import { Select, type SelectOption } from '../ui/Select'
import styles from './AddProjectActionDialog.module.css'

export const PROJECT_ACTION_GLYPHS: Record<ProjectActionIcon, LucideIcon> = {
  tool: Wrench,
  run: Play,
  debug: Bug,
  test: FlaskConical
}

export interface AddProjectActionTarget {
  workspacePath: string
  environmentId?: string
  revision: string | null
}

export type SavedProjectAction = Extract<AddProjectActionResult, { ok: true }>

interface ActiveRequest {
  id: number
  target: AddProjectActionTarget
  returnFocus: HTMLElement | null
  resolve(result: SavedProjectAction | null): void
}

let activeRequest: ActiveRequest | null = null
let requestListener: ((request: ActiveRequest | null) => void) | null = null
let nextRequestId = 0

const ICONS: readonly ProjectActionIcon[] = ['tool', 'run', 'debug', 'test']
const ANY_PLATFORM = 'any'
const PLATFORMS: ReadonlyArray<{ value: ProjectActionPlatform; label: string }> = [
  { value: 'win32', label: 'Windows' },
  { value: 'darwin', label: 'macOS' },
  { value: 'linux', label: 'Linux' }
]

function AddProjectActionDialog({
  target,
  onFinish
}: {
  target: AddProjectActionTarget
  onFinish(result: SavedProjectAction | null): void
}): JSX.Element {
  const t = useT()
  const titleId = useId()
  const nameId = useId()
  const commandId = useId()
  const iconId = useId()
  const platformId = useId()
  const nameRef = useRef<HTMLInputElement>(null)
  const [name, setName] = useState('')
  const [command, setCommand] = useState('')
  const [icon, setIcon] = useState<ProjectActionIcon>('run')
  const [platform, setPlatform] = useState<ProjectActionPlatform | typeof ANY_PLATFORM>(ANY_PLATFORM)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const canSave = name.trim() !== '' && command.trim() !== '' && !busy

  useEffect(() => {
    nameRef.current?.focus()
  }, [])

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape' && !busy) onFinish(null)
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [busy, onFinish])

  async function save(): Promise<void> {
    if (!canSave) return
    setBusy(true)
    setError(null)
    let result: AddProjectActionResult
    try {
      result = await window.api.projectActions.add({
        ...target,
        action: {
          name: name.trim(),
          icon,
          command: command.trim(),
          ...(platform === ANY_PLATFORM ? {} : { platform })
        }
      })
    } catch {
      result = { ok: false, reason: 'failed' }
    }
    setBusy(false)
    if (result.ok) onFinish(result)
    else setError(t(result.reason === 'conflict' ? 'projectActions.add.conflict' : 'projectActions.add.failed'))
  }

  const iconOptions: SelectOption<ProjectActionIcon>[] = ICONS.map((value) => {
    const Glyph = PROJECT_ACTION_GLYPHS[value]
    return { value, label: t(`projectActions.icon.${value}`), icon: <Glyph size={14} aria-hidden /> }
  })
  const platformOptions: SelectOption<ProjectActionPlatform | typeof ANY_PLATFORM>[] = [
    { value: ANY_PLATFORM, label: t('projectActions.add.anyPlatform') },
    ...PLATFORMS
  ]

  return (
    <ModalDialog
      titleId={titleId}
      width={440}
      onDismiss={busy ? undefined : () => onFinish(null)}
      onSubmit={() => void save()}
      bodyClassName={styles.body}
      header={(
        <ModalHeader
          icon={<SquareTerminal size={18} aria-hidden />}
          title={t('projectActions.add.title')}
          titleId={titleId}
          description={t('projectActions.add.description')}
          onClose={busy ? undefined : () => onFinish(null)}
          closeLabel={t('common.cancel')}
        />
      )}
      error={error}
      footer={(
        <ModalFooter
          leading={(
            <button
              type="button"
              className={styles.settingsLink}
              disabled={busy}
              onClick={() => {
                onFinish(null)
                openEnvironmentSettings({
                  projectPath: target.workspacePath,
                  ...(target.environmentId ? { environmentId: target.environmentId } : {})
                })
              }}
            >
              {t('projectActions.add.environmentSettings')}
            </button>
          )}
        >
          <Button variant="secondary" disabled={busy} onClick={() => onFinish(null)}>
            {t('common.cancel')}
          </Button>
          <Button type="submit" variant="primary" loading={busy} disabled={!canSave}>
            {t('projectActions.add.save')}
          </Button>
        </ModalFooter>
      )}
    >
      <div className={styles.row}>
        <div className={styles.field}>
          <label className={styles.label} htmlFor={nameId}>{t('projectActions.add.name')}</label>
          <Input
            ref={nameRef}
            id={nameId}
            value={name}
            disabled={busy}
            placeholder={t('projectActions.add.namePlaceholder')}
            onChange={(event) => setName(event.target.value)}
          />
        </div>
        <div className={styles.field}>
          <label className={styles.label} htmlFor={iconId}>{t('projectActions.add.icon')}</label>
          <Select id={iconId} value={icon} options={iconOptions} disabled={busy} onValueChange={setIcon} adaptiveWidth={false} />
        </div>
      </div>
      <div className={styles.field}>
        <label className={styles.label} htmlFor={commandId}>{t('projectActions.add.command')}</label>
        <Input
          id={commandId}
          mono
          spellCheck={false}
          value={command}
          disabled={busy}
          placeholder={t('projectActions.add.commandPlaceholder')}
          onChange={(event) => setCommand(event.target.value)}
        />
      </div>
      <div className={styles.field}>
        <label className={styles.label} htmlFor={platformId}>{t('projectActions.add.platform')}</label>
        <Select
          id={platformId}
          value={platform}
          options={platformOptions}
          disabled={busy}
          onValueChange={setPlatform}
          adaptiveWidth={false}
        />
      </div>
    </ModalDialog>
  )
}

export function AddProjectActionDialogHost(): JSX.Element | null {
  const [request, setRequest] = useState<ActiveRequest | null>(activeRequest)

  useEffect(() => {
    requestListener = setRequest
    setRequest(activeRequest)
    return () => {
      if (requestListener === setRequest) requestListener = null
      if (activeRequest) finishRequest(activeRequest, null)
    }
  }, [])

  if (!request) return null
  return (
    <AddProjectActionDialog
      key={request.id}
      target={request.target}
      onFinish={(result) => finishRequest(request, result)}
    />
  )
}

export function requestAddProjectActionDialog(target: AddProjectActionTarget): {
  result: Promise<SavedProjectAction | null>
  dismiss(): void
} {
  if (activeRequest) return { result: Promise.resolve(null), dismiss() {} }
  let resolve!: (result: SavedProjectAction | null) => void
  const result = new Promise<SavedProjectAction | null>((settle) => {
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
  return { result, dismiss: () => finishRequest(request, null) }
}

function finishRequest(request: ActiveRequest, result: SavedProjectAction | null): void {
  if (activeRequest?.id !== request.id) return
  activeRequest = null
  requestListener?.(null)
  request.returnFocus?.focus()
  request.resolve(result)
}
