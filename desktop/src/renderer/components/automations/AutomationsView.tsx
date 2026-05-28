import { useEffect, useMemo, useState, type CSSProperties } from 'react'
import { Ellipsis, Pencil, Play, Plus, Trash2 } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import {
  useAutomationsStore,
  type AutomationTemplate
} from '../../stores/automationsStore'
import { useCronStore } from '../../stores/cronStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useUIStore } from '../../stores/uiStore'
import { TaskCard } from './TaskCard'
import { NewTaskDialog } from './NewTaskDialog'
import { TaskReviewPanel } from './TaskReviewPanel'
import { CronJobCard } from './CronJobCard'
import { CronReviewPanel } from './CronReviewPanel'
import { useReviewPanelStore } from '../../stores/reviewPanelStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { RefreshIcon } from '../ui/AppIcons'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { ConfirmDialog } from '../ui/ConfirmDialog'
import {
  CatalogCompactGrid,
  CatalogSection,
  CatalogTabs,
  styles as catalogStyles
} from '../catalog/CatalogSurface'

function SkeletonCard(): JSX.Element {
  return (
    <div style={skeletonRow}>
      <div style={{ ...skeletonBlock, width: '42px', height: '16px' }} />
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: '6px' }}>
        <div style={{ ...skeletonBlock, width: '70%', height: '14px' }} />
        <div style={{ ...skeletonBlock, width: '40%', height: '12px' }} />
      </div>
    </div>
  )
}

