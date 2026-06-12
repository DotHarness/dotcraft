import { useEffect, useMemo, useState, type CSSProperties } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import {
  useAutomationsStore,
  type AutomationSchedule,
  type AutomationTemplate,
  type AutomationThreadBinding,
  type AutomationWorkspaceMode
} from '../../stores/automationsStore'
import { useThreadStore } from '../../stores/threadStore'
import { SchedulePicker } from './SchedulePicker'
import { ThreadPickerOverlay } from './ThreadPickerOverlay'
import { TemplateGalleryOverlay } from './TemplateGalleryOverlay'
import { PillSwitch } from '../ui/PillSwitch'
import { MenuOption, PillDropdown } from '../ui/PillDropdown'
import { PolicyDropdown, TargetDropdown, WorkspaceModeDropdown } from './TaskDropdowns'

type DialogTab = 'task' | 'template'

interface Props {
  onClose(): void
  /** Optional: pre-fill the dialog from a template (entry from the gallery strip). */
  initialTemplate?: AutomationTemplate
  /** Initial tab. Defaults to 'task'. Ignored when editingTemplate is provided. */
  initialTab?: DialogTab
  /** When present, the dialog opens in template-edit mode with the given template pre-filled. */
  editingTemplate?: AutomationTemplate
}

type TargetMode = AutomationWorkspaceMode | 'bound'

const DEFAULT_WORKFLOW_TEMPLATE = `---
max_rounds: 10
workspace: project
---

You are running a local automation task.

## Task

- **ID**: {{ task.id }}
- **Title**: {{ task.title }}

## Instructions

{{ task.description }}

When finished, call the **\`CompleteLocalTask\`** tool with a short summary.
`

