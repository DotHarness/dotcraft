import { describe, expect, it } from 'vitest'

import {
  parseSettingsDefinition,
  parseSettingsInput,
  readSettingsTools,
  settingsGroups
} from '../components/plugins/settings/mcpSettingsModel'

const declaration = { readTool: 'settings_read', updateTool: 'settings_update' }

const definition = {
  schema: {
    type: 'object',
    properties: {
      account: { type: 'string', minLength: 1 },
      apiKey: { type: 'string', writeOnly: true, minLength: 1 },
      region: { type: 'string', enum: ['east', 'west'] },
      maxRows: { type: 'integer', minimum: 1, maximum: 5000 }
    }
  },
  values: { account: 'reader', apiKey: '', region: 'east', maxRows: 1000 },
  layout: [{ title: 'Connection', items: [{ kind: 'property', property: 'account' }, { kind: 'property', property: 'apiKey' }] }]
}

describe('MCP settings model', () => {
  it('reads the declaration from extensions or experimental capabilities', () => {
    expect(readSettingsTools({ extensions: { 'dotcraft/settings': declaration } })).toEqual(declaration)
    expect(readSettingsTools({ experimental: { 'dotcraft/settings': declaration } })).toEqual(declaration)
    expect(readSettingsTools({ extensions: { 'dotcraft/settings': { readTool: 'same', updateTool: 'same' } } })).toBeNull()
  })

  it('accepts an empty write-only value and rejects the definition when any value is invalid', () => {
    expect(parseSettingsDefinition(definition)?.values.apiKey).toBe('')
    expect(parseSettingsDefinition({ ...definition, values: { ...definition.values, maxRows: 9000 } })).toBeNull()
    expect(parseSettingsDefinition({ ...definition, values: { ...definition.values, region: 'north' } })).toBeNull()
  })

  it('groups properties the layout leaves out after the declared groups', () => {
    const groups = settingsGroups(parseSettingsDefinition(definition)!)
    expect(groups.map((group) => [group.title, group.other, group.items.length])).toEqual([
      ['Connection', false, 2],
      [undefined, true, 2]
    ])
  })

  it('validates typed numbers before they are sent', () => {
    const maxRows = parseSettingsDefinition(definition)!.properties.maxRows
    expect(parseSettingsInput(maxRows, '250')).toEqual({ value: 250 })
    expect(parseSettingsInput(maxRows, '2.5')).toEqual({ error: 'integer' })
    expect(parseSettingsInput(maxRows, '9000')).toEqual({ error: 'range' })
  })
})
