import type { TurnStartParams } from '@dotcraft/sdk/contracts'
import type { ThreadConfigurationWire } from '../types/thread'
import { useThreadStore } from '../stores/threadStore'
import { readReasoningObject } from '../components/conversation/modelReasoning'

export type TurnModelSettings = Pick<TurnStartParams, 'providerId' | 'model' | 'reasoning' | 'speed'>

export function modelSettingsFromConfiguration(config?: ThreadConfigurationWire | null): TurnModelSettings {
  const providerId = config?.providerId ?? config?.ProviderId
  const model = config?.model ?? config?.Model
  const reasoning = readReasoningObject(config?.reasoning ?? config?.Reasoning)
  const speed = config?.speed ?? config?.Speed
  return {
    ...(typeof providerId === 'string' && providerId.trim() ? { providerId } : {}),
    ...(typeof model === 'string' && model.trim() && model !== 'Default' ? { model } : {}),
    ...(reasoning ? { reasoning: { ...reasoning } } : {}),
    ...(speed === 'standard' || speed === 'fast' ? { speed } : {})
  }
}

export function modelSettingsForThread(threadId: string): TurnModelSettings {
  const thread = useThreadStore.getState().activeThread
  return modelSettingsFromConfiguration(thread?.id === threadId ? thread.configuration : null)
}