export function NewTaskDialog({
  onClose,
  initialTemplate,
  initialTab,
  editingTemplate
}: Props): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const createTask = useAutomationsStore((s) => s.createTask)
  const saveTemplate = useAutomationsStore((s) => s.saveTemplate)
  const deleteTemplate = useAutomationsStore((s) => s.deleteTemplate)
  const templates = useAutomationsStore((s) => s.templates)
  const fetchTemplates = useAutomationsStore((s) => s.fetchTemplates)
  const threadList = useThreadStore((s) => s.threadList)

  const [tab, setTab] = useState<DialogTab>(() =>
    editingTemplate ? 'template' : (initialTab ?? 'task')
  )
  const isEditingTemplate = !!editingTemplate

  // --- Task tab state (unchanged fields) ---
  const [title, setTitle] = useState(initialTemplate?.defaultTitle ?? '')
  const [description, setDescription] = useState(initialTemplate?.defaultDescription ?? '')
  const [schedule, setSchedule] = useState<AutomationSchedule | null>(
    initialTemplate?.defaultSchedule ?? null
  )
  const [binding, setBinding] = useState<AutomationThreadBinding | null>(null)
  const [workspaceMode, setWorkspaceMode] = useState<AutomationWorkspaceMode>(
    normalizeWorkspaceMode(initialTemplate?.defaultWorkspaceMode)
  )
  const [approvalPolicy, setApprovalPolicy] = useState<'workspaceScope' | 'fullAuto'>(
    (initialTemplate?.defaultApprovalPolicy as 'workspaceScope' | 'fullAuto' | undefined) ??
      'workspaceScope'
  )
  const [showThreadPicker, setShowThreadPicker] = useState(false)
  const [showTemplates, setShowTemplates] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [templateId, setTemplateId] = useState<string | undefined>(initialTemplate?.id)
  const [workflowTemplate, setWorkflowTemplate] = useState<string | undefined>(
    initialTemplate?.workflowMarkdown
  )

  // --- Template tab state ---
  const [tplTitle, setTplTitle] = useState(editingTemplate?.title ?? '')
  const [tplDescription, setTplDescription] = useState(editingTemplate?.description ?? '')
  const [tplIcon, setTplIcon] = useState(editingTemplate?.icon ?? '')
  const [tplCategory, setTplCategory] = useState(editingTemplate?.category ?? '')
  const [tplWorkflow, setTplWorkflow] = useState(
    editingTemplate?.workflowMarkdown ?? DEFAULT_WORKFLOW_TEMPLATE
  )
  const [tplDefaultTitle, setTplDefaultTitle] = useState(editingTemplate?.defaultTitle ?? '')
  const [tplDefaultDescription, setTplDefaultDescription] = useState(
    editingTemplate?.defaultDescription ?? ''
  )
  const [tplSchedule, setTplSchedule] = useState<AutomationSchedule | null>(
    editingTemplate?.defaultSchedule ?? null
  )
  const [tplWorkspaceMode, setTplWorkspaceMode] = useState<AutomationWorkspaceMode>(
    normalizeWorkspaceMode(editingTemplate?.defaultWorkspaceMode)
  )
  const [tplApprovalPolicy, setTplApprovalPolicy] = useState<'workspaceScope' | 'fullAuto'>(
    (editingTemplate?.defaultApprovalPolicy as 'workspaceScope' | 'fullAuto' | undefined) ??
      'workspaceScope'
  )
  const [tplNeedsThreadBinding, setTplNeedsThreadBinding] = useState<boolean>(
    editingTemplate?.needsThreadBinding ?? false
  )
  const [tplPrefillFromId, setTplPrefillFromId] = useState<string>('')
  const [tplDeleteConfirm, setTplDeleteConfirm] = useState(false)
  const [tplDeleting, setTplDeleting] = useState(false)

  useEffect(() => {
    void fetchTemplates(locale)
  }, [fetchTemplates, locale])

  // When a template suggests thread binding, pop the thread picker after the dialog mounts
  // so the user sees the intent immediately (without forcing — they can still cancel).
  useEffect(() => {
    if (tab !== 'task') return
    if (initialTemplate?.needsThreadBinding) setShowThreadPicker(true)
  }, [initialTemplate, tab])

  const targetMode: TargetMode = binding ? 'bound' : workspaceMode

  const boundThreadName = useMemo(() => {
    if (!binding) return null
    const match = threadList.find((t) => t.id === binding.threadId)
    return match?.displayName ?? binding.threadId
  }, [binding, threadList])

  const canSubmitTask = title.trim().length > 0 && description.trim().length > 0 && !submitting
  const canSubmitTemplate =
    tplTitle.trim().length > 0 && tplWorkflow.trim().length > 0 && !submitting

  const categoryOptions = useMemo(() => {
    const set = new Set<string>()
    for (const tpl of templates) {
      if (tpl.category) set.add(tpl.category)
    }
    return Array.from(set).sort()
  }, [templates])

  function applyTemplate(tpl: AutomationTemplate): void {
    setTemplateId(tpl.id)
    setWorkflowTemplate(tpl.workflowMarkdown)
    if (tpl.defaultTitle) setTitle(tpl.defaultTitle)
    if (tpl.defaultDescription) setDescription(tpl.defaultDescription)
    if (tpl.defaultSchedule !== undefined) setSchedule(tpl.defaultSchedule ?? null)
    if (
      tpl.defaultWorkspaceMode === 'project' ||
      tpl.defaultWorkspaceMode === 'worktree' ||
      tpl.defaultWorkspaceMode === 'isolated'
    )
      setWorkspaceMode(normalizeWorkspaceMode(tpl.defaultWorkspaceMode))
    if (tpl.defaultApprovalPolicy === 'workspaceScope' || tpl.defaultApprovalPolicy === 'fullAuto')
      setApprovalPolicy(tpl.defaultApprovalPolicy)
    if (tpl.needsThreadBinding && !binding) setShowThreadPicker(true)
  }

  function prefillTemplateFrom(tpl: AutomationTemplate): void {
    setTplTitle(tpl.title)
    setTplDescription(tpl.description ?? '')
    setTplIcon(tpl.icon ?? '')
    setTplCategory(tpl.category ?? '')
    setTplWorkflow(tpl.workflowMarkdown)
    setTplDefaultTitle(tpl.defaultTitle ?? '')
    setTplDefaultDescription(tpl.defaultDescription ?? '')
    setTplSchedule(tpl.defaultSchedule ?? null)
    if (
      tpl.defaultWorkspaceMode === 'project' ||
      tpl.defaultWorkspaceMode === 'worktree' ||
      tpl.defaultWorkspaceMode === 'isolated'
    )
      setTplWorkspaceMode(normalizeWorkspaceMode(tpl.defaultWorkspaceMode))
    if (tpl.defaultApprovalPolicy === 'workspaceScope' || tpl.defaultApprovalPolicy === 'fullAuto')
      setTplApprovalPolicy(tpl.defaultApprovalPolicy)
    setTplNeedsThreadBinding(tpl.needsThreadBinding ?? false)
  }

  async function handleSubmitTask(): Promise<void> {
    if (!canSubmitTask) return
    setSubmitting(true)
    setError(null)
    try {
      await createTask({
        title: title.trim(),
        description: description.trim(),
        approvalPolicy,
        workspaceMode,
        schedule: schedule && schedule.kind !== 'once' ? schedule : null,
        threadBinding: binding,
        templateId,
        workflowTemplate
      })
      onClose()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSubmitting(false)
    }
  }

  async function handleSubmitTemplate(): Promise<void> {
    if (!canSubmitTemplate) return
    setSubmitting(true)
    setError(null)
    try {
      await saveTemplate({
        id: editingTemplate?.id,
        title: tplTitle.trim(),
        description: tplDescription.trim() || null,
        icon: tplIcon.trim() || null,
        category: tplCategory.trim() || null,
        workflowMarkdown: tplWorkflow,
        defaultSchedule:
          tplSchedule && tplSchedule.kind !== 'once' ? tplSchedule : null,
        defaultWorkspaceMode: tplWorkspaceMode,
        defaultApprovalPolicy: tplApprovalPolicy,
        needsThreadBinding: tplNeedsThreadBinding,
        defaultTitle: tplDefaultTitle.trim() || null,
        defaultDescription: tplDefaultDescription.trim() || null
      })
      onClose()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSubmitting(false)
    }
  }

  async function handleDeleteTemplate(): Promise<void> {
    if (!editingTemplate) return
    setTplDeleting(true)
    setError(null)
    try {
      await deleteTemplate(editingTemplate.id)
      onClose()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setTplDeleting(false)
      setTplDeleteConfirm(false)
    }
  }

  const dialogTitle = isEditingTemplate
    ? t('auto.newTemplate.editTitle')
    : tab === 'template'
      ? t('auto.newTemplate.title')
      : t('auto.newTask.title')

  return (
    <>
      <div
        style={{
          position: 'fixed',
          inset: 0,
          zIndex: 1000,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          backgroundColor: 'rgba(0,0,0,0.5)'
        }}
        onClick={onClose}
      >
        <div
          onClick={(e) => e.stopPropagation()}
          style={{
            width: tab === 'template' ? '680px' : '580px',
            maxWidth: 'calc(100vw - 48px)',
            maxHeight: '85vh',
            backgroundColor: 'var(--bg-primary)',
            borderRadius: '12px',
            border: '1px solid var(--border-default)',
            display: 'flex',
            flexDirection: 'column',
            overflow: 'hidden',
            boxShadow: '0 8px 32px rgba(0,0,0,0.3)'
          }}
        >
          <div
            style={{
              padding: '14px 20px',
              borderBottom: '1px solid var(--border-default)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: '8px'
            }}
          >
            <div style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)' }}>
              {dialogTitle}
            </div>
            {tab === 'task' && !isEditingTemplate && (
              <button
                type="button"
                onClick={() => setShowTemplates(true)}
                style={{
                  padding: '5px 12px',
                  borderRadius: '6px',
                  border: '1px solid var(--border-default)',
                  backgroundColor: 'transparent',
                  color: 'var(--text-secondary)',
                  fontSize: '12px',
                  fontWeight: 500,
                  cursor: 'pointer'
                }}
              >
                {t('auto.newTask.useTemplate')}
              </button>
            )}
          </div>

          {!isEditingTemplate && (
            <div
              role="tablist"
              aria-label={t('auto.newTask.tabAria')}
              style={{
                display: 'flex',
                gap: '4px',
                padding: '8px 20px 0',
                borderBottom: '1px solid var(--border-default)'
              }}
            >
              <TabButton
                active={tab === 'task'}
                onClick={() => setTab('task')}
                label={t('auto.newTask.tab.task')}
              />
              <TabButton
                active={tab === 'template'}
                onClick={() => setTab('template')}
                label={t('auto.newTask.tab.template')}
              />
            </div>
          )}

          <div
            style={{
              padding: '16px 20px',
              display: 'flex',
              flexDirection: 'column',
              gap: '12px',
              overflow: 'auto'
            }}
          >
            {tab === 'task' ? (
              <>
                <input
                  type="text"
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                  maxLength={200}
                  placeholder={t('auto.newTask.namePlaceholder')}
                  autoFocus
                  style={{
                    padding: '10px 12px',
                    borderRadius: '8px',
                    border: '1px solid var(--border-default)',
                    backgroundColor: 'var(--bg-secondary)',
                    color: 'var(--text-primary)',
                    fontSize: '14px',
                    fontWeight: 500,
                    outline: 'none'
                  }}
                />

                <textarea
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  rows={10}
                  placeholder={t('auto.newTask.promptPlaceholder')}
                  style={{
                    padding: '10px 12px',
                    borderRadius: '8px',
                    border: '1px solid var(--border-default)',
                    backgroundColor: 'var(--bg-secondary)',
                    color: 'var(--text-primary)',
                    fontSize: '13px',
                    resize: 'vertical',
                    outline: 'none',
                    fontFamily: 'inherit',
                    minHeight: '160px'
                  }}
                />

                <div
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    flexWrap: 'wrap',
                    gap: '8px',
                    padding: '8px 0 0'
                  }}
                >
                  <TargetDropdown
                    mode={targetMode}
                    boundName={boundThreadName}
                    onProject={() => {
                      setBinding(null)
                      setWorkspaceMode('project')
                    }}
                    onIsolated={() => {
                      setBinding(null)
                      setWorkspaceMode('worktree')
                    }}
                    onBind={() => setShowThreadPicker(true)}
                    onUnbind={() => setBinding(null)}
                  />
                  <SchedulePicker value={schedule} onChange={setSchedule} />
                  <PolicyDropdown value={approvalPolicy} onChange={setApprovalPolicy} />
                </div>
              </>
            ) : (
              <>
                {/* Prefill dropdown */}
                {templates.length > 0 && (
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: '8px',
                      fontSize: '12px'
                    }}
                  >
                    <label style={{ color: 'var(--text-secondary)' }}>
                      {t('auto.newTemplate.prefillFrom')}
                    </label>
                    <PillDropdown
                      ariaLabel={t('auto.newTemplate.prefillFrom')}
                      label={
                        templates.find((x) => x.id === tplPrefillFromId)?.title ??
                        t('auto.newTemplate.prefillNone')
                      }
                      panelMinWidth={260}
                    >
                      {(close) => (
                        <>
                          <MenuOption
                            selected={!tplPrefillFromId}
                            onClick={() => {
                              setTplPrefillFromId('')
                              close()
                            }}
                          >
                            {t('auto.newTemplate.prefillNone')}
                          </MenuOption>
                          {templates.map((tpl) => (
                            <MenuOption
                              key={tpl.id}
                              selected={tplPrefillFromId === tpl.id}
                              description={tpl.description ?? undefined}
                              onClick={() => {
                                setTplPrefillFromId(tpl.id)
                                prefillTemplateFrom(tpl)
                                close()
                              }}
                            >
                              {tpl.title + (tpl.isUser ? ' ★' : '')}
                            </MenuOption>
                          ))}
                        </>
                      )}
                    </PillDropdown>
                  </div>
                )}

                <div style={{ display: 'flex', gap: '8px' }}>
                  <input
                    type="text"
                    value={tplIcon}
                    onChange={(e) => setTplIcon(e.target.value.slice(0, 4))}
                    maxLength={4}
                    placeholder="✦"
                    aria-label={t('auto.newTemplate.field.icon')}
                    style={{
                      width: '56px',
                      padding: '10px 12px',
                      borderRadius: '8px',
                      border: '1px solid var(--border-default)',
                      backgroundColor: 'var(--bg-secondary)',
                      color: 'var(--text-primary)',
                      fontSize: '16px',
                      textAlign: 'center',
                      outline: 'none'
                    }}
                  />
                  <input
                    type="text"
                    value={tplTitle}
                    onChange={(e) => setTplTitle(e.target.value)}
                    maxLength={200}
                    placeholder={t('auto.newTemplate.field.titlePlaceholder')}
                    autoFocus={!isEditingTemplate}
                    style={{
                      flex: 1,
                      padding: '10px 12px',
                      borderRadius: '8px',
                      border: '1px solid var(--border-default)',
                      backgroundColor: 'var(--bg-secondary)',
                      color: 'var(--text-primary)',
                      fontSize: '14px',
                      fontWeight: 500,
                      outline: 'none'
                    }}
                  />
                </div>

                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                  <label style={advancedLabelStyle}>{t('auto.newTemplate.field.category')}</label>
                  <input
                    type="text"
                    list="automation-template-categories"
                    value={tplCategory}
                    onChange={(e) => setTplCategory(e.target.value)}
                    maxLength={48}
                    placeholder={t('auto.newTemplate.field.categoryPlaceholder')}
                    style={{
                      padding: '8px 10px',
                      borderRadius: '6px',
                      border: '1px solid var(--border-default)',
                      backgroundColor: 'var(--bg-secondary)',
                      color: 'var(--text-primary)',
                      fontSize: '12px',
                      outline: 'none'
                    }}
                  />
                  <datalist id="automation-template-categories">
                    {categoryOptions.map((c) => (
                      <option key={c} value={c} />
                    ))}
                  </datalist>
                </div>

                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                  <label style={advancedLabelStyle}>
                    {t('auto.newTemplate.field.description')}
                  </label>
                  <textarea
                    value={tplDescription}
                    onChange={(e) => setTplDescription(e.target.value)}
                    rows={2}
                    placeholder={t('auto.newTemplate.field.descriptionPlaceholder')}
                    style={{
                      padding: '8px 10px',
                      borderRadius: '6px',
                      border: '1px solid var(--border-default)',
                      backgroundColor: 'var(--bg-secondary)',
                      color: 'var(--text-primary)',
                      fontSize: '12px',
                      resize: 'vertical',
                      outline: 'none',
                      fontFamily: 'inherit',
                      minHeight: '48px'
                    }}
                  />
                </div>

                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                  <label style={advancedLabelStyle}>
                    {t('auto.newTemplate.field.workflow')}
                  </label>
                  <textarea
                    value={tplWorkflow}
                    onChange={(e) => setTplWorkflow(e.target.value)}
                    rows={12}
                    placeholder={t('auto.newTemplate.field.workflowPlaceholder')}
                    spellCheck={false}
                    style={{
                      padding: '10px 12px',
                      borderRadius: '8px',
                      border: '1px solid var(--border-default)',
                      backgroundColor: 'var(--bg-secondary)',
                      color: 'var(--text-primary)',
                      fontSize: '12px',
                      resize: 'vertical',
                      outline: 'none',
                      fontFamily: 'var(--font-mono)',
                      minHeight: '200px'
                    }}
                  />
                </div>

                <div
                  style={{
                    borderTop: '1px solid var(--border-default)',
                    marginTop: '4px',
                    paddingTop: '14px',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: '12px'
                  }}
                >
                  <div>
                    <div style={advancedLabelStyle}>{t('auto.newTemplate.showDefaults')}</div>
                    <div
                      style={{ fontSize: '11px', color: 'var(--text-tertiary)', marginTop: '3px' }}
                    >
                      {t('auto.newTemplate.defaultsHint')}
                    </div>
                  </div>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                    <label style={advancedLabelStyle}>
                      {t('auto.newTemplate.field.defaultTitle')}
                    </label>
                    <input
                      type="text"
                      value={tplDefaultTitle}
                      onChange={(e) => setTplDefaultTitle(e.target.value)}
                      maxLength={200}
                      style={{
                        padding: '8px 10px',
                        borderRadius: '6px',
                        border: '1px solid var(--border-default)',
                        backgroundColor: 'var(--bg-secondary)',
                        color: 'var(--text-primary)',
                        fontSize: '12px',
                        outline: 'none'
                      }}
                    />
                  </div>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                    <label style={advancedLabelStyle}>
                      {t('auto.newTemplate.field.defaultDescription')}
                    </label>
                    <textarea
                      value={tplDefaultDescription}
                      onChange={(e) => setTplDefaultDescription(e.target.value)}
                      rows={3}
                      style={{
                        padding: '8px 10px',
                        borderRadius: '6px',
                        border: '1px solid var(--border-default)',
                        backgroundColor: 'var(--bg-secondary)',
                        color: 'var(--text-primary)',
                        fontSize: '12px',
                        resize: 'vertical',
                        outline: 'none',
                        fontFamily: 'inherit'
                      }}
                    />
                  </div>
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      flexWrap: 'wrap',
                      gap: '8px'
                    }}
                  >
                    <SchedulePicker value={tplSchedule} onChange={setTplSchedule} />
                    <WorkspaceModeDropdown
                      value={tplWorkspaceMode}
                      onChange={setTplWorkspaceMode}
                    />
                    <PolicyDropdown value={tplApprovalPolicy} onChange={setTplApprovalPolicy} />
                  </div>
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      gap: '12px'
                    }}
                  >
                    <span style={{ fontSize: '12px', color: 'var(--text-primary)' }}>
                      {t('auto.newTemplate.field.needsThreadBinding')}
                    </span>
                    <PillSwitch
                      checked={tplNeedsThreadBinding}
                      onChange={setTplNeedsThreadBinding}
                      aria-label={t('auto.newTemplate.field.needsThreadBinding')}
                      size="sm"
                    />
                  </div>
                </div>
              </>
            )}

            {error && (
              <div
                style={{
                  padding: '8px 10px',
                  borderRadius: '6px',
                  backgroundColor: 'color-mix(in srgb, var(--error) 10%, transparent)',
                  color: 'var(--error)',
                  fontSize: '12px'
                }}
              >
                {error}
              </div>
            )}
          </div>

          <div
            style={{
              padding: '12px 20px',
              borderTop: '1px solid var(--border-default)',
              display: 'flex',
              justifyContent: 'space-between',
              gap: '8px'
            }}
          >
            <div style={{ display: 'flex', gap: '8px' }}>
              {isEditingTemplate && !tplDeleteConfirm && (
                <button
                  type="button"
                  onClick={() => setTplDeleteConfirm(true)}
                  style={{
                    padding: '6px 14px',
                    borderRadius: '6px',
                    border: '1px solid color-mix(in srgb, var(--error) 40%, transparent)',
                    backgroundColor: 'transparent',
                    color: 'var(--error)',
                    fontSize: '13px',
                    cursor: 'pointer'
                  }}
                >
                  {t('auto.newTemplate.delete')}
                </button>
              )}
              {isEditingTemplate && tplDeleteConfirm && (
                <>
                  <span
                    style={{
                      alignSelf: 'center',
                      fontSize: '12px',
                      color: 'var(--text-secondary)'
                    }}
                  >
                    {t('auto.newTemplate.deleteConfirm')}
                  </span>
                  <button
                    type="button"
                    onClick={() => setTplDeleteConfirm(false)}
                    disabled={tplDeleting}
                    style={{
                      padding: '6px 12px',
                      borderRadius: '6px',
                      border: '1px solid var(--border-default)',
                      backgroundColor: 'transparent',
                      color: 'var(--text-secondary)',
                      fontSize: '12px',
                      cursor: tplDeleting ? 'default' : 'pointer'
                    }}
                  >
                    {t('common.cancel')}
                  </button>
                  <button
                    type="button"
                    onClick={() => void handleDeleteTemplate()}
                    disabled={tplDeleting}
                    style={{
                      padding: '6px 12px',
                      borderRadius: '6px',
                      border: 'none',
                      backgroundColor: 'var(--error)',
                      color: '#fff',
                      fontSize: '12px',
                      fontWeight: 600,
                      cursor: tplDeleting ? 'default' : 'pointer',
                      opacity: tplDeleting ? 0.7 : 1
                    }}
                  >
                    {tplDeleting
                      ? t('auto.newTemplate.deleting')
                      : t('auto.newTemplate.deleteConfirmBtn')}
                  </button>
                </>
              )}
            </div>
            <div style={{ display: 'flex', gap: '8px' }}>
              <button
                type="button"
                onClick={onClose}
                style={{
                  padding: '6px 14px',
                  borderRadius: '6px',
                  border: '1px solid var(--border-default)',
                  backgroundColor: 'transparent',
                  color: 'var(--text-secondary)',
                  fontSize: '13px',
                  cursor: 'pointer'
                }}
              >
                {t('common.cancel')}
              </button>
              {tab === 'task' ? (
                <button
                  type="button"
                  onClick={() => void handleSubmitTask()}
                  disabled={!canSubmitTask}
                  style={primaryBtnStyle(canSubmitTask, submitting)}
                >
                  {submitting ? t('auto.newTask.creating') : t('auto.newTask.create')}
                </button>
              ) : (
                <button
                  type="button"
                  onClick={() => void handleSubmitTemplate()}
                  disabled={!canSubmitTemplate}
                  style={primaryBtnStyle(canSubmitTemplate, submitting)}
                >
                  {submitting
                    ? t('auto.newTemplate.saving')
                    : isEditingTemplate
                      ? t('auto.newTemplate.saveChanges')
                      : t('auto.newTemplate.save')}
                </button>
              )}
            </div>
          </div>
        </div>
      </div>

      {showThreadPicker && (
        <ThreadPickerOverlay
          onClose={() => setShowThreadPicker(false)}
          onSelect={(th) =>
            setBinding({ threadId: th.id, mode: 'run-in-thread' })
          }
        />
      )}

      {showTemplates && (
        <TemplateGalleryOverlay
          onClose={() => setShowTemplates(false)}
          onSelect={(tpl) => {
            applyTemplate(tpl)
            setShowTemplates(false)
          }}
        />
      )}
    </>
  )
}

