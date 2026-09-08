import {
  Bell,
  CalendarClock,
  ChevronRight,
  CircleDot,
  FileSearch,
  GripVertical,
  MessageSquareText,
  NotebookText,
  Pencil,
  type LucideIcon
} from 'lucide-react'
import { useCallback, useEffect, useMemo, useRef, useState, type DragEvent } from 'react'
import { useT, useLocale } from '../../contexts/LocaleContext'
import { useAutomationsStore, type AutomationDefinition, type AutomationInput, type AutomationPreset } from '../../stores/automationsStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useGitStore, normalizeGitPathKey } from '../../stores/gitStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { AUTOMATION_TASK_DRAG_MIME } from '../../utils/automationDrag'
import { automationScheduleSummary } from '../../utils/automationScheduleSummary'
import { ensureScheduleTimeZone, resolveSystemTimeZone } from '../../utils/automationTimeZone'
import { CatalogFilterButton, CatalogSearchBox } from '../catalog/CatalogSurface'
import { DragHandle } from '../layout/DragHandle'
import { ResizeEdgeGlow } from '../layout/ResizeEdgeGlow'
import { Button } from '../ui/Button'
import { ConfirmDialog } from '../ui/ConfirmDialog'
import { SplitButton } from '../ui/SplitButton'
import { AutomationEditor } from './AutomationEditor'
import { stageAutomationCreationInWelcome } from './automationDraft'

const presetPresentation: Record<string, { icon: LucideIcon; tone: string }> = {
  'daily-summary': { icon: Bell, tone: 'blue' },
  'weekly-review': { icon: NotebookText, tone: 'violet' },
  'ci-monitor': { icon: CircleDot, tone: 'orange' },
  'follow-up': { icon: FileSearch, tone: 'green' }
}
const localizedPresetIds = new Set(Object.keys(presetPresentation))

