import { useEffect, useState, type ReactNode } from 'react'
import { ArrowUpRight, ChevronRight, CirclePause, CirclePlay, MoreHorizontal, Play, Trash2, X } from 'lucide-react'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { useT } from '../../contexts/LocaleContext'
import { useAutomationsStore, editableAutomation, type AutomationDefinition, type AutomationInput } from '../../stores/automationsStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { Button } from '../ui/Button'
import { ActionTooltip } from '../ui/ActionTooltip'
import { Input, Textarea } from '../ui/Input'
import { Select } from '../ui/Select'
import { AgentProfileDropdown } from './AgentProfileDropdown'
import { AutomationScheduleEditor, validSchedule } from './AutomationScheduleEditor'
import { AutomationRunHistory } from './AutomationRunHistory'

export function AutomationEditor({ automation, initial, onClose, onSaved, onDirtyChange, onAction, onDelete }: {
  onAction?(action: 'run' | 'pause' | 'resume'): void
  onDelete?(): void
  automation?: AutomationDefinition
  initial: AutomationInput
  onClose(): void
  onSaved(value: AutomationDefinition): void
  onDirtyChange(value: boolean): void
}): JSX.Element {
  const t = useT()
  const [menu, setMenu] = useState<ContextMenuPosition | null>(null)
  const [base, setBase] = useState(automation)
  const [draft, setDraft] = useState<AutomationInput>(() => editableAutomation(initial))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const threads = useThreadStore((state) => state.threadList)
  const store = useAutomationsStore()
  const actionPending = !!(automation && store.pendingActions[automation.id])
  const completed = automation?.status === 'completed'
  const dirty = !base || JSON.stringify(draft) !== JSON.stringify(editableAutomation(base))
  const targetChatId = !dirty && draft.executionMode === 'thread' ? draft.targetThreadId : null
  const conflict = !!base && !!automation && base.version !== automation.version
  const valid = !!draft.name.trim() && !!draft.prompt.trim() && validSchedule(draft.schedule) && (draft.executionMode !== 'thread' || !!draft.targetThreadId)
  const executionOptions = [
    { value: 'independent' as const, label: t('automation.independent') },
    { value: 'thread' as const, label: t('automation.thread') }
  ]
  const threadOptions = [
    { value: '', label: t('automation.chooseChat') },
    ...(draft.targetThreadId && !threads.some((thread) => thread.id === draft.targetThreadId)
      ? [{ value: draft.targetThreadId, label: draft.targetThreadId }]
      : []),
    ...threads.filter((thread) => thread.status === 'active').map((thread) => ({ value: thread.id, label: thread.displayName ?? thread.id }))
  ]
  const workspaceOptions = [
    { value: '' as const, label: t('composer.reasoning.default') },
    { value: 'project' as const, label: t('automation.project') },
    { value: 'worktree' as const, label: t('automation.worktree') }
  ]
  const permissionOptions = [
    { value: 'workspaceScope' as const, label: t('automation.workspaceScope') },
    { value: 'fullAuto' as const, label: t('automation.fullAuto') }
  ]

  useEffect(() => { onDirtyChange(dirty); return () => onDirtyChange(false) }, [dirty, onDirtyChange])
  useEffect(() => {
    if (automation && !dirty) {
      setBase(automation)
      setDraft(editableAutomation(automation))
    }
  }, [automation, dirty])

  const update = (patch: Partial<AutomationInput>): void => setDraft((value) => ({ ...value, ...patch }))
  async function save(): Promise<void> {
    setSaving(true)
    setError(null)
    try {
      const saved = await store.save(draft, base)
      setBase(saved)
      setDraft(editableAutomation(saved))
      onSaved(saved)
    } catch (failure) {
      setError(String(failure))
    } finally {
      setSaving(false)
    }
  }
  const reset = (): void => {
    if (automation) {
      setBase(automation)
      setDraft(editableAutomation(automation))
      setError(null)
    } else {
      onClose()
    }
  }

  return (
    <aside className="dc-automation-editor" aria-label={t('automation.details')}>
      <header className="dc-automation-editor-toolbar">
        <span data-status={automation ? draft.status : 'new'}>{automation ? t(`automation.status.${draft.status}`) : t('automation.new')}</span>
        {automation && onAction ? (
          <Button variant="ghost" size="iconSm" disabled={dirty || saving} aria-label={t('automation.actions')} onClick={(event) => { const rect = event.currentTarget.getBoundingClientRect(); setMenu({ x: rect.right - 180, y: rect.bottom }) }}>
            <MoreHorizontal size={16} />
          </Button>
        ) : null}
        {automation && onAction && !completed ? (
          <ActionTooltip label={t(automation.status === 'paused' ? 'automation.resume' : 'automation.pause')} disabledReason={dirty ? t('automation.saveBeforeAction') : undefined}>
          <Button variant="ghost" size="iconSm" disabled={dirty || saving || actionPending} aria-label={t(automation.status === 'paused' ? 'automation.resume' : 'automation.pause')} onClick={() => onAction(automation.status === 'paused' ? 'resume' : 'pause')}>
            {automation.status === 'paused' ? <CirclePlay size={17} /> : <CirclePause size={17} />}
          </Button>
          </ActionTooltip>
        ) : null}
        <Button variant="ghost" size="iconSm" aria-label={t('common.close')} onClick={onClose}><X size={18} /></Button>
      </header>
      {menu && automation ? <ContextMenu position={menu} onClose={() => setMenu(null)} items={[
        { label: t('automation.runNow'), icon: <Play size={15} />, onClick: () => { setMenu(null); onAction?.('run') } },
        { label: t('automation.delete'), icon: <Trash2 size={15} />, danger: true, onClick: () => { setMenu(null); onDelete?.() } }
      ]} /> : null}
      <div className="dc-automation-editor-body">
        <fieldset disabled={saving || completed || actionPending} className="dc-automation-editor-form">
        <Input frameless className="dc-automation-title" maxLength={200} aria-label={t('automation.name')} placeholder={t('automation.name')} value={draft.name} onChange={(event) => update({ name: event.target.value })} />
        <Textarea frameless className="dc-automation-prompt" maxLength={10000} rows={3} aria-label={t('automation.prompt')} placeholder={t('automation.prompt')} value={draft.prompt} onChange={(event) => update({ prompt: event.target.value })} />

        <section className="dc-automation-section">
          <h3>{t('automation.details')}</h3>
          <div className="dc-automation-fields">
            <SettingRow label={t('automation.host')}><span className="dc-automation-static-value">{t('automation.connectedHost')}</span></SettingRow>
            <SettingRow label={t('automation.runsIn')}>
              <Select appearance="frameless" adaptiveWidth={false} value={draft.executionMode} options={executionOptions} ariaLabel={t('automation.runsIn')} onValueChange={(executionMode) => update({ executionMode, targetThreadId: executionMode === 'thread' ? draft.targetThreadId : null, notificationPolicy: executionMode === 'thread' ? 'important' : 'all' })} />
            </SettingRow>
            {draft.executionMode === 'thread' ? (
              <SettingRow label={t('automation.chat')}>
                <Select appearance="frameless" adaptiveWidth={false} value={draft.targetThreadId ?? ''} options={threadOptions} ariaLabel={t('automation.chat')} onValueChange={(targetThreadId) => update({ targetThreadId })} />
              </SettingRow>
            ) : null}
          </div>
          <p className="dc-automation-hint">{t('automation.onlineHint')}</p>
        </section>

        <AutomationScheduleEditor value={draft.schedule} onChange={(schedule) => update({ schedule })} notificationPolicy={draft.notificationPolicy} onNotificationPolicyChange={(notificationPolicy) => update({ notificationPolicy })} />

        {draft.executionMode === 'independent' ? (
          <details className="dc-automation-advanced">
            <summary><span>{t('automation.advanced')}</span><ChevronRight size={14} aria-hidden /></summary>
            <div className="dc-automation-fields">
              <SettingRow label={t('automation.agent')}><AgentProfileDropdown value={draft.agentProfileId} onChange={(agentProfileId) => update({ agentProfileId })} /></SettingRow>
              <SettingRow label={t('automation.workspace')}>
                <Select appearance="frameless" adaptiveWidth={false} value={draft.workspaceMode ?? ''} options={workspaceOptions} ariaLabel={t('automation.workspace')} onValueChange={(workspaceMode) => update({ workspaceMode: workspaceMode || undefined })} />
              </SettingRow>
              <SettingRow label={t('automation.permissions')}>
                <Select appearance="frameless" adaptiveWidth={false} value={draft.approvalPolicy} options={permissionOptions} ariaLabel={t('automation.permissions')} onValueChange={(approvalPolicy) => update({ approvalPolicy })} />
              </SettingRow>
            </div>
          </details>
        ) : null}

        </fieldset>
        {base ? <AutomationRunHistory automationId={base.id} automationName={base.name} /> : null}
        {conflict ? <div role="alert">{t('automation.conflict')} <Button variant="secondary" onClick={reset}>{t('automation.reload')}</Button></div> : null}
      </div>
      {error ? <p role="alert" className="dc-automation-save-error">{error}</p> : null}
      {dirty && !completed ? (
        <footer className="dc-automation-editor-footer">
          <Button variant="secondary" disabled={saving} onClick={reset}>{t('common.cancel')}</Button>
          <Button variant="primary" disabled={!valid || saving || conflict} onClick={() => void save()}>{t(saving ? 'automation.saving' : automation ? 'automation.save' : 'automation.createButton')}</Button>
        </footer>
      ) : targetChatId ? (
        <footer className="dc-automation-editor-footer" data-chat-action>
          <Button
            variant="outline"
            size="toolbar"
            onClick={() => {
              useThreadStore.getState().setActiveThreadId(targetChatId)
              useUIStore.getState().setActiveMainView('conversation')
            }}
          >
            <span className="dc-button__label">{t('automation.openChat')}</span>
            <ArrowUpRight size={13} aria-hidden />
          </Button>
        </footer>
      ) : null}
    </aside>
  )
}

function SettingRow({ label, children }: { label: string; children: ReactNode }): JSX.Element {
  return <div className="dc-automation-setting-row"><span>{label}</span><div>{children}</div></div>
}
