/** Builder tools return per-field change descriptors (specs/features/agent-profiles.md §12A); applying them locally keeps the editor in sync without re-fetching Markdown. */

import type { AgentControl, AgentProviderPreference, ApprovalPolicy, ProfileDraft, ToolPolicyMode } from './agentProfileDraft'

/** Field paths the builder tools report (and the editor marks). Mirrors the backend `field` values. */
export type BuilderField =
  | 'name'
  | 'description'
  | 'instructions'
  | 'tools.policy'
  | 'tools.agentControl'
  | 'skills.preload'
  | 'mcp.servers'
  | 'providerPreference'
  | 'approval'

/** The `change` payload a builder tool returns. Scalar edits use `value`; list edits carry the full `list`. */
export interface BuilderToolChange {
  op: 'set' | 'add' | 'remove' | 'append'
  value?: string | null
  values?: string[] | null
  rejected?: string[] | null
  list?: string[] | null
  mode?: ToolPolicyMode | null
  providerPreference?: AgentProviderPreference | null
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
const BUILDER_TOOL_FIELDS: ReadonlyMap<string, BuilderField> = new Map([
  ['SetAgentName', 'name'],
  ['SetAgentDescription', 'description'],
  ['SetAgentInstructions', 'instructions'],
  ['AppendAgentInstructions', 'instructions'],
  ['SetAgentToolPolicy', 'tools.policy'],
  ['SetAgentToolControl', 'tools.agentControl'],
  ['AddAgentSkills', 'skills.preload'],
  ['RemoveAgentSkills', 'skills.preload'],
  ['AddAgentMcpServers', 'mcp.servers'],
  ['RemoveAgentMcpServers', 'mcp.servers'],
  ['SetAgentProviderPreference', 'providerPreference'],
  ['ClearAgentProviderPreference', 'providerPreference'],
  ['SetAgentApproval', 'approval']
])

export const BUILDER_TOOL_NAMES: ReadonlySet<string> = new Set(BUILDER_TOOL_FIELDS.keys())

export const BUILDER_FIELD_LABEL_KEYS: Record<BuilderField, string> = {
  name: 'agentBuilder.field.name',
  description: 'agentBuilder.field.description',
  instructions: 'agentBuilder.field.instructions',
  'tools.policy': 'agentBuilder.field.tools',
  'tools.agentControl': 'agentBuilder.field.toolControl',
  'skills.preload': 'agentBuilder.field.skills',
  'mcp.servers': 'agentBuilder.field.mcp',
  providerPreference: 'agentBuilder.field.model',
  approval: 'agentBuilder.field.approval'
}

const BUILDER_FIELDS: ReadonlySet<BuilderField> = new Set(BUILDER_TOOL_FIELDS.values())

export function builderFieldForToolName(name: string | null | undefined): BuilderField | null {
  return name ? (BUILDER_TOOL_FIELDS.get(name) ?? null) : null
}

export function isBuilderToolName(name: string | null | undefined): boolean {
  return builderFieldForToolName(name) !== null
}

export function isBuilderField(value: string | null | undefined): value is BuilderField {
  return !!value && BUILDER_FIELDS.has(value as BuilderField)
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
    case 'providerPreference':
      next.providerPreference = ch?.op === 'remove'
        ? null
        : ch?.providerPreference ?? draft.providerPreference
      break
    case 'tools.agentControl':
      next.tools = { ...draft.tools, agentControl: (ch?.value as AgentControl) ?? draft.tools.agentControl }
      break
    case 'tools.policy': {
      const mode = ch?.mode ?? draft.tools.mode
      const list = ch?.list ?? []
      next.tools = {
        ...draft.tools,
        mode,
        allow: mode === 'allowList' ? list : [],
        deny: mode === 'denyList' ? list : []
      }
      break
    }
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
