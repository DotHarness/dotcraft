import { describe, expect, it } from 'vitest'
import {
  collectVisualizationDesignEdits,
  readVisualizationDesignControls,
  reconcileVisualizationDesignValues
} from '../components/conversation/visualizationDesignControls'

const slider = (id: string, initial = 18) => ({ id, label: 'Corner radius', reference: 'player.radius', kind: 'slider', min: 0, max: 40, step: 1, unit: 'px', initial })

describe('visualization design controls from a view', () => {
  it('keeps only valid controls within the host limits', () => {
    const controls = readVisualizationDesignControls({
      variant: 'Minimal',
      groups: [
        {
          id: 'tweak-1',
          label: 'Minimal player',
          variant: 'Minimal',
          visible: true,
          controls: [
            ...Array.from({ length: 14 }, (_, index) => slider(`tweak-1.${index}`)),
            { id: 'bad', label: 'Bad', kind: 'slider', min: 4, max: 2, step: 1, initial: 3 }
          ]
        },
        {
          id: 'tweak-2',
          label: 'Shared',
          variant: null,
          visible: true,
          controls: [
            { id: 'tweak-2.0', label: 'Accent', reference: 'has space', kind: 'color', initial: '#7c3aed' },
            { id: 'tweak-2.1', label: 'Tone', kind: 'select', initial: 'missing', options: [{ label: 'Calm', value: 'calm' }] },
            { id: 'tweak-2.2', label: 'Script', kind: 'script', initial: 'x' }
          ]
        }
      ]
    })!

    expect(controls.variant).toBe('Minimal')
    expect(controls.groups[0].controls).toHaveLength(12)
    expect(controls.groups[1].controls).toEqual([{ id: 'tweak-2.0', label: 'Accent', kind: 'color', initial: '#7c3aed' }])
    expect(readVisualizationDesignControls({ groups: 'nope' })).toBeNull()
  })

  it('keeps edits across snapshots and reports only changed controls', () => {
    const controls = readVisualizationDesignControls({
      variant: 'Editorial',
      groups: [
        { id: 'tweak-1', label: 'Minimal player', variant: 'Minimal', visible: false, controls: [slider('tweak-1.0')] },
        { id: 'tweak-2', label: 'Shared', variant: null, visible: true, controls: [
          { id: 'tweak-2.0', label: 'Dense', kind: 'toggle', initial: false },
          { id: 'tweak-2.1', label: 'Accent', reference: '--accent', kind: 'color', initial: '#7c3aed' }
        ] }
      ]
    })!
    const values = reconcileVisualizationDesignValues(controls, { 'tweak-1.0': 24, 'tweak-2.0': 'yes', gone: 1 })

    expect(values).toEqual({ 'tweak-1.0': 24, 'tweak-2.0': false, 'tweak-2.1': '#7c3aed' })
    expect(collectVisualizationDesignEdits(controls, { ...values, 'tweak-2.1': '#000000' })).toEqual([
      { group: 'Minimal player', label: 'Corner radius', reference: 'player.radius', from: 18, to: 24 },
      { group: 'Shared', label: 'Accent', reference: '--accent', from: '#7c3aed', to: '#000000' }
    ])
  })
})
