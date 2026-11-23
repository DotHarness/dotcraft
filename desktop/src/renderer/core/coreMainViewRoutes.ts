import { createElement, Suspense, type JSX, type ReactNode } from 'react'
import { prefetchSettingsPanel } from '../components/settings/coreSettingsPanels'
import { useUIStore } from '../stores/uiStore'
import { lazyWithPrefetch } from '../utils/lazyWithPrefetch'
import { MainViewLoading } from './MainViewLoading'

export const coreMainViews = {
  agents: lazyWithPrefetch(() => import('../components/agents/AgentBuilderView')
    .then((module) => module.AgentBuilderView)),
  automations: lazyWithPrefetch(() => import('../components/automations/AutomationsView')
    .then((module) => module.AutomationsView)),
  skills: lazyWithPrefetch(() => import('../components/plugins/PluginsView')
    .then((module) => module.PluginsView))
} as const

export const CoreSettingsView = lazyWithPrefetch(() => import('../components/settings/SettingsView')
  .then((module) => module.SettingsView))

export type CoreMainViewKey = keyof typeof coreMainViews

// The Agent Builder releases its thread when hidden and only claims it again on mount.
export function retainsMainView(view: string): boolean {
  return view !== 'agents'
}

export function prefetchCoreMainView(view: CoreMainViewKey): void {
  void coreMainViews[view].prefetch().catch(() => undefined)
}

export function prefetchSettings(): void {
  void CoreSettingsView.prefetch().catch(() => undefined)
  prefetchSettingsPanel(useUIStore.getState().activeSettingsTab)
}

export function CoreMainViewBoundary({ children, fallback }: { children: ReactNode; fallback?: ReactNode }): JSX.Element {
  return createElement(Suspense, { fallback: fallback ?? createElement(MainViewLoading) }, children)
}
