import { useMemo } from 'react'
import type { DesktopPluginComposerProvider } from '@dotcraft/plugin'
import { useConfig } from '../../stores/configStore'
import { useThreadStore } from '../../stores/threadStore'
import { resolveWorkspaceProviderFromConfig } from '../../utils/workspaceCoreConfig'
import { useProviderSummary } from '../../stores/providersStore'

export function useComposerProvider(explicitProviderId?: string | null, threadId?: string | null): DesktopPluginComposerProvider | null {
  const workspaceConfig = useConfig()
  const threadProviderId = useThreadStore((state) => {
    const thread = state.activeThread
    if (threadId != null && thread?.id !== threadId) return null
    const value = thread?.configuration?.providerId ?? thread?.configuration?.ProviderId
    return typeof value === 'string' ? value.trim() : null
  })
  const providerId = explicitProviderId !== undefined
    ? explicitProviderId?.trim() || null
    : threadProviderId || resolveWorkspaceProviderFromConfig(workspaceConfig ?? {}) || null
  const backendId = useProviderSummary(providerId)?.backendId ?? null
  return useMemo(() => (providerId ? { id: providerId, backendId } : null), [providerId, backendId])
}
