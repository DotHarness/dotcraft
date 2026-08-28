import type { ThreadMode } from '../types/conversation'
import type { ThreadConfiguration } from '@dotcraft/sdk/contracts'
import type {
  ApprovalPolicyWire,
  ContextWindowConfigurationWire,
  InferenceSpeedWire,
  ReasoningConfigurationWire
} from '../types/thread'

export interface WelcomeThreadConfigurationInput {
  mode: ThreadMode
  providerId?: string
  model?: string
  reasoning: ReasoningConfigurationWire
  speed?: InferenceSpeedWire
  contextWindow?: ContextWindowConfigurationWire
  approvalPolicy?: Extract<ApprovalPolicyWire, 'prompt' | 'autoApprove'>
  approvalPolicyExplicit: boolean
  agentProfileId?: string | null
}

export function buildWelcomeThreadConfiguration(
  input: WelcomeThreadConfigurationInput
): ThreadConfiguration {
  const config: ThreadConfiguration = {}
  const profileId = input.agentProfileId?.trim()
  const providerId = input.providerId?.trim()
  const model = input.model?.trim()

  if (profileId) {
    config.agentProfileId = profileId
  } else {
    config.mode = input.mode
    if (input.approvalPolicyExplicit && input.approvalPolicy) {
      config.approvalPolicy = input.approvalPolicy
    }
  }

  if (providerId && model && model !== 'Default') {
    config.providerId = providerId
    config.model = model
  }
  config.reasoning = { ...input.reasoning }
  if (input.speed) config.speed = input.speed
  if (input.contextWindow) config.contextWindow = { ...input.contextWindow }

  return config
}
