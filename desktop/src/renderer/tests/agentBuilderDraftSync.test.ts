import { describe, expect, it } from 'vitest'
import { createEmptyDraft } from '../components/agents/agentProfileDraft'
import {
  applyBuilderChange,
  builderFieldForToolName,
  isBuilderToolName,
  parseBuilderToolResult
} from '../components/agents/agentBuilderDraftSync'

describe('agentBuilderDraftSync', () => {
  it('recognizes builder tool names and maps them to edited fields', () => {
    expect(isBuilderToolName('SetAgentToolPolicy')).toBe(true)
    expect(isBuilderToolName('ReadFile')).toBe(false)
    expect(isBuilderToolName(undefined)).toBe(false)

    expect(builderFieldForToolName('SetAgentName')).toBe('name')
    expect(builderFieldForToolName('AppendAgentInstructions')).toBe('instructions')
    expect(builderFieldForToolName('SetAgentToolPolicy')).toBe('tools.policy')
    expect(builderFieldForToolName('SetAgentProviderPreference')).toBe('providerPreference')
    expect(builderFieldForToolName('ClearAgentProviderPreference')).toBe('providerPreference')
    expect(builderFieldForToolName('SetAgentApproval')).toBe('approval')
    expect(builderFieldForToolName('ReadFile')).toBeNull()
  })

  it('parses JSON-string and object results and rejects malformed input', () => {
    const ok = parseBuilderToolResult('{"ok":true,"field":"name","change":{"op":"set","value":"triage"}}')
    expect(ok).toEqual({ ok: true, field: 'name', change: { op: 'set', value: 'triage' } })
    expect(parseBuilderToolResult({ ok: false, field: 'approval', error: 'bad' }))
      .toEqual({ ok: false, field: 'approval', error: 'bad' })

    expect(parseBuilderToolResult('not json')).toBeNull()
    expect(parseBuilderToolResult('')).toBeNull()
    expect(parseBuilderToolResult({ missingOk: true })).toBeNull()
  })

  it('applies text, instructions and enum changes', () => {
    let draft = createEmptyDraft()
    ;({ draft } = applyBuilderChange(draft, { ok: true, field: 'name', change: { op: 'set', value: 'triage-bot' } }))
    ;({ draft } = applyBuilderChange(draft, {
      ok: true,
      field: 'description',
      change: { op: 'set', value: 'Triages issues' }
    }))
    ;({ draft } = applyBuilderChange(draft, {
      ok: true,
      field: 'tools.agentControl',
      change: { op: 'set', value: 'allowList' }
    }))
    ;({ draft } = applyBuilderChange(draft, { ok: true, field: 'approval', change: { op: 'set', value: 'autoApprove' } }))
    const res = applyBuilderChange(draft, {
      ok: true,
      field: 'instructions',
      change: { op: 'append', value: 'First.\n\nSecond.' }
    })

    expect(res.changedField).toBe('instructions')
    expect(res.draft).toMatchObject({
      name: 'triage-bot',
      description: 'Triages issues',
      roleInstructions: 'First.\n\nSecond.',
      tools: { agentControl: 'allowList' },
      permissions: { approvalPolicy: 'autoApprove' }
    })
  })

  it('sets and clears the provider model preset', () => {
    const draft = createEmptyDraft()
    const providerPreference = {
      providerId: 'openai',
      model: 'gpt-5.6',
      reasoning: { enabled: true, effort: 'high' as const },
      speed: 'fast' as const,
    }
    const set = applyBuilderChange(draft, {
      ok: true,
      field: 'providerPreference',
      change: { op: 'set', providerPreference }
    })
    const clear = applyBuilderChange(set.draft, {
      ok: true,
      field: 'providerPreference',
      change: { op: 'remove' }
    })

    expect(set.draft.providerPreference).toEqual(providerPreference)
    expect(set.changedField).toBe('providerPreference')
    expect(clear.draft.providerPreference).toBeNull()
  })

  it('applies an atomic tool policy change using the authoritative mode and list', () => {
    const draft = createEmptyDraft()
    const res = applyBuilderChange(draft, {
      ok: true,
      field: 'tools.policy',
      change: { op: 'set', mode: 'denyList', list: ['WriteFile'] }
    })

    expect(res.draft.tools.mode).toBe('denyList')
    expect(res.draft.tools.allow).toEqual([])
    expect(res.draft.tools.deny).toEqual(['WriteFile'])
    expect(res.changedField).toBe('tools.policy')
    expect(draft.tools.allow).toEqual([])
  })

  it('ignores rejections and unknown fields', () => {
    const draft = createEmptyDraft()
    expect(applyBuilderChange(draft, { ok: false, field: 'name', error: 'x' }).changedField).toBeNull()
    expect(applyBuilderChange(draft, { ok: true, field: 'bogus' as never }).changedField).toBeNull()
    expect(applyBuilderChange(draft, null).changedField).toBeNull()
  })
})
