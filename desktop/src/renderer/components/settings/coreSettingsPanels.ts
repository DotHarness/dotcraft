import { lazy } from 'react'

export const coreSettingsPanels = {
  appearance: lazy(() => import('./panels/AppearancePanel')
    .then((module) => ({ default: module.AppearancePanel }))),
  voice: lazy(() => import('./panels/VoicePanel')
    .then((module) => ({ default: module.VoicePanel }))),
  connections: lazy(() => import('./panels/connections/ConnectionsPanel')
    .then((module) => ({ default: module.ConnectionsPanel }))),
  sourceControl: lazy(() => import('./panels/SourceControlPanel')
    .then((module) => ({ default: module.SourceControlPanel }))),
  hooks: lazy(() => import('./panels/HooksPanel')
    .then((module) => ({ default: module.HooksPanel }))),
  subAgents: lazy(() => import('./panels/SubAgentsPanel')
    .then((module) => ({ default: module.SubAgentsPanel }))),
  archivedThreads: lazy(() => import('./ArchivedThreadsSettingsView')
    .then((module) => ({ default: module.ArchivedThreadsSettingsView })))
} as const
