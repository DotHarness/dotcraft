/**
 * Agent Builder — live draft synchronization from the conversational builder.
 *
 * The conversational profile-builder agent (specs/agents/agent-profiles.md §12A) edits a profile by
 * calling fine-grained tools whose results are compact change descriptors, NOT the whole document:
 *   { ok: true, field: "tools.allow", change: { op: "add", values: [...], rejected: [...], list: [...] } }
 * This module reads one such result off the streamed tool-call output and applies it to the local
 * `ProfileDraft` — so the structured editor (the left pane) updates field-by-field as the agent works,
 * without re-fetching Markdown. It also reports which field changed, to drive the cursor-on-field
 * highlight (the "agent is editing this" affordance). Pure and synchronous: no I/O, no React.
 */

import type { AgentControl, ApprovalPolicy, ProfileDraft } from './agentProfileDraft'

/** Field paths the builder tools report (and the editor highlights). Mirrors the backend `field` values. */
export type BuilderField =
  | 'name'
  | 'description'
  | 'instructions'
  | 'tools.allow'
  | 'tools.agentControl'
  | 'skills.preload'
  | 'mcp.servers'
  | 'model'
  | 'approval'

/** The `change` payload a builder tool returns. Scalar edits use `value`; list edits carry the full `list`. */
export interface BuilderToolChange {
  op: 'set' | 'add' | 'remove' | 'append'
  value?: string | null
  values?: string[] | null
  rejected?: string[] | null
  list?: string[] | null
}

/** A builder tool's parsed result. `ok:false` carries an `error` (e.g. a validation rejection). */
export interface BuilderToolResult {
  ok: boolean
  field?: BuilderField
  change?: BuilderToolChange
  error?: string
}

/**
 * The PascalCase builder tool names (must match AgentProfileBuilderToolProvider). A streamed tool call
 * whose name is in this set is a builder edit whose result should flow through {@link applyBuilderChange}.
 */
export const BUILDER_TOOL_NAMES: ReadonlySet<string> = new Set([
  'SetAgentName',
  'SetAgentDescription',
  'SetAgentInstructions',
  'AppendAgentInstructions',
  'AddAgentTools',
  'RemoveAgentTools',
  'SetAgentToolControl',
  'AddAgentSkills',
  'RemoveAgentSkills',
  'AddAgentMcpServers',
  'RemoveAgentMcpServers',
  'SetAgentModel',
  'SetAgentApproval'
])

export function isBuilderToolName(name: string | null | undefined): boolean {
  return !!name && BUILDER_TOOL_NAMES.has(name)
}

/** Safely parse a builder tool result that may arrive as a JSON string or an already-parsed object. */
export function parseBuilderToolResult(raw: unknown): BuilderToolResult | null {
  let obj: unknown = raw
  if (typeof raw === 'string') {
    const trimmed = raw.trim()
    if (!trimmed) return null
    try {
      obj = JSON.parse(trimmed)
    } catch {
      return null
    }
  }
  if (!obj || typeof obj !== 'object') return null
  const r = obj as Record<string, unknown>
  if (typeof r.ok !== 'boolean') return null
  return {
    ok: r.ok,
    field: typeof r.field === 'string' ? (r.field as BuilderField) : undefined,
    change: isChange(r.change) ? (r.change as BuilderToolChange) : undefined,
    error: typeof r.error === 'string' ? r.error : undefined
  }
}

function isChange(value: unknown): boolean {
  return !!value && typeof value === 'object' && typeof (value as Record<string, unknown>).op === 'string'
}

/**
 * Apply one builder tool result to a draft, returning a NEW draft (for React) and the changed field
 * (null when the result is a rejection or carries no usable change). Unknown fields are ignored.
 */
export function applyBuilderChange(
  draft: ProfileDraft,
  result: BuilderToolResult | null
): { draft: ProfileDraft; changedField: BuilderField | null } {
  if (!result || !result.ok || !result.field) return { draft, changedField: null }

  const ch = result.change
  const field = result.field
  const next: ProfileDraft = { ...draft }

  switch (field) {
    case 'name':
      next.name = ch?.value ?? draft.name
      break
    case 'description':
      next.description = ch?.value ?? ''
      break
    case 'instructions':
      next.roleInstructions = ch?.value ?? draft.roleInstructions
      break
    case 'model':
      next.model = ch?.value ?? draft.model
      break
    case 'tools.agentControl':
      next.tools = { ...draft.tools, agentControl: (ch?.value as AgentControl) ?? draft.tools.agentControl }
      break
    case 'tools.allow':
      next.tools = { ...draft.tools, allow: ch?.list ?? draft.tools.allow }
      break
    case 'skills.preload':
      next.skills = { ...draft.skills, preload: ch?.list ?? draft.skills.preload }
      break
    case 'mcp.servers':
      next.mcp = { ...draft.mcp, servers: ch?.list ?? draft.mcp.servers }
      break
    case 'approval':
      next.permissions = {
        ...draft.permissions,
        approvalPolicy: (ch?.value as ApprovalPolicy) ?? draft.permissions.approvalPolicy
      }
      break
    default:
      return { draft, changedField: null }
  }

  return { draft: next, changedField: field }
}