function normalizeWorkspaceMode(value: unknown): AutomationWorkspaceMode {
  return value === 'worktree' || value === 'isolated' ? 'worktree' : 'project'
}

const advancedLabelStyle: CSSProperties = {
  fontSize: '11px',
  fontWeight: 500,
  color: 'var(--text-secondary)',
  textTransform: 'uppercase',
  letterSpacing: '0.04em'
}

function primaryBtnStyle(enabled: boolean, busy: boolean): CSSProperties {
  return {
    padding: '6px 14px',
    borderRadius: '6px',
    border: 'none',
    backgroundColor: enabled ? 'var(--text-primary)' : 'var(--bg-tertiary)',
    color: enabled ? 'var(--bg-primary)' : 'var(--text-tertiary)',
    fontSize: '13px',
    fontWeight: 600,
    cursor: enabled ? 'pointer' : 'default',
    opacity: busy ? 0.7 : 1
  }
}

function TabButton({
  active,
  onClick,
  label
}: {
  active: boolean
  onClick(): void
  label: string
}): JSX.Element {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      onClick={onClick}
      style={{
        padding: '8px 14px',
        borderRadius: '6px 6px 0 0',
        border: 'none',
        borderBottom: active ? '2px solid var(--accent)' : '2px solid transparent',
        marginBottom: '-1px',
        backgroundColor: 'transparent',
        color: active ? 'var(--accent)' : 'var(--text-secondary)',
        fontSize: '12px',
        fontWeight: 600,
        cursor: 'pointer'
      }}
    >
      {label}
    </button>
  )
}
