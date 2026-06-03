import { useEffect, useMemo, useState, type CSSProperties } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import {
  useAutomationsStore,
  type AutomationSchedule,
  type AutomationTemplate,
  type AutomationThreadBinding
} from '../../stores/automationsStore'
import { useThreadStore } from '../../stores/threadStore'
import { SchedulePicker } from './SchedulePicker'
import { ThreadPickerOverlay } from './ThreadPickerOverlay'
import { TemplateGalleryOverlay } from './TemplateGalleryOverlay'
import { PillSwitch } from '../ui/PillSwitch'
import { ActionTooltip } from '../ui/ActionTooltip'

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

type TargetMode = 'project' | 'isolated' | 'bound'

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
  const [workspaceMode, setWorkspaceMode] = useState<'project' | 'isolated'>(
    (initialTemplate?.defaultWorkspaceMode as 'project' | 'isolated' | undefined) ?? 'project'
  )
  const [approvalPolicy, setApprovalPolicy] = useState<'workspaceScope' | 'fullAuto'>(
    (initialTemplate?.defaultApprovalPolicy as 'workspaceScope' | 'fullAuto' | undefined) ??
      'workspaceScope'
  )
  const [showAdvanced, setShowAdvanced] = useState(false)
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
  const [tplWorkspaceMode, setTplWorkspaceMode] = useState<'project' | 'isolated'>(
    (editingTemplate?.defaultWorkspaceMode as 'project' | 'isolated' | undefined) ?? 'project'
  )
  const [tplApprovalPolicy, setTplApprovalPolicy] = useState<'workspaceScope' | 'fullAuto'>(
    (editingTemplate?.defaultApprovalPolicy as 'workspaceScope' | 'fullAuto' | undefined) ??
      'workspaceScope'
  )
  const [tplNeedsThreadBinding, setTplNeedsThreadBinding] = useState<boolean>(
    editingTemplate?.needsThreadBinding ?? false
  )
  const [tplShowAdvanced, setTplShowAdvanced] = useState(false)
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
    if (tpl.defaultWorkspaceMode === 'project' || tpl.defaultWorkspaceMode === 'isolated')
      setWorkspaceMode(tpl.defaultWorkspaceMode)
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
    if (tpl.defaultWorkspaceMode === 'project' || tpl.defaultWorkspaceMode === 'isolated')
      setTplWorkspaceMode(tpl.defaultWorkspaceMode)
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
            width: '580px',
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
                📚 {t('auto.newTask.useTemplate')}
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
                  <TargetPill
                    mode={targetMode}
                    boundName={boundThreadName}
                    onIsolated={() => {
                      setBinding(null)
                      setWorkspaceMode('isolated')
                    }}
                    onProject={() => {
                      setBinding(null)
                      setWorkspaceMode('project')
                    }}
                    onBind={() => setShowThreadPicker(true)}
                    onUnbind={() => setBinding(null)}
                  />
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <SchedulePicker value={schedule} onChange={setSchedule} />
                  </div>
                </div>

                <button
                  type="button"
                  onClick={() => setShowAdvanced((v) => !v)}
                  style={{
                    alignSelf: 'flex-start',
                    fontSize: '12px',
                    color: 'var(--text-secondary)',
                    background: 'transparent',
                    border: 'none',
                    cursor: 'pointer',
                    padding: 0,
                    textDecoration: 'underline dotted'
                  }}
                >
                  {showAdvanced ? t('auto.newTask.hideDetails') : t('auto.newTask.advanced')} ▾
                </button>

                {showAdvanced && (
                  <div
                    style={{
                      display: 'flex',
                      flexDirection: 'column',
                      gap: '10px',
                      padding: '10px 12px',
                      borderRadius: '8px',
                      border: '1px solid var(--border-default)',
                      backgroundColor: 'var(--bg-secondary)'
                    }}
                  >
                    {targetMode !== 'bound' && (
                      <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                        <label style={advancedLabelStyle}>
                          {t('auto.newTask.agentWorkspaceLabel')}
                        </label>
                        <select
                          value={workspaceMode}
                          onChange={(e) =>
                            setWorkspaceMode(e.target.value as 'project' | 'isolated')
                          }
                          style={selectStyle}
                        >
                          <option value="project">{t('auto.newTask.workspaceProject')}</option>
                          <option value="isolated">{t('auto.newTask.workspaceIsolated')}</option>
                        </select>
                      </div>
                    )}
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                      <label style={advancedLabelStyle}>{t('auto.newTask.toolPolicyLabel')}</label>
                      <select
                        value={approvalPolicy}
                        onChange={(e) =>
                          setApprovalPolicy(e.target.value as 'workspaceScope' | 'fullAuto')
                        }
                        style={selectStyle}
                      >
                        <option value="workspaceScope">
                          {t('auto.newTask.policyWorkspace')}
                        </option>
                        <option value="fullAuto">{t('auto.newTask.policyFullAuto')}</option>
                      </select>
                    </div>
                  </div>
                )}
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
                    <select
                      value={tplPrefillFromId}
                      onChange={(e) => {
                        const id = e.target.value
                        setTplPrefillFromId(id)
                        if (!id) return
                        const src = templates.find((x) => x.id === id)
                        if (src) prefillTemplateFrom(src)
                      }}
                      style={{ ...selectStyle, flex: 1 }}
                    >
                      <option value="">{t('auto.newTemplate.prefillNone')}</option>
                      {templates.map((tpl) => (
                        <option key={tpl.id} value={tpl.id}>
                          {(tpl.icon ? tpl.icon + ' ' : '') + tpl.title}
                          {tpl.isUser ? ' ★' : ''}
                        </option>
                      ))}
                    </select>
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

                <button
                  type="button"
                  onClick={() => setTplShowAdvanced((v) => !v)}
                  style={{
                    alignSelf: 'flex-start',
                    fontSize: '12px',
                    color: 'var(--text-secondary)',
                    background: 'transparent',
                    border: 'none',
                    cursor: 'pointer',
                    padding: 0,
                    textDecoration: 'underline dotted'
                  }}
                >
                  {tplShowAdvanced
                    ? t('auto.newTemplate.hideDefaults')
                    : t('auto.newTemplate.showDefaults')}{' '}
                  ▾
                </button>

                {tplShowAdvanced && (
                  <div
                    style={{
                      display: 'flex',
                      flexDirection: 'column',
                      gap: '10px',
                      padding: '10px 12px',
                      borderRadius: '8px',
                      border: '1px solid var(--border-default)',
                      backgroundColor: 'var(--bg-secondary)'
                    }}
                  >
                    <div style={{ fontSize: '11px', color: 'var(--text-tertiary)' }}>
                      {t('auto.newTemplate.defaultsHint')}
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
                          backgroundColor: 'var(--bg-primary)',
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
                          backgroundColor: 'var(--bg-primary)',
                          color: 'var(--text-primary)',
                          fontSize: '12px',
                          resize: 'vertical',
                          outline: 'none',
                          fontFamily: 'inherit'
                        }}
                      />
                    </div>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                      <label style={advancedLabelStyle}>
                        {t('auto.newTemplate.field.defaultSchedule')}
                      </label>
                      <SchedulePicker value={tplSchedule} onChange={setTplSchedule} />
                    </div>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                      <label style={advancedLabelStyle}>
                        {t('auto.newTask.agentWorkspaceLabel')}
                      </label>
                      <select
                        value={tplWorkspaceMode}
                        onChange={(e) =>
                          setTplWorkspaceMode(e.target.value as 'project' | 'isolated')
                        }
                        style={selectStyle}
                      >
                        <option value="project">{t('auto.newTask.workspaceProject')}</option>
                        <option value="isolated">{t('auto.newTask.workspaceIsolated')}</option>
                      </select>
                    </div>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                      <label style={advancedLabelStyle}>
                        {t('auto.newTask.toolPolicyLabel')}
                      </label>
                      <select
                        value={tplApprovalPolicy}
                        onChange={(e) =>
                          setTplApprovalPolicy(e.target.value as 'workspaceScope' | 'fullAuto')
                        }
                        style={selectStyle}
                      >
                        <option value="workspaceScope">
                          {t('auto.newTask.policyWorkspace')}
                        </option>
                        <option value="fullAuto">{t('auto.newTask.policyFullAuto')}</option>
                      </select>
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
                )}
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

