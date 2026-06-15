import { describe, expect, it } from 'vitest'
import { createEmptyDraft } from '../components/agents/agentProfileDraft'
import {
  applyBuilderChange,
  builderFieldForToolName,
  isBuilderToolName,
  parseBuilderToolResult
} from '../components/agents/agentBuilderDraftSync'

describe('agentBuilderDraftSync', () => {
  it('recognizes builder tool names', () => {
    expect(isBuilderToolName('SetAgentName')).toBe(true)
    expect(isBuilderToolName('AddAgentTools')).toBe(true)
    expect(isBuilderToolName('ReadFile')).toBe(false)
    expect(isBuilderToolName(undefined)).toBe(false)
  })

  it('maps builder tool names to edited fields', () => {
    expect(builderFieldForToolName('SetAgentName')).toBe('name')
    expect(builderFieldForToolName('AppendAgentInstructions')).toBe('instructions')
    expect(builderFieldForToolName('AddAgentTools')).toBe('tools.allow')
    expect(builderFieldForToolName('SetAgentApproval')).toBe('approval')
    expect(builderFieldForToolName('ReadFile')).toBeNull()
  })

  it('parses a JSON-string result and rejects malformed input', () => {
    const ok = parseBuilderToolResult('{"ok":true,"field":"name","change":{"op":"set","value":"triage"}}')
    expect(ok).toEqual({ ok: true, field: 'name', change: { op: 'set', value: 'triage' } })

    expect(parseBuilderToolResult('not json')).toBeNull()
    expect(parseBuilderToolResult('')).toBeNull()
    expect(parseBuilderToolResult({ missingOk: true })).toBeNull()
  })

  it('parses an already-parsed object', () => {
    const r = parseBuilderToolResult({ ok: false, field: 'approval', error: 'bad' })
    expect(r).toEqual({ ok: false, field: 'approval', error: 'bad' })
  })

  it('applies a scalar set (name / description / model)', () => {
    let draft = createEmptyDraft()
    ;({ draft } = applyBuilderChange(draft, { ok: true, field: 'name', change: { op: 'set', value: 'triage-bot' } }))
    ;({ draft } = applyBuilderChange(draft, {
      ok: true,
      field: 'description',
      change: { op: 'set', value: 'Triages issues' }
    }))
    const res = applyBuilderChange(draft, { ok: true, field: 'model', change: { op: 'set', value: 'claude-opus-4-8' } })

    expect(res.draft.name).toBe('triage-bot')
    expect(res.draft.description).toBe('Triages issues')
    expect(res.draft.model).toBe('claude-opus-4-8')
    expect(res.changedField).toBe('model')
  })

  it('applies a list change using the authoritative full list', () => {
    const draft = createEmptyDraft()
    const res = applyBuilderChange(draft, {
      ok: true,
      field: 'tools.allow',
      change: { op: 'add', values: ['ReadFile'], rejected: ['Nope'], list: ['ReadFile'] }
    })

    expect(res.draft.tools.allow).toEqual(['ReadFile'])
    expect(res.changedField).toBe('tools.allow')
    // Original draft is not mutated (immutability for React).
    expect(draft.tools.allow).toEqual([])
  })

  it('applies the instructions body carried in the change value', () => {
    const draft = createEmptyDraft()
    const res = applyBuilderChange(draft, {
      ok: true,
      field: 'instructions',
      change: { op: 'append', value: 'First.\n\nSecond.' }
    })

    expect(res.draft.roleInstructions).toBe('First.\n\nSecond.')
    expect(res.changedField).toBe('instructions')
  })

  it('applies agentControl and approval enum values', () => {
    let draft = createEmptyDraft()
    ;({ draft } = applyBuilderChange(draft, {
      ok: true,
      field: 'tools.agentControl',
      change: { op: 'set', value: 'allowList' }
    }))
    const res = applyBuilderChange(draft, { ok: true, field: 'approval', change: { op: 'set', value: 'interrupt' } })

    expect(res.draft.tools.agentControl).toBe('allowList')
    expect(res.draft.permissions.approvalPolicy).toBe('interrupt')
  })

  it('ignores rejections and unknown fields', () => {
    const draft = createEmptyDraft()
    expect(applyBuilderChange(draft, { ok: false, field: 'name', error: 'x' }).changedField).toBeNull()
    expect(applyBuilderChange(draft, { ok: true, field: 'bogus' as never }).changedField).toBeNull()
    expect(applyBuilderChange(draft, null).changedField).toBeNull()
  })
})
