import { createElement, lazy, Suspense, type JSX, type ReactNode } from 'react'

const loadChannelsView = () => import('../components/channels/ChannelsView')
  .then((module) => ({ default: module.ChannelsView }))
const loadAgentBuilderView = () => import('../components/agents/AgentBuilderView')
  .then((module) => ({ default: module.AgentBuilderView }))
const loadAutomationsView = () => import('../components/automations/AutomationsView')
  .then((module) => ({ default: module.AutomationsView }))
const loadPluginsView = () => import('../components/plugins/PluginsView')
  .then((module) => ({ default: module.PluginsView }))
const loadSettingsView = () => import('../components/settings/SettingsView')
  .then((module) => ({ default: module.SettingsView }))

export const coreMainViews = {
  channels: lazy(loadChannelsView),
  agents: lazy(loadAgentBuilderView),
  automations: lazy(loadAutomationsView),
  skills: lazy(loadPluginsView),
  settings: lazy(loadSettingsView)
} as const

export function CoreMainViewBoundary({ children }: { children: ReactNode }): JSX.Element {
  return createElement(Suspense, { fallback: null }, children)
}
