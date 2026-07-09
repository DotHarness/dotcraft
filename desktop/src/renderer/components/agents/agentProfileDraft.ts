/**
 * Agent Builder — editable draft model.
 *
 * A `ProfileDraft` is the in-memory shape the editor manipulates. It mirrors the
 * Agent Profile frontmatter from specs/features/agent-profiles.md (name, description,
 * avatar, model, reasoning, mode, tools, mcp, skills, permissions) plus the Markdown body
 * (`roleInstructions`). `toMarkdown` renders the draft as the raw Markdown the
 * `agent/profiles/upsert` write format expects; `parseProfile` reads it back.
 */

import { decodeAvatar, encodeAvatar, type AvatarSpec } from './agentAvatar'

export type SaveTarget = 'user' | 'workspace'
export type ReasoningEffort = 'minimal' | 'low' | 'medium' | 'high'
export type AgentControl = 'full' | 'disabled' | 'allowList'
export type ApprovalPolicy = 'default' | 'autoApprove' | 'interrupt'
export type ModelId = 'inherit' | 'claude-opus-4-8' | 'claude-sonnet-4-6' | 'claude-haiku-4-5'

// Operational mode (Agent/Plan) is intentionally NOT a profile field: it is a per-thread
// runtime posture, and a profile already expresses its capability scope through tools/mcp/skills
// allow-deny + approval policy. See specs/features/agent-profiles.md and the Plan/Custom composer pills.

export interface ProfileDraft {
  name: string
  description: string
  avatar?: AvatarSpec
  model: string
  reasoningEffort: ReasoningEffort
  tools: {
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

export const MODELS: { value: string; label: string }[] = [
  { value: 'inherit', label: 'Inherit (thread default)' },
  { value: 'claude-opus-4-8', label: 'Opus 4.8' },
  { value: 'claude-sonnet-4-6', label: 'Sonnet 4.6' },
  { value: 'claude-haiku-4-5', label: 'Haiku 4.5' }
]

export const REASONING_OPTIONS: { value: ReasoningEffort; label: string }[] = [
  { value: 'minimal', label: 'Minimal' },
  { value: 'low', label: 'Low' },
  { value: 'medium', label: 'Medium' },
  { value: 'high', label: 'High' }
]

export const APPROVAL_OPTIONS: { value: ApprovalPolicy; label: string }[] = [
  { value: 'default', label: 'Default' },
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
    model: 'inherit',
    reasoningEffort: 'medium',
    tools: { allow: [], deny: [], agentControl: 'full' },
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
      else if (key === 'model') draft.model = val || 'inherit'
      else if (key === 'avatar') {
        draft.avatar = parsePackedAvatar(val)
      } else if (key === 'reasoning' || key === 'tools' || key === 'mcp' || key === 'skills' || key === 'permissions') section = key
    } else if (indent === 2) {
      sub = null
      if (section === 'reasoning' && key === 'effort') draft.reasoningEffort = (val || 'medium') as ReasoningEffort
      else if (section === 'tools' && key === 'allow') draft.tools.allow = parseList(val)
      else if (section === 'tools' && key === 'deny') draft.tools.deny = parseList(val)
      else if (section === 'tools' && key === 'agentControl') draft.tools.agentControl = (val || 'full') as AgentControl
      else if (section === 'mcp' && key === 'servers') draft.mcp.servers = parseList(val)
      else if (section === 'mcp' && key === 'tools') sub = 'mcpTools'
      else if (section === 'skills' && key === 'preload') draft.skills.preload = parseList(val)
      else if (section === 'skills' && key === 'allow') draft.skills.allow = parseList(val)
      else if (section === 'skills' && key === 'deny') draft.skills.deny = parseList(val)
      else if (section === 'permissions' && key === 'approvalPolicy') draft.permissions.approvalPolicy = (val || 'default') as ApprovalPolicy
      else if (section === 'permissions' && key === 'requireApprovalOutsideWorkspace') draft.permissions.requireApprovalOutsideWorkspace = val === 'true'
    } else if (indent >= 4 && sub === 'mcpTools') {
      if (key === 'allow') draft.mcp.toolsAllow = parseList(val)
      else if (key === 'deny') draft.mcp.toolsDeny = parseList(val)
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
  fm.push(`name: ${draft.name || 'untitled-agent'}`)
  fm.push(`description: ${draft.description || ''}`)
  if (draft.avatar) {
    fm.push(`avatar: ${encodeAvatar(draft.avatar)}`)
  }
  fm.push(`model: ${draft.model || 'inherit'}`)
  if (draft.reasoningEffort && draft.reasoningEffort !== 'medium') {
    fm.push('reasoning:')
    fm.push(`  effort: ${draft.reasoningEffort}`)
  }

  if (draft.tools.allow.length || draft.tools.deny.length || draft.tools.agentControl !== 'full') {
    fm.push('tools:')
    if (draft.tools.allow.length) fm.push(`  allow: ${yamlList(draft.tools.allow)}`)
    if (draft.tools.deny.length) fm.push(`  deny: ${yamlList(draft.tools.deny)}`)
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
