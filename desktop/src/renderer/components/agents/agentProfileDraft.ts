/**
 * Agent Builder — editable draft model.
 *
 * A `ProfileDraft` is the in-memory shape the editor manipulates. It mirrors the
 * Agent Profile frontmatter from specs/features/agent-profiles.md (name, description,
 * avatar, providerPreference, tools, mcp, skills, permissions) plus the Markdown body
 * (`roleInstructions`). `toMarkdown` renders the draft as the raw Markdown the
 * `agent/profiles/upsert` write format expects; `parseProfile` reads it back.
 */

import { decodeAvatar, encodeAvatar, type AvatarSpec } from './agentAvatar'
import type {
  ModelPreferenceContextMode,
  ModelPreferenceReasoningEffort,
  ModelPreferenceSpeed
} from '../../../shared/modelPreference'

export type SaveTarget = 'user' | 'workspace'
export type AgentControl = 'full' | 'disabled' | 'allowList'
export type ToolPolicyMode = 'all' | 'allowList' | 'denyList'
export type ApprovalPolicy = 'default' | 'prompt' | 'autoApprove' | 'interrupt'

export interface AgentProviderPreference {
  providerId: string
  model: string
  reasoning: {
    enabled: boolean
    effort: ModelPreferenceReasoningEffort
  }
  speed: ModelPreferenceSpeed
  contextWindow: {
    mode: ModelPreferenceContextMode
  }
}

// Operational mode (Agent/Plan) is intentionally NOT a profile field: it is a per-thread
// runtime posture, and a profile already expresses its capability scope through tools/mcp/skills
// allow-deny + approval policy. See specs/features/agent-profiles.md and the Plan/Custom composer pills.

export interface ProfileDraft {
  name: string
  description: string
  avatar?: AvatarSpec
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
    requireApprovalOutsideWorkspace: boolean
  }
  roleInstructions: string
}

export const APPROVAL_OPTIONS: { value: ApprovalPolicy; label: string }[] = [
  { value: 'default', label: 'Default' },
  { value: 'prompt', label: 'Prompt' },
  { value: 'autoApprove', label: 'Auto-approve' },
  { value: 'interrupt', label: 'Interrupt' }
]

export const AGENT_CONTROL_OPTIONS: { value: AgentControl; label: string }[] = [
  { value: 'full', label: 'Full' },
  { value: 'disabled', label: 'Disabled' },
  { value: 'allowList', label: 'Allow-list' }
]

export function createEmptyDraft(): ProfileDraft {
  return {
    name: '',
    description: '',
    providerPreference: null,
    tools: { mode: 'all', allow: [], deny: [], agentControl: 'full' },
    mcp: { servers: [], toolsAllow: [], toolsDeny: [] },
    skills: { preload: [], allow: [], deny: [] },
    permissions: { approvalPolicy: 'default', requireApprovalOutsideWorkspace: false },
    roleInstructions: ''
  }
}

function parseList(value: string): string[] {
  const v = (value || '').trim()
  if (!v || v === '[]') return []
  const inner = v.replace(/^\[/, '').replace(/\]$/, '')
  return inner.split(',').map((x) => x.trim()).filter(Boolean)
}

