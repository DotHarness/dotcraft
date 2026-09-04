import type { LocalizedTextMap } from './locales'

export interface ConfigDescriptorWire {
  key: string
  displayLabel: string
  description: string
  localizedDisplayLabel?: LocalizedTextMap
  localizedDescription?: LocalizedTextMap
  required: boolean
  dataKind: string
  masked: boolean
  interactiveSetupOnly: boolean
  group?: string
  advanced?: boolean
  defaultValue?: unknown
  options?: ConfigFieldOptionWire[]
  allowCustomValue?: boolean
  enumValues?: string[]
  docsPath?: LocalizedTextMap
}

export interface ConfigFieldOptionWire {
  value: string
  displayLabel: string
  localizedDisplayLabel?: LocalizedTextMap
  description?: string
  localizedDescription?: LocalizedTextMap
  preview?: string
}

export interface ConfigGroupDescriptorWire {
  id: string
  displayLabel: string
  localizedDisplayLabel?: LocalizedTextMap
  description?: string
  localizedDescription?: LocalizedTextMap
}

export interface ModuleInterfaceWire {
  shortDescription?: string
  localizedShortDescription?: LocalizedTextMap
  longDescription?: string
  localizedLongDescription?: LocalizedTextMap
  previewPrompt?: string
  localizedPreviewPrompt?: LocalizedTextMap
}

export interface DiscoveredModule {
  moduleId: string
  channelName: string
  displayName: string
  localizedDisplayName?: LocalizedTextMap
  interface?: ModuleInterfaceWire
  packageName: string
  configFileName: string
  supportedTransports: string[]
  requiresInteractiveSetup: boolean
  capabilitySummary?: Record<string, unknown>
  variant: string
  source: 'bundled' | 'user'
  absolutePath: string
  configGroups?: ConfigGroupDescriptorWire[]
  configDescriptors: ConfigDescriptorWire[]
}

export type ModuleProcessState = 'starting' | 'running' | 'stopping' | 'stopped' | 'crashed'

export interface ModuleStatusEntry {
  processState: ModuleProcessState
  connected: boolean
  failureCode?: string
}

export type ModuleStatusMap = Record<string, ModuleStatusEntry>

export interface QrUpdatePayload {
  moduleId: string
  qrDataUrl: string | null
  timestamp: number
}