const selectStyle: CSSProperties = {
  padding: '7px 10px',
  borderRadius: '6px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-primary)',
  color: 'var(--text-primary)',
  fontSize: '12px',
  outline: 'none',
  width: '100%',
  cursor: 'pointer'
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

function TargetPill({
  mode,
  boundName,
  onIsolated,
  onProject,
  onBind,
  onUnbind
}: {
  mode: TargetMode
  boundName: string | null
  onIsolated(): void
  onProject(): void
  onBind(): void
  onUnbind(): void
}): JSX.Element {
  const t = useT()
  const [open, setOpen] = useState(false)

  const label =
    mode === 'bound'
      ? `💬 ${boundName ?? ''}`
      : mode === 'isolated'
        ? `📦 ${t('auto.newTask.targetIsolated')}`
        : `📁 ${t('auto.newTask.targetProject')}`

  return (
    <div style={{ position: 'relative' }}>
      <ActionTooltip label={label}>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        style={{
          padding: '5px 10px',
          borderRadius: '999px',
          border: mode === 'bound' ? '1px solid var(--accent)' : '1px solid var(--border-default)',
          backgroundColor:
            mode === 'bound'
              ? 'color-mix(in srgb, var(--accent) 12%, transparent)'
              : 'transparent',
          color: mode === 'bound' ? 'var(--accent)' : 'var(--text-secondary)',
          fontSize: '12px',
          fontWeight: 500,
          cursor: 'pointer',
          maxWidth: '220px',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap'
        }}
      >
        {label} ▾
      </button>
      </ActionTooltip>
      {open && (
        <div
          style={{
            position: 'absolute',
            top: '110%',
            left: 0,
            zIndex: 20,
            minWidth: '200px',
            padding: '4px',
            border: '1px solid var(--border-default)',
            borderRadius: '8px',
            backgroundColor: 'var(--bg-primary)',
            boxShadow: '0 6px 18px rgba(0,0,0,0.25)',
            display: 'flex',
            flexDirection: 'column'
          }}
          onMouseLeave={() => setOpen(false)}
        >
          <MenuItem
            active={mode === 'project'}
            onClick={() => {
              onProject()
              setOpen(false)
            }}
          >
            📁 {t('auto.newTask.targetProject')}
          </MenuItem>
          <MenuItem
            active={mode === 'isolated'}
            onClick={() => {
              onIsolated()
              setOpen(false)
            }}
          >
            📦 {t('auto.newTask.targetIsolated')}
          </MenuItem>
          <MenuItem
            onClick={() => {
              onBind()
              setOpen(false)
            }}
          >
            💬 {t('auto.newTask.targetBindThread')}
          </MenuItem>
          {mode === 'bound' && (
            <MenuItem
              onClick={() => {
                onUnbind()
                setOpen(false)
              }}
            >
              ✕ {t('auto.newTask.unbind')}
            </MenuItem>
          )}
        </div>
      )}
    </div>
  )
}

function MenuItem({
  active,
  onClick,
  children
}: {
  active?: boolean
  onClick(): void
  children: React.ReactNode
}): JSX.Element {
  return (
    <button
      type="button"
      onClick={onClick}
      style={{
        padding: '6px 10px',
        border: 'none',
        borderRadius: '6px',
        backgroundColor: active ? 'var(--bg-tertiary)' : 'transparent',
        color: 'var(--text-primary)',
        fontSize: '12px',
        textAlign: 'left',
        cursor: 'pointer'
      }}
      onMouseEnter={(e) => (e.currentTarget.style.backgroundColor = 'var(--bg-tertiary)')}
      onMouseLeave={(e) =>
        (e.currentTarget.style.backgroundColor = active ? 'var(--bg-tertiary)' : 'transparent')
      }
    >
      {children}
    </button>
  )
}