function parsePackedAvatar(value: string): AvatarSpec | undefined {
  const trimmed = value.trim()
  if (!/^\d+$/.test(trimmed)) return undefined
  return decodeAvatar(Number.parseInt(trimmed, 10)) ?? undefined
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
  const front = match[1]
  draft.roleInstructions = (match[2] || '').trim()

  let section: string | null = null
  let sub: string | null = null
  let hasToolsAllow = false
  let hasToolsDeny = false
  const providerPreference: {
    providerId?: string
    model?: string
    reasoning?: Partial<AgentProviderPreference['reasoning']>
    speed?: ModelPreferenceSpeed
    contextWindow?: Partial<AgentProviderPreference['contextWindow']>
  } = {}
  let providerPreferenceHasRemovedOutput = false
  for (const rawLine of front.split('\n')) {
    if (!rawLine.trim()) continue
    const indent = rawLine.length - rawLine.replace(/^\s+/, '').length
    const line = rawLine.trim()
    const ci = line.indexOf(':')
    if (ci < 0) continue
    const key = line.slice(0, ci).trim()
    const val = line.slice(ci + 1).trim()
    if (indent === 0) {
      section = null
      sub = null
      if (key === 'name') draft.name = val
      else if (key === 'description') draft.description = val
      else if (key === 'avatar') {
        draft.avatar = parsePackedAvatar(val)
      } else if (key === 'providerPreference' || key === 'tools' || key === 'mcp' || key === 'skills' || key === 'permissions') section = key
    } else if (indent === 2) {
      sub = null
      if (section === 'providerPreference' && key === 'providerId') providerPreference.providerId = val
      else if (section === 'providerPreference' && key === 'model') providerPreference.model = val
      else if (section === 'providerPreference' && key === 'reasoning') {
        providerPreference.reasoning = {}
        sub = 'providerReasoning'
      } else if (section === 'providerPreference' && key === 'speed') {
        providerPreference.speed = val as ModelPreferenceSpeed
      } else if (section === 'providerPreference' && key === 'contextWindow') {
        providerPreference.contextWindow = {}
        sub = 'providerContextWindow'
      } else if (section === 'tools' && key === 'allow') {
        hasToolsAllow = true
        draft.tools.allow = parseList(val)
      } else if (section === 'tools' && key === 'deny') {
        hasToolsDeny = true
        draft.tools.deny = parseList(val)
      }
      else if (section === 'tools' && key === 'agentControl') draft.tools.agentControl = (val || 'full') as AgentControl
      else if (section === 'mcp' && key === 'servers') draft.mcp.servers = parseList(val)
      else if (section === 'mcp' && key === 'tools') sub = 'mcpTools'
      else if (section === 'skills' && key === 'preload') draft.skills.preload = parseList(val)
      else if (section === 'skills' && key === 'allow') draft.skills.allow = parseList(val)
      else if (section === 'skills' && key === 'deny') draft.skills.deny = parseList(val)
      else if (section === 'permissions' && key === 'approvalPolicy') draft.permissions.approvalPolicy = (val || 'default') as ApprovalPolicy
      else if (section === 'permissions' && key === 'requireApprovalOutsideWorkspace') draft.permissions.requireApprovalOutsideWorkspace = val === 'true'
    } else if (indent >= 4) {
      if (sub === 'providerReasoning') {
        if (key === 'enabled' && (val === 'true' || val === 'false')) {
          providerPreference.reasoning!.enabled = val === 'true'
        }
        else if (key === 'effort') providerPreference.reasoning!.effort = val as ModelPreferenceReasoningEffort
        else if (key === 'output') providerPreferenceHasRemovedOutput = true
      } else if (sub === 'providerContextWindow' && key === 'mode') {
        providerPreference.contextWindow!.mode = val as ModelPreferenceContextMode
      } else if (sub === 'mcpTools') {
        if (key === 'allow') draft.mcp.toolsAllow = parseList(val)
        else if (key === 'deny') draft.mcp.toolsDeny = parseList(val)
      }
    }
  }
  draft.tools.mode = hasToolsAllow ? 'allowList' : hasToolsDeny ? 'denyList' : 'all'
  if (
    !providerPreferenceHasRemovedOutput
    && providerPreference.providerId
    && providerPreference.model
    && typeof providerPreference.reasoning?.enabled === 'boolean'
    && ['low', 'medium', 'high', 'extraHigh'].includes(providerPreference.reasoning.effort ?? '')
    && ['standard', 'fast'].includes(providerPreference.speed ?? '')
    && ['default', 'max'].includes(providerPreference.contextWindow?.mode ?? '')
  ) {
    draft.providerPreference = providerPreference as AgentProviderPreference
  }
  return draft
}

function yamlList(values: string[]): string {
  return values.length === 0 ? '[]' : `[${values.join(', ')}]`
}

/** Render the draft as the raw Markdown an agent/profiles/upsert would persist. */
export function toMarkdown(draft: ProfileDraft): string {
  const fm: string[] = ['---']
  fm.push(`name: ${draft.name || 'untitled-agent'}`)
  fm.push(`description: ${draft.description || ''}`)
  if (draft.avatar) {
    fm.push(`avatar: ${encodeAvatar(draft.avatar)}`)
  }
  if (draft.providerPreference) {
    const preference = draft.providerPreference
    fm.push('providerPreference:')
    fm.push(`  providerId: ${preference.providerId}`)
    fm.push(`  model: ${preference.model}`)
    fm.push('  reasoning:')
    fm.push(`    enabled: ${preference.reasoning.enabled ? 'true' : 'false'}`)
    fm.push(`    effort: ${preference.reasoning.effort}`)
    fm.push(`  speed: ${preference.speed}`)
    fm.push('  contextWindow:')
    fm.push(`    mode: ${preference.contextWindow.mode}`)
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
  fm.push(`  requireApprovalOutsideWorkspace: ${draft.permissions.requireApprovalOutsideWorkspace ? 'true' : 'false'}`)
  fm.push('---')

  return `${fm.join('\n')}\n\n${draft.roleInstructions.trim()}\n`
}