export function AutomationsView(): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const store = useAutomationsStore()
  const connected = useConnectionStore((state) => state.status === 'connected')
  const surfaceRef = useRef<HTMLDivElement>(null)
  const [manual, setManual] = useState<AutomationInput | null>(null)
  const [query, setQuery] = useState('')
  const [filter, setFilter] = useState('all')
  const [dirty, setDirty] = useState(false)
  const [pending, setPending] = useState<(() => void) | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [removing, setRemoving] = useState<AutomationDefinition | null>(null)
  const [splitPercent, setSplitPercent] = useState(50)
  const [dividerActive, setDividerActive] = useState(false)
  const selected = store.automations.find((automation) => automation.id === store.selectedAutomationId)
  const editing = selected != null || manual != null || store.selectedAutomationId != null
  const onDirty = useCallback((value: boolean) => setDirty(value), [])

  useEffect(() => {
    void store.fetchAutomations()
    void store.fetchPresets(locale).catch(() => {})
  }, [locale])

  const rows = useMemo(() => {
    const normalizedQuery = query.trim().toLocaleLowerCase()
    return store.automations.filter((automation) =>
      (filter === 'all' || automation.status === filter)
      && (!normalizedQuery || `${automation.name} ${automation.prompt}`.toLocaleLowerCase().includes(normalizedQuery)))
  }, [filter, query, store.automations])

  const presets = useMemo(() => {
    const normalizedQuery = query.trim().toLocaleLowerCase()
    return store.presets.filter((preset) => {
      const name = presetName(preset, t)
      const prompt = presetPrompt(preset, t)
      return !normalizedQuery || `${name} ${prompt}`.toLocaleLowerCase().includes(normalizedQuery)
    })
  }, [query, store.presets, t])
  const hasDefinitions = store.automations.length > 0
  const showDefinitions = store.loading || !!store.error || !!actionError || hasDefinitions

  const navigate = (next: () => void): void => {
    if (dirty) setPending(() => next)
    else next()
  }
  const close = (): void => navigate(() => {
    store.selectAutomation(null)
    setManual(null)
  })
  async function action(operation: () => Promise<unknown>): Promise<void> {
    setActionError(null)
    try { await operation() } catch (error) { setActionError(String(error)) }
  }
  function chat(prompt = t('automation.createPrompt')): void {
    navigate(() => stageAutomationCreationInWelcome(prompt))
  }
  function usePreset(preset: AutomationPreset): void {
    const schedule = preset.schedule ? ensureScheduleTimeZone(preset.schedule) : null
    const timing = schedule ? `\n${automationScheduleSummary(schedule, locale)}` : ''
    chat(`${t('automation.createPrompt')}\n\n${presetPrompt(preset, t)}${timing}`)
  }
  async function createManual(): Promise<void> {
    const path = useViewerTabStore.getState().currentWorkspacePath
      ?? useUIStore.getState().welcomeDraftWorkspacePath
      ?? useThreadStore.getState().activeThread?.workspacePath
    if (path) await useGitStore.getState().ensureBranches(path)
    const gitStatus = path
      ? useGitStore.getState().branchesByPath[normalizeGitPathKey(path)]?.status
      : undefined
    navigate(() => {
      store.selectAutomation(null)
      setManual({
        name: '', prompt: '', status: 'active', executionMode: 'independent',
        workspaceMode: gitStatus === 'available' ? 'worktree' : gitStatus === 'unavailable' ? 'project' : undefined,
        approvalPolicy: 'workspaceScope', notificationPolicy: 'all',
        schedule: { kind: 'daily', hour: 9, minute: 0, timeZone: resolveSystemTimeZone() }
      })
    })
  }
  function startDrag(event: DragEvent<HTMLElement>, automation: AutomationDefinition): void {
    event.dataTransfer.setData(AUTOMATION_TASK_DRAG_MIME, automation.id)
    event.dataTransfer.setData('text/plain', automation.name)
    event.dataTransfer.effectAllowed = 'link'
  }
  function resizeEditor(delta: number): void {
    const width = surfaceRef.current?.getBoundingClientRect().width ?? 0
    if (width <= 0) return
    setSplitPercent((current) => Math.min(65, Math.max(35, current + delta / width * 100)))
  }

  return (
    <div ref={surfaceRef} className="dc-automations" data-editing={editing || undefined}>
      {!editing && (
        <div className="dc-automations-topbar">
          <SplitButton
            label={t('automation.createButton')}
            menuLabel={t('automation.createMenu')}
            disabled={!connected}
            onClick={() => chat()}
            items={[
              { key: 'agent', label: t('automation.createWithAgent'), icon: <MessageSquareText size={15} />, onClick: () => chat() },
              { key: 'manual', label: t('automation.manual'), icon: <Pencil size={15} />, onClick: () => { void createManual() } }
            ]}
          />
        </div>
      )}
      <div className="dc-automations-body" style={editing ? { gridTemplateColumns: `${splitPercent}% ${100 - splitPercent}%` } : undefined}>
        <main className="dc-automations-list">
          <h1>{t(editing ? 'automation.title' : 'auto.viewTitle')}</h1>
          <div className="dc-automations-filters">
            <CatalogSearchBox value={query} placeholder={t('automation.search')} onChange={setQuery} />
            <CatalogFilterButton
              ariaLabel={t('automation.filter')}
              groups={[{
                label: t('automation.filter'), value: filter, onChange: setFilter,
                options: ['all', 'active', 'paused', 'completed'].map((value) => ({ value, label: t(`automation.status.${value}`) }))
              }]}
            />
          </div>

          {showDefinitions ? <section className="dc-automation-current">
            <h2>{t('automation.yours')}</h2>
            {store.loading ? <p role="status" className="dc-automation-empty">{t('common.loading')}</p> : null}
            {store.error || actionError ? <p role="alert" className="dc-automation-error">{store.error ?? actionError}</p> : null}
            {!store.loading && !rows.length ? <p className="dc-automation-empty">{t('automation.empty')}</p> : null}
            <div className="dc-automation-list-rows">
              {rows.map((automation) => (
                <article key={automation.id} draggable data-selected={selected?.id === automation.id || undefined} onDragStart={(event) => startDrag(event, automation)}>
                  <GripVertical className="dc-automation-grip" size={15} aria-hidden />
                  <span className="dc-automation-row-icon"><CalendarClock size={17} aria-hidden /></span>
                  <button type="button" onClick={() => navigate(() => { setManual(null); store.selectAutomation(automation.id) })}>
                    <strong>{automation.name}</strong>
                    <small>{automationScheduleSummary(automation.schedule, locale)} · {t(automation.executionMode === 'thread' ? 'automation.currentChat' : 'automation.newChat')}</small>
                  </button>
                  <ChevronRight size={15} aria-hidden />
                </article>
              ))}
            </div>
          </section> : null}

          <h2>{t('automation.presets')}</h2>
          <div className="dc-automation-suggestions">
            {presets.map((preset) => <AutomationSuggestion key={preset.id} preset={preset} locale={locale} onClick={() => usePreset(preset)} />)}
          </div>
        </main>

        {editing && (
          <>
            <div className="dc-automation-divider" style={{ left: `${splitPercent}%` }}>
              <ResizeEdgeGlow active={dividerActive} testId="automation-editor-divider-glow" />
            </div>
            <DragHandle onDrag={resizeEditor} onActiveChange={setDividerActive} style={{ position: 'absolute', top: 0, bottom: 0, left: `calc(${splitPercent}% - 4px)` }} />
          </>
        )}
        {selected || manual ? (
          <AutomationEditor
            key={selected?.id ?? 'new'} automation={selected} initial={selected ?? manual!}
            onDelete={() => selected && setRemoving(selected)}
            onAction={(operation) => {
              if (!selected) return
              void action(() => operation === 'run'
                ? store.run(selected.id)
                : store.save({ ...selected, status: operation === 'pause' ? 'paused' : 'active' }, selected))
            }}
            onDirtyChange={onDirty} onClose={close}
            onSaved={(automation) => { setManual(null); store.selectAutomation(automation.id) }}
          />
        ) : store.selectedAutomationId ? (
          <aside className="dc-automation-editor dc-automation-editor-unavailable">
            <p>{t('automation.unavailable')}</p>
            <Button variant="ghost" onClick={close}>{t('common.close')}</Button>
          </aside>
        ) : null}
      </div>
      {pending ? <ConfirmDialog title={t('automation.discard')} message={t('automation.discardHint')} confirmLabel={t('automation.discard')} onConfirm={() => { const next = pending; setPending(null); setDirty(false); next() }} onCancel={() => setPending(null)} /> : null}
      {removing ? <ConfirmDialog title={t('automation.delete')} message={t('automation.deleteHint')} danger onConfirm={() => { const automation = removing; setRemoving(null); void action(() => store.remove(automation.id)) }} onCancel={() => setRemoving(null)} /> : null}
    </div>
  )
}

