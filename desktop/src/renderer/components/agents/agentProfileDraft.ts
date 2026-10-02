import { load as loadYaml } from 'js-yaml'
/**
 * `toMarkdown` renders the write format `agent/profiles/upsert` expects.
 */

import type {
  ModelPreferenceReasoningEffort,
  ModelPreferenceSpeed
} from '../../../shared/modelPreference'

export type SaveTarget = 'user' | 'workspace'
export type AgentControl = 'full' | 'disabled' | 'allowList'
export type ToolPolicyMode = 'all' | 'allowList' | 'denyList'
export type ApprovalPolicy = 'default' | 'prompt' | 'autoApprove' | 'deny'

export interface AgentProviderPreference {
  providerId: string
  model: string
  reasoning: {
    enabled: boolean
    effort: ModelPreferenceReasoningEffort
  }
  speed: ModelPreferenceSpeed
}

// Operational mode (Agent/Plan) is intentionally NOT a profile field: it is a per-thread
// runtime posture, and a profile already scopes capability through tools/mcp/skills.

export interface ProfileDraft {
  name: string
  description: string
  providerPreference: AgentProviderPreference | null
  tools: {
    mode: ToolPolicyMode
    allow: string[]
    deny: string[]
    agentControl: AgentControl
  }
  mcp: {
    servers: string[]
    toolsAllow: string[]
    toolsDeny: string[]
  }
  skills: {
    preload: string[]
    allow: string[]
    deny: string[]
  }
  permissions: {
    approvalPolicy: ApprovalPolicy
    /** `null` when the profile does not author the key; the builder neither presents nor writes it. */
    requireApprovalOutsideWorkspace: boolean | null
  }
  roleInstructions: string
}

/** The two answers the builder offers; `default` and `deny` are read but never authored here. */
export const APPROVAL_OPTIONS: { value: ApprovalPolicy; labelKey: string }[] = [
  { value: 'prompt', labelKey: 'agentBuilder.approval.prompt' },
  { value: 'autoApprove', labelKey: 'agentBuilder.approval.autoApprove' }
]

export const DENY_APPROVAL_LABEL_KEY = 'agentBuilder.approval.deny'

export function createEmptyDraft(): ProfileDraft {
  return {
    name: '',
    description: '',
    providerPreference: null,
    tools: { mode: 'all', allow: [], deny: [], agentControl: 'full' },
    mcp: { servers: [], toolsAllow: [], toolsDeny: [] },
    skills: { preload: [], allow: [], deny: [] },
    permissions: { approvalPolicy: 'prompt', requireApprovalOutsideWorkspace: null },
    roleInstructions: ''
  }
}

type YamlMap = Record<string, unknown>

