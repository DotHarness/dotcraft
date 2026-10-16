import type { SettingsTab } from '../../types/settings'
import { lazyWithPrefetch } from '../../utils/lazyWithPrefetch'

export const coreSettingsPanels = {
  appearance: lazyWithPrefetch(() => import('./panels/AppearancePanel')
    .then((module) => module.AppearancePanel)),
  pet: lazyWithPrefetch(() => import('./panels/pet/PetPanel')
    .then((module) => module.PetPanel)),
  voice: lazyWithPrefetch(() => import('./panels/VoicePanel')
    .then((module) => module.VoicePanel)),
  computerUse: lazyWithPrefetch(() => import('./panels/computerUse/ComputerUsePanel')
    .then((module) => module.ComputerUsePanel)),
  connections: lazyWithPrefetch(() => import('./panels/connections/ConnectionsPanel')
    .then((module) => module.ConnectionsPanel)),
  sourceControl: lazyWithPrefetch(() => import('./panels/SourceControlPanel')
    .then((module) => module.SourceControlPanel)),
  hooks: lazyWithPrefetch(() => import('./panels/HooksPanel')
    .then((module) => module.HooksPanel)),
  subAgents: lazyWithPrefetch(() => import('./panels/SubAgentsPanel')
    .then((module) => module.SubAgentsPanel)),
  import: lazyWithPrefetch(() => import('./panels/ImportPanel')
    .then((module) => module.ImportPanel)),
  archivedThreads: lazyWithPrefetch(() => import('./ArchivedThreadsSettingsView')
    .then((module) => module.ArchivedThreadsSettingsView))
} as const

const panelByTab: Partial<Record<SettingsTab, { prefetch: () => Promise<void> }>> = {
  appearance: coreSettingsPanels.appearance,
  pet: coreSettingsPanels.pet,
  voice: coreSettingsPanels.voice,
  computerControl: coreSettingsPanels.computerUse,
  connections: coreSettingsPanels.connections,
  sourceControl: coreSettingsPanels.sourceControl,
  hooks: coreSettingsPanels.hooks,
  subAgents: coreSettingsPanels.subAgents,
  import: coreSettingsPanels.import,
  archivedThreads: coreSettingsPanels.archivedThreads
}

// Tabs drawn inside SettingsView have no module of their own.
export function prefetchSettingsPanel(tab: SettingsTab): void {
  void panelByTab[tab]?.prefetch().catch(() => undefined)
}