export function AutomationsView(): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const capabilities = useConnectionStore((s) => s.capabilities)
  const hasTasks = capabilities?.automations === true
  const hasCron = capabilities?.cronManagement === true
  const automationsTab = useUIStore((s) => s.automationsTab)
  const setAutomationsTab = useUIStore((s) => s.setAutomationsTab)

  const { tasks, loading, error, fetchTasks } = useAutomationsStore()
  const selectedTaskId = useAutomationsStore((s) => s.selectedTaskId)
  const selectTask = useAutomationsStore((s) => s.selectTask)
  const startPolling = useAutomationsStore((s) => s.startPolling)
  const stopPolling = useAutomationsStore((s) => s.stopPolling)

  const cronJobs = useCronStore((s) => s.jobs)
  const cronLoading = useCronStore((s) => s.loading)
  const cronError = useCronStore((s) => s.error)
  const fetchCronJobs = useCronStore((s) => s.fetchJobs)
  const startCronPolling = useCronStore((s) => s.startPolling)
  const stopCronPolling = useCronStore((s) => s.stopPolling)
  const selectedCronJobId = useCronStore((s) => s.selectedCronJobId)
  const selectCronJob = useCronStore((s) => s.selectCronJob)

  const [showNewTask, setShowNewTask] = useState(false)
  const [newTaskTemplate, setNewTaskTemplate] = useState<AutomationTemplate | undefined>(undefined)
  const [newDialogTab, setNewDialogTab] = useState<'task' | 'template'>('task')
  const [editingTemplate, setEditingTemplate] = useState<AutomationTemplate | undefined>(undefined)
  const [menuPosition, setMenuPosition] = useState<ContextMenuPosition | null>(null)
  const [reviewAsDrawer, setReviewAsDrawer] = useState(
    () => typeof window !== 'undefined' && window.innerWidth < 980
  )
  const templates = useAutomationsStore((s) => s.templates)
  const fetchTemplates = useAutomationsStore((s) => s.fetchTemplates)

  const activePanel: 'tasks' | 'cron' =
    automationsTab === 'tasks' && hasTasks
      ? 'tasks'
      : automationsTab === 'cron' && hasCron
        ? 'cron'
        : hasTasks
          ? 'tasks'
          : 'cron'

  const showTabBar = hasTasks && hasCron

  useEffect(() => {
    if (hasTasks && !hasCron) setAutomationsTab('tasks')
    else if (!hasTasks && hasCron) setAutomationsTab('cron')
  }, [hasTasks, hasCron, setAutomationsTab])

  useEffect(() => {
    if (activePanel === 'tasks' && selectedCronJobId) selectCronJob(null)
    if (activePanel === 'cron' && selectedTaskId) {
      useReviewPanelStore.getState().destroyReviewPanel()
      selectTask(null)
    }
  }, [activePanel, selectedCronJobId, selectedTaskId, selectCronJob, selectTask])

  useEffect(() => {
    function updateReviewMode(): void {
      setReviewAsDrawer(window.innerWidth < 980)
    }
    updateReviewMode()
    window.addEventListener('resize', updateReviewMode)
    return () => window.removeEventListener('resize', updateReviewMode)
  }, [])

  useEffect(() => {
    if (!hasTasks) {
      stopPolling()
      return
    }
    startPolling()
    return () => {
      stopPolling()
    }
  }, [hasTasks, startPolling, stopPolling])

  useEffect(() => {
    if (hasTasks) {
      void fetchTemplates(locale)
    }
  }, [hasTasks, locale, fetchTemplates])

  useEffect(() => {
    return () => {
      useReviewPanelStore.getState().destroyReviewPanel()
    }
  }, [])

  useEffect(() => {
    if (activePanel !== 'cron' || !hasCron) {
      stopCronPolling()
      return
    }
    void fetchCronJobs()
    startCronPolling()
    return () => {
      stopCronPolling()
    }
  }, [activePanel, hasCron, fetchCronJobs, startCronPolling, stopCronPolling])

  const sortedTasks = useMemo(
    () =>
      [...tasks].sort(
        (a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime()
      ),
    [tasks]
  )

  const templateSections = useMemo(() => buildTemplateSections(templates, t), [templates, t])
  const reviewPanel =
    selectedTaskId != null
      ? <TaskReviewPanel />
      : selectedCronJobId != null
        ? <CronReviewPanel />
        : null

  function openNewTask(template?: AutomationTemplate): void {
    setNewTaskTemplate(template)
    setEditingTemplate(undefined)
    setNewDialogTab('task')
    setShowNewTask(true)
  }

  function openTemplateEditor(template?: AutomationTemplate): void {
    setEditingTemplate(template)
    setNewTaskTemplate(undefined)
    setNewDialogTab('template')
    setShowNewTask(true)
  }

  const refreshCurrent = (): void => {
    if (activePanel === 'tasks') void fetchTasks()
    else void fetchCronJobs()
  }

  function closeReviewPanel(): void {
    if (selectedTaskId) {
      useReviewPanelStore.getState().closeReviewPanel()
    }
    if (selectedCronJobId) {
      selectCronJob(null)
    }
  }

  return (
    <div style={page}>
      {showTabBar && (
        <CatalogTabs
          value={activePanel}
          onChange={(next) => {
            if (next === 'tasks' && hasTasks) setAutomationsTab('tasks')
            if (next === 'cron' && hasCron) setAutomationsTab('cron')
          }}
          items={[
            { value: 'tasks', label: t('auto.tabTasks') },
            { value: 'cron', label: t('auto.tabCron') }
          ]}
        />
      )}

      <header style={browseHeader}>
        <div style={topActions}>
          {activePanel === 'tasks' && (
            <button
              type="button"
              aria-label={t('auto.createTask')}
              onClick={() => openNewTask()}
              style={primaryCreateButton}
            >
              <Plus size={14} aria-hidden />
              <span style={primaryActionLabel}>{t('auto.newTaskButtonLabel')}</span>
            </button>
          )}
          <ActionTooltip label={t('auto.moreActions')} placement="bottom">
            <button
              type="button"
              aria-label={t('auto.moreActions')}
              onClick={(event) => setMenuPosition({ x: event.clientX, y: event.clientY })}
              style={iconButton}
            >
              <Ellipsis size={16} aria-hidden />
            </button>
          </ActionTooltip>
        </div>
        <h1 style={heroTitle}>{t('auto.viewTitle')}</h1>
      </header>

      <div style={contentShell}>
        <div style={contentPane}>
          {activePanel === 'tasks' && (
            <main id="automations-task-list" role="tabpanel" style={browseMain}>
              {templateSections.map((section) => (
                <CatalogSection key={section.key} title={section.title}>
                  <CatalogCompactGrid>
                    {section.templates.map((tpl) => (
                      <TemplateCard
                        key={tpl.id}
                        template={tpl}
                        onSelect={() => openNewTask(tpl)}
                        onEdit={() => openTemplateEditor(tpl)}
                      />
                    ))}
                    {section.key === 'user' && (
                      <CreateTemplateCard onClick={() => openTemplateEditor()} />
                    )}
                  </CatalogCompactGrid>
                </CatalogSection>
              ))}

              {templateSections.length === 0 && (
                <CatalogSection title={t('auto.templates.title')}>
                  <p style={emptyText}>{t('auto.templates.empty')}</p>
                </CatalogSection>
              )}

              <CatalogSection title={t('auto.tasks.title')}>
                {loading && (
                  <div style={listConstrained}>
                    <SkeletonCard />
                    <SkeletonCard />
                    <SkeletonCard />
                  </div>
                )}

                {!loading && error && (
                  <RetryState message={error} onRetry={() => void fetchTasks()} />
                )}

                {!loading && !error && sortedTasks.length === 0 && (
                  <EmptyState title={t('auto.emptyTasks')} hint={t('auto.emptyTasksHint')} />
                )}

                {!loading && !error && sortedTasks.length > 0 && (
                  <div style={listConstrained}>
                    {sortedTasks.map((task) => (
                      <TaskCard key={task.id} task={task} />
                    ))}
                  </div>
                )}
              </CatalogSection>
            </main>
          )}

          {activePanel === 'cron' && hasCron && (
            <main id="automations-cron-list" role="tabpanel" style={browseMain}>
              <CatalogSection title={t('auto.cron.title')}>
                {cronLoading && (
                  <div style={listConstrained}>
                    <SkeletonCard />
                    <SkeletonCard />
                  </div>
                )}

                {!cronLoading && cronError && (
                  <RetryState message={cronError} onRetry={() => void fetchCronJobs()} />
                )}

                {!cronLoading && !cronError && cronJobs.length === 0 && (
                  <EmptyState title={t('auto.emptyCron')} hint={t('auto.emptyCronHint')} />
                )}

                {!cronLoading && !cronError && cronJobs.length > 0 && (
                  <div style={listConstrained}>
                    {cronJobs.map((job) => <CronJobCard key={job.id} job={job} />)}
                  </div>
                )}
              </CatalogSection>
            </main>
          )}
        </div>

        {reviewPanel && !reviewAsDrawer && (
          <aside style={reviewSidePanel}>{reviewPanel}</aside>
        )}

        {reviewPanel && reviewAsDrawer && (
          <div style={reviewDrawerLayer} onMouseDown={closeReviewPanel}>
            <aside style={reviewDrawer} onMouseDown={(event) => event.stopPropagation()}>
              {reviewPanel}
            </aside>
          </div>
        )}
      </div>

      {menuPosition && (
        <ContextMenu
          position={menuPosition}
          onClose={() => setMenuPosition(null)}
          items={[
            {
              label: activePanel === 'tasks' ? t('auto.refreshTasks') : t('auto.refreshCron'),
              icon: <RefreshIcon size={14} />,
              onClick: refreshCurrent
            }
          ]}
        />
      )}

      {showNewTask && (
        <NewTaskDialog
          onClose={() => {
            setShowNewTask(false)
            setNewTaskTemplate(undefined)
            setEditingTemplate(undefined)
            setNewDialogTab('task')
          }}
          initialTemplate={newTaskTemplate}
          initialTab={newDialogTab}
          editingTemplate={editingTemplate}
        />
      )}

    </div>
  )
}

