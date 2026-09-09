import { create } from 'zustand'
import { useThreadStore } from './threadStore'
import { useUIStore } from './uiStore'
import type { AutomationRun } from '../types/automation'
export const useAutomationRunNavigation = create<{ target: AutomationRun | null }>(() => ({ target: null }))
export function openAutomationRun(run: AutomationRun): void {
  if (!run.threadId || !run.turnId) return
  useAutomationRunNavigation.setState({ target: run })
  useThreadStore.getState().setActiveThreadId(run.threadId)
  useUIStore.getState().setActiveMainView('conversation')
}
