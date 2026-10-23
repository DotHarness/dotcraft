import { useEffect, useState } from 'react'
import { CalendarClock } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useAutomationsStore } from '../../stores/automationsStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import { useWorkspaceProjectChoices } from '../conversation/workspaceProjectMenu'
import { useDetailPanelTabClose } from '../layout/useDetailPanelTabClose'
import { ConfirmDialog } from '../ui/ConfirmDialog'
import { AutomationEditor } from './AutomationEditor'

const noop = (): void => {}

export function AutomationViewerTab({ tabId }: { tabId: string }): JSX.Element | null {
  const t = useT()
  const automationId = useViewerTabStore((state) => {
    const tab = state.getThreadState(state.currentThreadId ?? '').tabs.find((item) => item.id === tabId)
    return tab?.kind === 'automation' ? tab.automationId : null
  })
  const automation = useAutomationsStore((state) => state.automations.find((item) => item.id === automationId))
  const foregroundWorkspacePath = useWorkspaceProjectsStore((state) => state.foregroundWorkspacePath)
  const { foregroundIsChat } = useWorkspaceProjectChoices(foregroundWorkspacePath, false)
  const { closeTab } = useDetailPanelTabClose()
  const [missing, setMissing] = useState(false)
  const [removing, setRemoving] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const close = (): void => { void closeTab({ kind: 'viewer', id: tabId }) }

  useEffect(() => {
    if (!automationId) return
    setMissing(false)
    const store = useAutomationsStore.getState()
    void store.readAutomation(automationId).catch(() => setMissing(true))
    void store.fetchRuns(automationId).catch(() => {})
  }, [automationId])

  async function action(operation: () => Promise<unknown>): Promise<void> {
    setActionError(null)
    try { await operation() } catch (error) { setActionError(String(error)) }
  }

  if (!automationId) return null
  if (!automation) {
    return (
      <div className="dc-automation-viewer-tab dc-automation-viewer-tab--placeholder">
        {missing ? <span className="dc-automation-viewer-tab__mark" aria-hidden><CalendarClock size={18} strokeWidth={1.8} /></span> : null}
        <p role={missing ? 'alert' : 'status'}>{t(missing ? 'automation.unavailable' : 'common.loading')}</p>
      </div>
    )
  }
  return (
    <div className="dc-automation-viewer-tab">
      <AutomationEditor
        key={automation.id} automation={automation} initial={automation} chats={foregroundIsChat}
        onSaved={noop} onDirtyChange={noop}
        onDelete={() => setRemoving(true)}
        onAction={(operation) => void action(() => operation === 'run'
          ? useAutomationsStore.getState().run(automation.id)
          : useAutomationsStore.getState().setEnabled(automation.id, operation === 'resume'))}
      />
      {actionError ? <p role="alert" className="dc-automation-save-error">{actionError}</p> : null}
      {removing ? <ConfirmDialog title={t('automation.delete')} message={t('automation.deleteHint')} danger onConfirm={() => {
        setRemoving(false)
        void action(async () => {
          await useAutomationsStore.getState().remove(automation.id)
          close()
        })
      }} onCancel={() => setRemoving(false)} /> : null}
    </div>
  )
}