function buildTemplateSections(
  templates: AutomationTemplate[],
  t: ReturnType<typeof useT>
): Array<{ key: string; title: string; templates: AutomationTemplate[] }> {
  const sections: Array<{ key: string; title: string; templates: AutomationTemplate[] }> = []
  const userTemplates = templates.filter((tpl) => tpl.isUser)
  sections.push({ key: 'user', title: t('auto.gallery.my.heading'), templates: userTemplates })

  const grouped = new Map<string, AutomationTemplate[]>()
  for (const template of templates.filter((tpl) => !tpl.isUser)) {
    const key = template.category?.trim() || 'general'
    grouped.set(key, [...(grouped.get(key) ?? []), template])
  }

  for (const [key, group] of grouped) {
    sections.push({ key, title: templateCategoryTitle(key, t), templates: group })
  }

  return sections
}

function templateCategoryTitle(category: string, t: ReturnType<typeof useT>): string {
  const key = `auto.templates.category.${category}`
  const translated = t(key)
  if (translated !== key) return translated
  return category
    .split(/[-_\s]+/)
    .filter(Boolean)
    .map((part) => part.slice(0, 1).toUpperCase() + part.slice(1))
    .join(' ')
}

function TemplateCard({
  template,
  onSelect,
  onEdit
}: {
  template: AutomationTemplate
  onSelect(): void
  onEdit(): void
}): JSX.Element {
  const t = useT()
  const [hovered, setHovered] = useState(false)
  const [menuPosition, setMenuPosition] = useState<ContextMenuPosition | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const deleteTemplate = useAutomationsStore((s) => s.deleteTemplate)

  async function handleDelete(): Promise<void> {
    setDeleting(true)
    try {
      await deleteTemplate(template.id)
      setConfirmDelete(false)
    } finally {
      setDeleting(false)
    }
  }

  return (
    <>
      <div
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        style={{ position: 'relative', minWidth: 0 }}
      >
        <button
          type="button"
          onClick={onSelect}
          style={{
            ...templateButton,
            backgroundColor: hovered ? 'var(--bg-secondary)' : 'transparent'
          }}
        >
          <span style={templateIcon}>{template.icon ?? <Play size={17} aria-hidden />}</span>
          <span style={templateText}>
            <span style={templateTitle}>{template.title}</span>
            {template.description && <span style={templateDescription}>{template.description}</span>}
          </span>
        </button>

        {template.isUser && (
          <ActionTooltip label={t('auto.moreActions')} placement="top">
            <button
              type="button"
              aria-label={t('auto.moreActions')}
              onClick={(event) => {
                event.stopPropagation()
                const rect = event.currentTarget.getBoundingClientRect()
                setMenuPosition({ x: rect.left, y: rect.bottom + 6 })
              }}
              style={{
                ...smallIconButton,
                opacity: hovered || menuPosition ? 1 : 0
              }}
            >
              <Ellipsis size={15} aria-hidden />
            </button>
          </ActionTooltip>
        )}
      </div>

      {menuPosition && (
        <ContextMenu
          position={menuPosition}
          onClose={() => setMenuPosition(null)}
          items={[
            {
              label: t('auto.gallery.my.edit'),
              icon: <Pencil size={14} />,
              onClick: onEdit
            },
            {
              label: deleting ? t('auto.newTemplate.deleting') : t('auto.gallery.my.delete'),
              icon: <Trash2 size={14} />,
              danger: true,
              disabled: deleting,
              onClick: () => setConfirmDelete(true)
            }
          ]}
        />
      )}

      {confirmDelete && (
        <ConfirmDialog
          title={t('auto.gallery.my.delete')}
          message={t('auto.gallery.my.deleteConfirm')}
          confirmLabel={deleting ? t('auto.newTemplate.deleting') : t('auto.newTemplate.deleteConfirmBtn')}
          danger
          onConfirm={() => void handleDelete()}
          onCancel={() => setConfirmDelete(false)}
        />
      )}
    </>
  )
}

