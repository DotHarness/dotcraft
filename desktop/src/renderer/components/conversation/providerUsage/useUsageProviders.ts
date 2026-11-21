import { useMemo } from 'react'
import { useConfigValue } from '../../../stores/configStore'
import { useProvidersStore, type ProviderSummary } from '../../../stores/providersStore'
import {
  findSubAgentSelection,
  readSubAgentModelChoices,
  readSubAgentSelections,
  sameProvider
} from '../../settings/panels/subAgentModels/subAgentModels'

export function collectUsageProviderIds(mainProviderId: string, subAgentPreferences: unknown, subAgentModels: unknown): string[] {
  const ids: string[] = []
  const add = (id: string | null | undefined): void => {
    const trimmed = id?.trim()
    if (trimmed && !ids.some((existing) => sameProvider(existing, trimmed))) ids.push(trimmed)
  }
  add(mainProviderId)
  add(findSubAgentSelection(readSubAgentSelections(subAgentPreferences), mainProviderId)?.providerId)
  for (const choice of readSubAgentModelChoices(subAgentModels)) add(choice.providerId)
  return ids
}

export function useUsageProviders(mainProvider: ProviderSummary | null): ProviderSummary[] {
  const subAgentPreferences = useConfigValue('SubAgent.ProviderPreferences')
  const subAgentModels = useConfigValue('SubAgent.Models')
  const providers = useProvidersStore((state) => state.providers)
  return useMemo(() => {
    if (!mainProvider) return []
    const ids = collectUsageProviderIds(mainProvider.id, subAgentPreferences, subAgentModels)
    return ids.flatMap((id) => {
      if (sameProvider(id, mainProvider.id)) return [mainProvider]
      const provider = providers.find((candidate) => sameProvider(candidate.id, id))
      return provider ? [provider] : []
    })
  }, [mainProvider, providers, subAgentModels, subAgentPreferences])
}
