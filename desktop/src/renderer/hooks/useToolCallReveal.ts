import { useEffect, type RefObject } from 'react'
import { useConversationStore } from '../stores/conversationStore'
import { loadThreadHistoryGap, useThreadHistoryStore } from '../stores/threadHistoryStore'
import { useThreadStore } from '../stores/threadStore'
import { addToast } from '../stores/toastStore'
import { findToolCallRevealTarget, finishToolCallReveal, useToolCallRevealStore, type ToolCallRevealMatch } from '../stores/toolCallRevealStore'
import { useT } from '../contexts/LocaleContext'
import { prefersReducedMotion } from '../utils/appearance'

const RENDER_TIMEOUT_MS = 1500
const EXPAND_SETTLE_MS = 260
const FLASH_KEYFRAMES: Keyframe[] = [
  { backgroundColor: 'color-mix(in srgb, var(--text-primary) 10%, transparent)' },
  { backgroundColor: 'color-mix(in srgb, var(--text-primary) 10%, transparent)', offset: 0.35 },
  { backgroundColor: 'transparent' }
]
const FLASH_TIMING: KeyframeAnimationOptions = { duration: 1400, easing: 'cubic-bezier(0.23, 1, 0.32, 1)' }

function findRendered(container: HTMLElement | null, attribute: 'callId' | 'turnId', value: string): HTMLElement | undefined {
  const selector = attribute === 'callId' ? '[data-call-id]' : '[data-turn-id]'
  return [...(container?.querySelectorAll<HTMLElement>(selector) ?? [])].find((node) => node.dataset[attribute] === value)
}

function unavailableMessageKey(match: ToolCallRevealMatch): string {
  if ('agentPath' in match) return 'conversation.revealSubagentUnavailable'
  if ('workflowRunId' in match) return 'conversation.revealWorkflowUnavailable'
  return 'conversation.revealCommandUnavailable'
}

export function useToolCallReveal(container: RefObject<HTMLDivElement | null>): void {
  const request = useToolCallRevealStore((s) => s.request)
  const target = useToolCallRevealStore((s) => s.target)
  const active = useThreadStore((s) => s.activeThreadId)
  const historyReady = useThreadHistoryStore((s) => s.threadId === active && s.headLoaded)
  const gaps = useThreadHistoryStore((s) => s.gaps)
  const turns = useConversationStore((s) => s.turns)
  const t = useT()

  useEffect(() => {
    if (!request || target || active !== request.threadId || !historyReady) return
    const found = findToolCallRevealTarget(turns, request)
    if (found) {
      useToolCallRevealStore.setState({ target: found })
      return
    }
    const newestGap = gaps[gaps.length - 1]
    if (!newestGap) {
      addToast(t(unavailableMessageKey(request.match)), 'error')
      finishToolCallReveal(request.nonce)
      return
    }
    let cancelled = false
    void loadThreadHistoryGap(newestGap.id, 'older').catch((error) => {
      if (cancelled) return
      addToast(String(error), 'error')
      finishToolCallReveal(request.nonce)
    })
    return () => { cancelled = true }
  }, [request, target, active, historyReady, gaps, turns, t])

  useEffect(() => {
    if (!target) return
    let cancelled = false
    let frame = 0
    let timer = 0
    const startedAt = performance.now()
    const show = (node: HTMLElement, flash: boolean): void => {
      timer = window.setTimeout(() => {
        if (cancelled) return
        const reduced = prefersReducedMotion()
        node.scrollIntoView({ block: 'center', behavior: reduced ? 'instant' : 'smooth' })
        if (flash && !reduced) node.animate?.(FLASH_KEYFRAMES, FLASH_TIMING)
        finishToolCallReveal(target.nonce)
      }, EXPAND_SETTLE_MS)
    }
    const check = (): void => {
      if (cancelled) return
      const item = findRendered(container.current, 'callId', target.callId)
      if (item) return show(item, true)
      if (performance.now() - startedAt < RENDER_TIMEOUT_MS) {
        frame = requestAnimationFrame(check)
        return
      }
      const turn = findRendered(container.current, 'turnId', target.turnId)
      if (turn) show(turn, false)
      else finishToolCallReveal(target.nonce)
    }
    check()
    return () => {
      cancelled = true
      cancelAnimationFrame(frame)
      window.clearTimeout(timer)
    }
  }, [target, container])
}
