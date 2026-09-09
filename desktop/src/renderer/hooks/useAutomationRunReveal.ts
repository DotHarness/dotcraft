import { useEffect, type RefObject } from 'react'
import { useAutomationRunNavigation } from '../stores/automationRunNavigation'
import { useThreadStore } from '../stores/threadStore'
import { useConversationStore } from '../stores/conversationStore'
import { readThreadTurnsPage } from '../utils/threadHistory'
import { wireTurnToConversationTurn } from '../types/conversation'
import { addToast } from '../stores/toastStore'
import { useT } from '../contexts/LocaleContext'
import { useAutomationsStore } from '../stores/automationsStore'
export function useAutomationRunReveal(container: RefObject<HTMLDivElement | null>): void {
  const target = useAutomationRunNavigation(s => s.target)
  const active = useThreadStore(s => s.activeThreadId)
  const cursors = useThreadStore(s => s.activeHistoryCursors)
  const turns = useConversationStore(s => s.turns)
  const t = useT()
  useEffect(() => {
    if (!target || active !== target.threadId || cursors?.threadId !== active) return
    let cancelled = false
    const found = turns.some(turn => turn.id === target.turnId)
    if (found) {
      let shown = false
      const reveal = (): void => {
        if (shown || cancelled) return
        const node = [...(container.current?.querySelectorAll<HTMLElement>('[data-turn-id]') ?? [])].find(el => el.dataset.turnId === target.turnId)
        if (!node) return
        shown = true
        node.scrollIntoView({ block: 'start' })
        void useAutomationsStore.getState().markRunsRead(target.automationId, [target.id], true).catch(error => addToast(String(error), 'error'))
        useAutomationRunNavigation.setState({ target: null })
      }
      const observer = new MutationObserver(reveal)
      if (container.current) observer.observe(container.current, { childList: true, subtree: true })
      const frame = requestAnimationFrame(reveal)
      return () => { cancelled = true; observer.disconnect(); cancelAnimationFrame(frame) }
    }
    if (!cursors.turnCursor) {
      addToast(t('automation.runUnavailable'), 'error')
      useAutomationRunNavigation.setState({ target: null })
      return
    }
    const request = (method: string, params: unknown) => window.api.appServer.sendRequest(method as Parameters<typeof window.api.appServer.sendRequest>[0], params as never)
    void readThreadTurnsPage(request, active, cursors.turnCursor).then(page => {
      if (cancelled) return
      const older = page.turns.map(turn => wireTurnToConversationTurn(turn as unknown as Record<string, unknown>))
      useConversationStore.getState().setTurns([...older, ...useConversationStore.getState().turns], { preserveExistingRealtime: true, realtimeScopeThreadId: active })
      useThreadStore.getState().setActiveHistoryCursors(active, page.nextCursor)
    }).catch(error => { if (!cancelled) { addToast(String(error), 'error'); useAutomationRunNavigation.setState({ target: null }) } })
    return () => { cancelled = true }
  }, [target, active, cursors, turns, container, t])
}
