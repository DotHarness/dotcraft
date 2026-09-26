import { useEffect, type RefObject } from 'react'
import { useAutomationRunNavigation } from '../stores/automationRunNavigation'
import { useThreadStore } from '../stores/threadStore'
import { useConversationStore } from '../stores/conversationStore'
import { loadThreadHistoryGap, useThreadHistoryStore } from '../stores/threadHistoryStore'
import { addToast } from '../stores/toastStore'
import { useT } from '../contexts/LocaleContext'
import { useAutomationsStore } from '../stores/automationsStore'
export function useAutomationRunReveal(container: RefObject<HTMLDivElement | null>): void {
  const target = useAutomationRunNavigation(s => s.target)
  const active = useThreadStore(s => s.activeThreadId)
  const historyReady = useThreadHistoryStore(s => s.threadId === active && s.headLoaded)
  const gaps = useThreadHistoryStore(s => s.gaps)
  const turns = useConversationStore(s => s.turns)
  const t = useT()
  useEffect(() => {
    if (!target || active !== target.threadId || !historyReady) return
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
    const newestGap = gaps[gaps.length - 1]
    if (!newestGap) {
      addToast(t('automation.runUnavailable'), 'error')
      useAutomationRunNavigation.setState({ target: null })
      return
    }
    void loadThreadHistoryGap(newestGap.id, 'older').catch(error => { if (!cancelled) { addToast(String(error), 'error'); useAutomationRunNavigation.setState({ target: null }) } })
    return () => { cancelled = true }
  }, [target, active, historyReady, gaps, turns, container, t])
}
