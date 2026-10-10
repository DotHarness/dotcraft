import type { ModelCatalogItem, ReasoningConfig, ThreadConfiguration } from '@dotcraft/sdk/contracts'

export type ApprovalPolicy = 'prompt' | 'autoApprove'
export type Speed = 'standard' | 'fast'
export type ReasoningValue = 'default' | 'off' | string

export interface ChatControls {
  providerId: string | null
  model: string | null
  reasoning: ReasoningValue
  speed: Speed
  approvalPolicy: ApprovalPolicy
}

export type ConfigChange =
  | { kind: 'model'; providerId: string | null; model: string; catalog: ModelCatalogItem | undefined }
  | { kind: 'reasoning'; value: ReasoningValue }
  | { kind: 'speed'; speed: Speed }
  | { kind: 'approval'; policy: ApprovalPolicy }

const DEFAULT_REASONING = { enabled: false, effort: 'medium', output: 'full' }

function reasoningOf(value: ReasoningConfig | null | undefined): { enabled: boolean; effort: string; output: string } | null {
  if (!value) return null
  return { enabled: value.enabled === true, effort: value.effort || 'medium', output: value.output || 'full' }
}

const APPROVAL_DEFAULT_KEY = 'Permissions.DefaultApprovalPolicy'

function field(value: unknown, name: string): unknown {
  if (!value || typeof value !== 'object') return undefined
  const key = Object.keys(value).find((entry) => entry.toLowerCase() === name.toLowerCase())
  return key === undefined ? undefined : (value as Record<string, unknown>)[key]
}

export function workspaceApprovalOf(config: unknown): ApprovalPolicy {
  return field(field(config, 'Permissions'), 'DefaultApprovalPolicy') === 'autoApprove' ? 'autoApprove' : 'prompt'
}

export function changesWorkspaceApproval(regions: readonly string[] | null | undefined): boolean {
  return (regions ?? []).some((region) => region === APPROVAL_DEFAULT_KEY || APPROVAL_DEFAULT_KEY.startsWith(`${region}.`))
}

export function controlsOf(config: ThreadConfiguration | null | undefined, workspaceApproval: ApprovalPolicy): ChatControls {
  const reasoning = reasoningOf(config?.reasoning)
  return {
    providerId: config?.providerId || null,
    model: config?.model || null,
    reasoning: reasoning ? (reasoning.enabled ? reasoning.effort : 'off') : 'default',
    speed: config?.speed === 'fast' ? 'fast' : 'standard',
    approvalPolicy: config?.approvalPolicy === 'autoApprove' || config?.approvalPolicy === 'prompt' ? config.approvalPolicy : workspaceApproval,
  }
}

function reasoningPayload(value: ReasoningValue, current: ReasoningConfig | null | undefined): ReasoningConfig | null {
  if (value === 'default') return null
  const base = reasoningOf(current) ?? DEFAULT_REASONING
  return value === 'off' ? { ...base, enabled: false } : { ...base, enabled: true, effort: value }
}

function compatible(config: ThreadConfiguration, model: ModelCatalogItem | undefined): void {
  const capability = model?.reasoning
  const current = reasoningOf(config.reasoning)
  if (!capability || !current) return
  const supported = capability.supportedEfforts?.some((option) => option.effort === current.effort) === true
  if ((!current.enabled && !capability.supportsDisable) || (current.enabled && !supported)) {
    const outputs = capability.supportedOutputs ?? []
    config.reasoning = {
      enabled: true,
      effort: capability.defaultEffort ?? current.effort,
      output: outputs.includes(current.output) ? current.output : (capability.defaultOutput ?? current.output),
    }
  }
}

export function applyChange(config: ThreadConfiguration, change: ConfigChange): ThreadConfiguration {
  const next = { ...config }
  switch (change.kind) {
    case 'model':
      if (change.providerId) next.providerId = change.providerId
      next.model = change.model
      compatible(next, change.catalog)
      break
    case 'reasoning': {
      const payload = reasoningPayload(change.value, next.reasoning)
      if (payload) next.reasoning = payload
      else delete next.reasoning
      break
    }
    case 'speed':
      next.speed = change.speed
      break
    case 'approval':
      next.approvalPolicy = change.policy
      break
  }
  return next
}

export interface NewChatChoices {
  touched: Partial<Record<'reasoning' | 'speed' | 'approval', true>>
  controls: ChatControls
}

export function startConfig({ touched, controls }: NewChatChoices): ThreadConfiguration | undefined {
  const config: ThreadConfiguration = {}
  if (controls.providerId && controls.model) {
    config.providerId = controls.providerId
    config.model = controls.model
  }
  if (touched.reasoning) {
    const payload = reasoningPayload(controls.reasoning, null)
    if (payload) config.reasoning = payload
  }
  if (touched.speed) config.speed = controls.speed
  if (touched.approval) config.approvalPolicy = controls.approvalPolicy
  return Object.keys(config).length > 0 ? config : undefined
}

export function offersPlanMode(config: ThreadConfiguration | null | undefined, profileId: string | null | undefined): boolean {
  return !config?.agentProfileId && !profileId
}
