import { createElement, Suspense, type JSX, type ReactNode } from 'react'
import { prefetchSettingsPanel } from '../components/settings/coreSettingsPanels'
import { useUIStore } from '../stores/uiStore'
import { lazyWithPrefetch } from '../utils/lazyWithPrefetch'
import { MainViewLoading } from './MainViewLoading'

export const coreMainViews = {
  channels: lazyWithPrefetch(() => import('../components/channels/ChannelsView')
    .then((module) => module.ChannelsView)),
  agents: lazyWithPrefetch(() => import('../components/agents/AgentBuilderView')
    .then((module) => module.AgentBuilderView)),
  automations: lazyWithPrefetch(() => import('../components/automations/AutomationsView')
    .then((module) => module.AutomationsView)),
  skills: lazyWithPrefetch(() => import('../components/plugins/PluginsView')
    .then((module) => module.PluginsView)),
  settings: lazyWithPrefetch(() => import('../components/settings/SettingsView')
    .then((module) => module.SettingsView))
} as const

export type CoreMainViewKey = keyof typeof coreMainViews

export function prefetchCoreMainView(view: CoreMainViewKey): void {
  void coreMainViews[view].prefetch().catch(() => undefined)
  if (view === 'settings') prefetchSettingsPanel(useUIStore.getState().activeSettingsTab)
}

export function CoreMainViewBoundary({ children, fallback }: { children: ReactNode; fallback?: ReactNode }): JSX.Element {
  return createElement(Suspense, { fallback: fallback ?? createElement(MainViewLoading) }, children)
}