function CreateTemplateCard({ onClick }: { onClick(): void }): JSX.Element {
  const t = useT()
  return (
    <button type="button" onClick={onClick} style={createTemplateButton}>
      <span style={createTemplateIcon}>
        <Plus size={18} aria-hidden />
      </span>
      <span style={templateText}>
        <span style={templateTitle}>{t('auto.gallery.my.create')}</span>
        <span style={templateDescription}>{t('auto.gallery.my.empty')}</span>
      </span>
    </button>
  )
}

function EmptyState({ title, hint }: { title: string; hint: string }): JSX.Element {
  return (
    <div style={emptyState}>
      <p style={{ margin: 0 }}>{title}</p>
      <p style={{ margin: '8px 0 0', fontSize: '12px' }}>{hint}</p>
    </div>
  )
}

function RetryState({ message, onRetry }: { message: string; onRetry(): void }): JSX.Element {
  const t = useT()
  return (
    <div style={emptyState}>
      <p style={{ margin: 0, color: 'var(--error)' }}>{message}</p>
      <button type="button" onClick={onRetry} style={retryButton}>
        {t('common.retry')}
      </button>
    </div>
  )
}

const page: CSSProperties = catalogStyles.page
const browseHeader: CSSProperties = catalogStyles.browseHeader
const topActions: CSSProperties = catalogStyles.topActions
const heroTitle: CSSProperties = catalogStyles.heroTitle
const browseMain: CSSProperties = catalogStyles.browseMain
const iconButton: CSSProperties = catalogStyles.iconButton
const emptyText: CSSProperties = catalogStyles.emptyText

