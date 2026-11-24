export const DESKTOP_TOOL_CONFIRM_REQUEST_CHANNEL = 'workspace:tool-confirm-request'
export const DESKTOP_TOOL_CONFIRM_DISMISSED_CHANNEL = 'workspace:tool-confirm-dismissed'
export const DESKTOP_TOOL_CONFIRM_RESPOND_CHANNEL = 'workspace:tool-confirm-respond'

export interface ProjectCreationConfirmDetails {
  kind: 'projectCreation'
  name: string
  folders: string[]
  createsNewFolder: boolean
}

export interface PluginActivationConfirmDetails {
  kind: 'pluginActivation'
  /** The `plugin/view` entry resolved on the calling thread's connection, which may not be the foreground one. */
  plugin: { id: string }
}

export type DesktopToolConfirmDetails = ProjectCreationConfirmDetails | PluginActivationConfirmDetails

export type DesktopToolConfirmRequest = DesktopToolConfirmDetails & { requestId: string }

export type DesktopToolConfirmOutcome = 'confirmed' | 'canceled' | 'abandoned'