function isMap(value: unknown): value is YamlMap {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function scalarText(value: unknown): string {
  return typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean' ? String(value) : ''
}

/** A persisted `interrupt` is the former name of `deny`. */
function parseApprovalPolicy(value: string): ApprovalPolicy {
  if (value === 'interrupt') return 'deny'
  return (value || 'default') as ApprovalPolicy
}

function parseList(value: unknown): string[] {
  if (Array.isArray(value)) return value.map(scalarText).filter(Boolean)
  const single = scalarText(value)
  return single ? [single] : []
}

/** Parse the raw Markdown (frontmatter + body) returned by agent/profiles/read into a draft. */
export function parseProfile(rawContent: string | null | undefined): ProfileDraft {
  const draft = createEmptyDraft()
  const text = String(rawContent || '')
  const match = text.match(/^---\s*\n([\s\S]*?)\n---\s*\n?([\s\S]*)$/)
  if (!match) {
    draft.roleInstructions = text.trim()
    return draft
  }
  draft.roleInstructions = (match[2] || '').trim()

  // Read as YAML, as the Runtime does, so every valid layout of a key yields the same draft.
  let front: unknown = null
  try { front = loadYaml(match[1]) } catch { /* Malformed frontmatter states nothing. */ }
  if (!isMap(front)) return draft

  draft.name = scalarText(front.name)
  draft.description = scalarText(front.description)

  const tools = isMap(front.tools) ? front.tools : {}
  draft.tools.allow = parseList(tools.allow)
  draft.tools.deny = parseList(tools.deny)
  draft.tools.agentControl = (scalarText(tools.agentControl) || 'full') as AgentControl
  draft.tools.mode = 'allow' in tools ? 'allowList' : 'deny' in tools ? 'denyList' : 'all'

  const mcp = isMap(front.mcp) ? front.mcp : {}
  draft.mcp.servers = parseList(mcp.servers)
  const mcpTools = isMap(mcp.tools) ? mcp.tools : {}
  draft.mcp.toolsAllow = parseList(mcpTools.allow)
  draft.mcp.toolsDeny = parseList(mcpTools.deny)

  const skills = isMap(front.skills) ? front.skills : {}
  draft.skills.preload = parseList(skills.preload)
  draft.skills.allow = parseList(skills.allow)
  draft.skills.deny = parseList(skills.deny)

  const permissions = isMap(front.permissions) ? front.permissions : {}
  draft.permissions.approvalPolicy = parseApprovalPolicy(scalarText(permissions.approvalPolicy))
  if ('requireApprovalOutsideWorkspace' in permissions) {
    draft.permissions.requireApprovalOutsideWorkspace = scalarText(permissions.requireApprovalOutsideWorkspace) === 'true'
  }

  const preference = isMap(front.providerPreference) ? front.providerPreference : null
  const reasoning = preference && isMap(preference.reasoning) ? preference.reasoning : null
  const effort = scalarText(reasoning?.effort)
  const speed = scalarText(preference?.speed)
  if (
    preference && reasoning
    && !('output' in reasoning)
    && scalarText(preference.providerId)
    && scalarText(preference.model)
    && typeof reasoning.enabled === 'boolean'
    && ['low', 'medium', 'high', 'extraHigh', 'max', 'ultra'].includes(effort)
    && ['standard', 'fast'].includes(speed)
  ) {
    draft.providerPreference = {
      providerId: scalarText(preference.providerId),
      model: scalarText(preference.model),
      reasoning: { enabled: reasoning.enabled, effort: effort as ModelPreferenceReasoningEffort },
      speed: speed as ModelPreferenceSpeed
    }
  }
  return draft
}

function yamlList(values: string[]): string {
  return values.length === 0 ? '[]' : `[${values.join(', ')}]`
}

/** Render the draft as the raw Markdown an agent/profiles/upsert would persist. */
export function toMarkdown(draft: ProfileDraft): string {
  const fm: string[] = ['---']
  fm.push(`name: ${JSON.stringify(draft.name.trim().normalize('NFC') || 'untitled-agent')}`)
  fm.push(`description: ${JSON.stringify(draft.description)}`)
  if (draft.providerPreference) {
    const preference = draft.providerPreference
    fm.push('providerPreference:')
    fm.push(`  providerId: ${preference.providerId}`)
    fm.push(`  model: ${preference.model}`)
    fm.push('  reasoning:')
    fm.push(`    enabled: ${preference.reasoning.enabled ? 'true' : 'false'}`)
    fm.push(`    effort: ${preference.reasoning.effort}`)
    fm.push(`  speed: ${preference.speed}`)
  }

  if (draft.tools.mode !== 'all' || draft.tools.agentControl !== 'full') {
    fm.push('tools:')
    if (draft.tools.mode === 'allowList') fm.push(`  allow: ${yamlList(draft.tools.allow)}`)
    if (draft.tools.mode === 'denyList') fm.push(`  deny: ${yamlList(draft.tools.deny)}`)
    if (draft.tools.agentControl !== 'full') fm.push(`  agentControl: ${draft.tools.agentControl}`)
  }

  if (draft.mcp.servers.length || draft.mcp.toolsAllow.length || draft.mcp.toolsDeny.length) {
    fm.push('mcp:')
    if (draft.mcp.servers.length) fm.push(`  servers: ${yamlList(draft.mcp.servers)}`)
    if (draft.mcp.toolsAllow.length || draft.mcp.toolsDeny.length) {
      fm.push('  tools:')
      if (draft.mcp.toolsAllow.length) fm.push(`    allow: ${yamlList(draft.mcp.toolsAllow)}`)
      if (draft.mcp.toolsDeny.length) fm.push(`    deny: ${yamlList(draft.mcp.toolsDeny)}`)
    }
  }

  if (draft.skills.preload.length || draft.skills.allow.length || draft.skills.deny.length) {
    fm.push('skills:')
    if (draft.skills.preload.length) fm.push(`  preload: ${yamlList(draft.skills.preload)}`)
    if (draft.skills.allow.length) fm.push(`  allow: ${yamlList(draft.skills.allow)}`)
    if (draft.skills.deny.length) fm.push(`  deny: ${yamlList(draft.skills.deny)}`)
  }

  fm.push('permissions:')
  fm.push(`  approvalPolicy: ${draft.permissions.approvalPolicy}`)
  if (draft.permissions.requireApprovalOutsideWorkspace !== null) {
    fm.push(`  requireApprovalOutsideWorkspace: ${draft.permissions.requireApprovalOutsideWorkspace ? 'true' : 'false'}`)
  }
  fm.push('---')

  return `${fm.join('\n')}\n\n${draft.roleInstructions.trim()}\n`
}