const primaryCreateButton: CSSProperties = {
  ...catalogStyles.manageButton,
  borderColor: 'var(--text-primary)',
  backgroundColor: 'var(--text-primary)',
  color: 'var(--bg-primary)',
  fontWeight: 600
}

const primaryActionLabel: CSSProperties = {
  lineHeight: 1,
  transform: 'translateY(-1px)'
}

const listConstrained: CSSProperties = {
  maxWidth: '760px',
  margin: '0 auto'
}

const contentShell: CSSProperties = {
  flex: 1,
  minHeight: 0,
  minWidth: 0,
  display: 'flex',
  position: 'relative'
}

const contentPane: CSSProperties = {
  flex: 1,
  minWidth: 0,
  minHeight: 0,
  display: 'flex',
  flexDirection: 'column'
}

const reviewSidePanel: CSSProperties = {
  width: 'min(480px, 42vw)',
  minWidth: '360px',
  maxWidth: '480px',
  height: '100%',
  flexShrink: 0
}

const reviewDrawerLayer: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 50,
  display: 'flex',
  justifyContent: 'flex-end',
  backgroundColor: 'color-mix(in srgb, var(--bg-primary) 24%, transparent)'
}

const reviewDrawer: CSSProperties = {
  width: 'min(480px, 92vw)',
  height: '100%',
  boxShadow: '-12px 0 30px color-mix(in srgb, var(--bg-primary) 28%, transparent)'
}

const skeletonRow: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '12px',
  minHeight: '58px',
  padding: '0 8px',
  borderRadius: '8px'
}

const skeletonBlock: CSSProperties = {
  borderRadius: '4px',
  backgroundColor: 'var(--bg-tertiary)',
  animation: 'pulse 1.5s ease-in-out infinite'
}

const templateButton: CSSProperties = {
  width: '100%',
  minWidth: 0,
  minHeight: '72px',
  display: 'flex',
  alignItems: 'center',
  gap: '12px',
  padding: '8px',
  border: 'none',
  borderRadius: '8px',
  backgroundColor: 'transparent',
  color: 'var(--text-primary)',
  cursor: 'pointer',
  textAlign: 'left'
}

const templateText: CSSProperties = {
  minWidth: 0,
  flex: 1,
  display: 'flex',
  flexDirection: 'column',
  gap: '4px',
  overflow: 'hidden'
}

const templateTitle: CSSProperties = {
  fontSize: '13px',
  lineHeight: 1.25,
  fontWeight: 700,
  color: 'var(--text-primary)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const templateDescription: CSSProperties = {
  fontSize: '12px',
  lineHeight: 1.35,
  color: 'var(--text-secondary)',
  display: '-webkit-box',
  WebkitLineClamp: 2,
  WebkitBoxOrient: 'vertical',
  overflow: 'hidden',
  wordBreak: 'break-word'
}

const templateIcon: CSSProperties = {
  width: '38px',
  height: '38px',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0,
  borderRadius: '8px',
  backgroundColor: 'var(--bg-secondary)',
  fontSize: '19px'
}

const smallIconButton: CSSProperties = {
  position: 'absolute',
  top: '8px',
  right: '8px',
  width: '28px',
  height: '28px',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-primary)',
  color: 'var(--text-secondary)',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  cursor: 'pointer',
  padding: 0,
  transition: 'opacity 0.15s'
}

const createTemplateButton: CSSProperties = {
  ...templateButton,
  border: '1px dashed var(--border-default)'
}

const createTemplateIcon: CSSProperties = {
  ...templateIcon,
  backgroundColor: 'transparent',
  border: '1px solid var(--border-default)',
  color: 'var(--text-secondary)'
}

const emptyState: CSSProperties = {
  maxWidth: '760px',
  margin: '0 auto',
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  justifyContent: 'center',
  padding: '34px 20px',
  color: 'var(--text-tertiary)',
  fontSize: '13px',
  textAlign: 'center'
}

const retryButton: CSSProperties = {
  marginTop: '12px',
  padding: '5px 14px',
  borderRadius: '6px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'transparent',
  color: 'var(--text-secondary)',
  fontSize: '12px',
  cursor: 'pointer'
}
