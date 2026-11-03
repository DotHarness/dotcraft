import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { ChevronRight, FilePlus2, Plus, Settings, X } from 'lucide-react'
import { Button, Combobox, Input, ModalHeader, Select, Textarea } from './ui'
import { type OratorioTask } from './oratorio-model'
import { useOratorioLocalTaskT } from './oratorio-local-task-i18n'
import { loadOratorioSettings } from './settings/oratorio-settings-service'
import { localTaskRepositoryOptions, type LocalTaskRepositoryOption } from './oratorio-local-task-repositories'
import './oratorio-new-local-task.css'

const LAST_REPOSITORY_KEY = 'dotcraft.oratorio.localTask.repository'
const DEFAULT_LABELS = ['bug', 'feature', 'docs', 'frontend', 'backend', 'security', 'test', 'chore', 'review', 'oratorio:auto']

export interface NewLocalTaskDraft {
  title: string
  description: string
  repository: string
  labels: string[]
  assignee?: string
  branch?: string
}

export interface NewLocalTaskDialogProps {
  tasks: ReadonlyArray<OratorioTask>
  onCancel: () => void
  onCreate: (task: NewLocalTaskDraft) => void
  onOpenSettings: () => void
}

type RepositoryState = { status: 'loading' } | { status: 'error' } | { status: 'ready'; options: LocalTaskRepositoryOption[] }

export function NewLocalTaskDialog({ tasks, onCancel, onCreate, onOpenSettings }: NewLocalTaskDialogProps): JSX.Element {
  const t = useOratorioLocalTaskT()
  const [repositories, setRepositories] = useState<RepositoryState>({ status: 'loading' })
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [repository, setRepository] = useState('')
  const [labels, setLabels] = useState<string[]>([])
  const [assignee, setAssignee] = useState('')
  const [branch, setBranch] = useState('')
  const [addingLabel, setAddingLabel] = useState(false)
  const [labelDraft, setLabelDraft] = useState('')

  useEffect(() => {
    loadOratorioSettings()
      .then(({ settings }) => {
        const options = localTaskRepositoryOptions(settings.projects)
        setRepositories({ status: 'ready', options })
        setRepository(resolveInitialRepository(options))
      })
      .catch(() => setRepositories({ status: 'error' }))
  }, [])

  const assignees = useMemo(() => unique(tasks.map((task) => task.assignee).filter((value): value is string => Boolean(value))), [tasks])
  const branchCandidates = useMemo(() => {
    const scoped = repository ? tasks.filter((task) => task.repository === repository) : []
    return unique([...scoped.map((task) => task.branch), ...tasks.map((task) => task.branch), 'main'].filter((value): value is string => Boolean(value)))
  }, [repository, tasks])
  const suggestedLabels = useMemo(() => unique([...tasks.flatMap((task) => task.labels), ...DEFAULT_LABELS])
    .filter((label) => !includesLabel(labels, label)), [labels, tasks])
  const canCreate = Boolean(title.trim() && repository)

  function addLabel(value: string): void {
    const normalized = value.trim()
    if (normalized) setLabels((current) => includesLabel(current, normalized) ? current : [...current, normalized])
    setLabelDraft('')
    setAddingLabel(false)
  }

  function submit(): void {
    if (!canCreate) return
    rememberRepository(repository)
    onCreate({
      title: title.trim(),
      description: description.trim() || t('noDescription'),
      repository,
      labels: normalizeLabels(labels),
      assignee: assignee.trim() || undefined,
      branch: branch.trim() || undefined,
    })
  }

  return (
    <div className="ora-new-task-layer" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) onCancel() }}>
      <section className="ora-new-task" role="dialog" aria-modal="true" aria-labelledby="ora-new-task-title">
        <ModalHeader icon={<FilePlus2 size={18} />} title={t('newTask')} titleId="ora-new-task-title" onClose={onCancel} closeLabel={t('close')} />
        <div className="ora-new-task__body">
          <Input frameless autoFocus className="ora-new-task__title" maxLength={200} aria-label={t('title')} placeholder={t('titlePlaceholder')} value={title} onChange={(event) => setTitle(event.target.value)} />
          <Textarea className="ora-new-task__description" rows={4} aria-label={t('description')} placeholder={t('descriptionPlaceholder')} value={description} onChange={(event) => setDescription(event.target.value)} />

          <section className="ora-new-task__section">
            <h3>{t('details')}</h3>
            <div className="ora-new-task__fields">
              <SettingRow label={t('repository')}>
                <RepositoryField state={repositories} value={repository} onChange={setRepository} onOpenSettings={onOpenSettings} />
              </SettingRow>
              <SettingRow label={t('baseBranch')}>
                <Combobox value={branch} onValueChange={setBranch} ariaLabel={t('baseBranch')} placeholder={t('repositoryDefault')} disabled={!repository} options={branchCandidates.map((value) => ({ value, label: value }))} />
              </SettingRow>
              <SettingRow label={t('labels')}>
                <div className="ora-new-task__labels" aria-label={t('labels')}>
                  {labels.map((label) => (
                    <span className="ora-new-task__label" key={label}>
                      {label}
                      <button type="button" aria-label={`${t('removeLabel')} ${label}`} onClick={() => setLabels((current) => current.filter((value) => value !== label))}><X size={12} /></button>
                    </span>
                  ))}
                  {addingLabel ? (
                    <Input className="ora-new-task__label-input" autoFocus value={labelDraft} placeholder={t('labelPlaceholder')} onChange={(event) => setLabelDraft(event.target.value)} onBlur={() => addLabel(labelDraft)} onKeyDown={(event) => {
                      if (event.key === 'Enter') { event.preventDefault(); addLabel(labelDraft) }
                      if (event.key === 'Escape') { setLabelDraft(''); setAddingLabel(false) }
                    }} />
                  ) : <button type="button" className="ora-new-task__add-label" onClick={() => setAddingLabel(true)}><Plus size={12} />{t('addLabel')}</button>}
                </div>
              </SettingRow>
            </div>
            {suggestedLabels.length > 0 ? (
              <div className="ora-new-task__suggestions">
                <small>{t('suggestedLabels')}</small>
                {suggestedLabels.slice(0, 8).map((label) => <button type="button" key={label} onClick={() => addLabel(label)}>{label}</button>)}
              </div>
            ) : null}
          </section>

          <details className="ora-new-task__advanced">
            <summary><span>{t('advanced')}</span><ChevronRight size={14} strokeWidth={1.8} aria-hidden /></summary>
            <div className="ora-new-task__fields">
              <SettingRow label={t('assignee')}>
                <Combobox value={assignee} onValueChange={setAssignee} ariaLabel={t('assignee')} placeholder={t('optionalAssignee')} options={assignees.map((value) => ({ value, label: value }))} />
              </SettingRow>
            </div>
          </details>
        </div>
        <footer className="ora-new-task__footer"><Button variant="secondary" onClick={onCancel}>{t('cancel')}</Button><Button variant="primary" disabled={!canCreate} onClick={submit}>{t('createTask')}</Button></footer>
      </section>
    </div>
  )
}

