import type { ConfigDescriptorWire, ConfigGroupDescriptorWire, ModuleInterfaceWire } from './channelModules'

export interface ChannelPluginInfo {
  moduleId: string
  channelName: string
  displayName: string
  packageName: string
  supportedTransports: string[]
  localizedDisplayName?: Record<string, string>
  variant: string
  interface?: ModuleInterfaceWire
  configDescriptors?: ConfigDescriptorWire[]
  configGroups?: ConfigGroupDescriptorWire[]
  requiresInteractiveSetup: boolean
  capabilitySummary?: Record<string, unknown>
  minHostVersion?: string
  compatibilityError?: string
}
