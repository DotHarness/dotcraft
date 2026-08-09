import { useMemo, useState } from 'react'
import { FilePlus2, Plus, X } from 'lucide-react'
import { Button } from '../ui/Button'
import { Combobox } from '../ui/Combobox'
import { Input, Textarea } from '../ui/Input'
import { ModalHeader } from '../ui/ModalHeader'
import { Select } from '../ui/Select'
import type { OratorioTask } from './oratorio-model'
import { useOratorioLocalTaskT } from './oratorio-local-task-i18n'
import './oratorio-new-local-task.css'

const LAST_REPOSITORY_KEY = 'dotcraft.oratorio.localTask.repository'
const DEFAULT_LABELS = ['bug', 'feature', 'docs', 'frontend', 'backend', 'security', 'test', 'chore', 'review', 'oratorio:auto']

export interface NewLocalTaskDraft {
  title: string
  description: string
  repository?: string
  labels: string[]
  assignee?: string
  branch?: string
}

export interface NewLocalTaskDialogProps {
  tasks: ReadonlyArray<OratorioTask>
  onCancel: () => void
  onCreate: (task: NewLocalTaskDraft) => void
}

export function NewLocalTaskDialog({ tasks, onCancel, onCreate }: NewLocalTaskDialogProps): JSX.Element {
  const t = useOratorioLocalTaskT()
  const repositories = useMemo(() => unique(tasks.filter((task) => task.provider !== 'local').map((task) => task.repository)), [tasks])
  const initialRepository = useMemo(() => resolveInitialRepository(repositories), [repositories])
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [repository, setRepository] = useState(initialRepository)
  const [labels, setLabels] = useState<string[]>([])
  const [assignee, setAssignee] = useState('')
  const [branch, setBranch] = useState('')
  const [addingLabel, setAddingLabel] = useState(false)
  const [labelDraft, setLabelDraft] = useState('')

  const assignees = useMemo(() => unique(tasks.map((task) => task.assignee).filter((value): value is string => Boolean(value))), [tasks])
  const branchCandidates = useMemo(() => {
    const scoped = repository ? tasks.filter((task) => task.repository === repository) : []
    return unique([...scoped.map((task) => task.branch), ...tasks.map((task) => task.branch), 'main'].filter((value): value is string => Boolean(value)))
  }, [repository, tasks])
  const suggestedLabels = useMemo(() => unique([...tasks.flatMap((task) => task.labels), ...DEFAULT_LABELS])
    .filter((label) => !includesLabel(labels, label)), [labels, tasks])

  function addLabel(value: string): void {
    const normalized = value.trim()
    if (normalized) setLabels((current) => includesLabel(current, normalized) ? current : [...current, normalized])
    setLabelDraft('')
    setAddingLabel(false)
  }

  function submit(): void {
    const trimmedTitle = title.trim()
    if (!trimmedTitle) return
    if (repository) rememberRepository(repository)
    onCreate({
      title: trimmedTitle,
      description: description.trim() || t('noDescription'),
      repository: repository || undefined,
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
          <label className="ora-new-task__field ora-new-task__field--wide">
            <span>{t('title')}</span>
            <Input autoFocus value={title} onChange={(event) => setTitle(event.target.value)} placeholder={t('titlePlaceholder')} />
          </label>
          <label className="ora-new-task__field ora-new-task__field--wide">
            <span>{t('description')}</span>
            <Textarea value={description} onChange={(event) => setDescription(event.target.value)} rows={4} placeholder={t('descriptionPlaceholder')} />
          </label>
          <label className="ora-new-task__field ora-new-task__field--wide">
            <span>{t('repository')}</span>
            <Select value={repository} onValueChange={setRepository} ariaLabel={t('repository')} adaptiveWidth={false} options={[
              { value: '', label: t('noRepository') },
              ...repositories.map((value) => ({ value, label: value })),
            ]} />
          </label>
          <div className="ora-new-task__field ora-new-task__field--wide">
            <span>{t('labels')}</span>
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
            {suggestedLabels.length > 0 ? (
              <div className="ora-new-task__suggestions">
                <small>{t('suggestedLabels')}</small>
                <span>{suggestedLabels.slice(0, 8).map((label) => <button type="button" key={label} onClick={() => addLabel(label)}>{label}</button>)}</span>
              </div>
            ) : null}
          </div>
          <label className="ora-new-task__field">
            <span>{t('assignee')}</span>
            <Combobox value={assignee} onValueChange={setAssignee} ariaLabel={t('assignee')} placeholder={t('optionalAssignee')} options={assignees.map((value) => ({ value, label: value }))} />
          </label>
          <label className="ora-new-task__field">
            <span>{t('baseBranch')}</span>
            <Combobox value={branch} onValueChange={setBranch} ariaLabel={t('baseBranch')} placeholder={t('repositoryDefault')} disabled={!repository} options={branchCandidates.map((value) => ({ value, label: value }))} />
          </label>
        </div>
        <footer className="ora-new-task__footer"><Button variant="secondary" onClick={onCancel}>{t('cancel')}</Button><Button variant="primary" disabled={!title.trim()} onClick={submit}>{t('createTask')}</Button></footer>
      </section>
    </div>
  )
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

function resolveInitialRepository(repositories: ReadonlyArray<string>): string {
  if (repositories.length === 1) return repositories[0]
  try {
    const remembered = window.localStorage.getItem(LAST_REPOSITORY_KEY)
    return repositories.find((repository) => repository.toLocaleLowerCase() === remembered?.toLocaleLowerCase()) ?? ''
  } catch {
    return ''
  }
}

function rememberRepository(repository: string): void {
  try { window.localStorage.setItem(LAST_REPOSITORY_KEY, repository) } catch { /* storage is optional */ }
}