function RepositoryField({ state, value, onChange, onOpenSettings }: {
  state: RepositoryState
  value: string
  onChange: (value: string) => void
  onOpenSettings: () => void
}): JSX.Element {
  const t = useOratorioLocalTaskT()
  if (state.status === 'loading') return <span className="ora-new-task__static">{t('loadingRepositories')}</span>
  if (state.status === 'error' || !state.options.length) {
    return (
      <span className="ora-new-task__missing">
        <span>{t(state.status === 'error' ? 'repositoriesFailed' : 'noRepositories')}</span>
        <Button variant="secondary" size="sm" onClick={onOpenSettings}><Settings size={13} aria-hidden />{t('openSettings')}</Button>
      </span>
    )
  }
  return (
    <Select
      appearance="frameless"
      adaptiveWidth={false}
      value={value}
      ariaLabel={t('repository')}
      onValueChange={onChange}
      options={[
        ...(value ? [] : [{ value: '', label: t('chooseRepository'), disabled: true }]),
        ...state.options,
      ]}
    />
  )
}

function SettingRow({ label, children }: { label: string; children: ReactNode }): JSX.Element {
  return <div className="ora-new-task__row"><span>{label}</span><div>{children}</div></div>
}

function unique(values: ReadonlyArray<string>): string[] {
  const seen = new Set<string>()
  return values.filter((value) => {
    const key = value.trim().toLocaleLowerCase()
    if (!key || seen.has(key)) return false
    seen.add(key)
    return true
  })
}

function includesLabel(labels: ReadonlyArray<string>, candidate: string): boolean {
  const key = candidate.trim().toLocaleLowerCase()
  return labels.some((label) => label.toLocaleLowerCase() === key)
}

export function normalizeLabels(labels: ReadonlyArray<string>): string[] {
  return unique(labels.map((label) => label.trim()))
}

function resolveInitialRepository(options: ReadonlyArray<LocalTaskRepositoryOption>): string {
  if (options.length === 1) return options[0].value
  const remembered = window.localStorage.getItem(LAST_REPOSITORY_KEY)?.toLocaleLowerCase()
  return options.find((option) => option.value.toLocaleLowerCase() === remembered)?.value ?? ''
}

function rememberRepository(repository: string): void {
  window.localStorage.setItem(LAST_REPOSITORY_KEY, repository)
}