function AutomationSuggestion({ preset, locale, onClick }: { preset: AutomationPreset; locale: ReturnType<typeof useLocale>; onClick(): void }): JSX.Element {
  const t = useT()
  const presentation = presetPresentation[preset.id] ?? { icon: CalendarClock, tone: 'blue' }
  const Icon = presentation.icon
  return (
    <button type="button" className="dc-automation-suggestion" data-tone={presentation.tone} onClick={onClick}>
      <span className="dc-automation-suggestion-icon"><Icon size={17} strokeWidth={1.7} aria-hidden /></span>
      <span>
        <span className="dc-automation-suggestion-heading">
          <strong>{presetName(preset, t)}</strong>
          {preset.schedule ? <small>{automationScheduleSummary(preset.schedule, locale, { includeTimeZone: false })}</small> : null}
        </span>
        <span className="dc-automation-suggestion-description">{presetPrompt(preset, t)}</span>
      </span>
    </button>
  )
}

function presetName(preset: AutomationPreset, t: ReturnType<typeof useT>): string {
  return localizedPresetIds.has(preset.id) ? t(`automation.preset.${preset.id}.name`) : preset.name
}
function presetPrompt(preset: AutomationPreset, t: ReturnType<typeof useT>): string {
  return localizedPresetIds.has(preset.id) ? t(`automation.preset.${preset.id}.prompt`) : preset.prompt
}
