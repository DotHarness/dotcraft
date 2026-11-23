import { selectModelCatalog, useModelCatalogStore } from '../../stores/modelCatalogStore'
import { useProviderSummary } from '../../stores/providersStore'
import type { ContextHandoff, HandoffIdentity } from '../../types/contextHandoff'
import { providerPresetMark } from '../settings/providers/providerPresets'
import { ContextHandoffDivider, type ContextHandoffEndpoint } from './ContextHandoffDivider'

function useHandoffEndpoint({ providerId, model }: HandoffIdentity): ContextHandoffEndpoint {
  const provider = useProviderSummary(providerId)
  const modelName = useModelCatalogStore((state) =>
    selectModelCatalog(state, providerId).models.find((item) => item.id === model)?.displayName || model
  )
  return {
    providerName: provider?.displayName || providerId,
    mark: provider ? providerPresetMark(provider) : null,
    pluginId: provider?.pluginId ?? null,
    model: modelName
  }
}

export function ContextHandoffNotice({ handoff }: { handoff: ContextHandoff }): JSX.Element {
  const from = useHandoffEndpoint(handoff.from)
  const to = useHandoffEndpoint(handoff.to)
  return <ContextHandoffDivider from={from} to={to} />
}
