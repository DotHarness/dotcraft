import { describe, expect, it } from 'vitest'

import type { EnvironmentDocument } from '../../shared/projectActions'
import {
  documentFromDraft,
  draftFromDocument,
  isDraftDirty,
  newActionDraft,
  saveBlockReason,
  type EnvironmentDraft
} from '../components/settings/panels/worktrees/environmentDraft'

const DOCUMENT: EnvironmentDocument = {
  name: 'web',
  setup: { default: 'npm install', win32: 'npm install --no-fund' },
  cleanup: { default: '' },
  actions: [{ name: 'Dev', icon: 'run', command: 'npm run dev' }]
}

function reason(draft: EnvironmentDraft, baseline: EnvironmentDraft, options: { alwaysChanged?: boolean; saving?: boolean; readable?: boolean } = {}) {
  return saveBlockReason({
    saving: options.saving ?? false,
    readable: options.readable ?? true,
    draft,
    dirty: isDraftDirty(draft, baseline, options.alwaysChanged ?? false)
  })
}

describe('environment draft', () => {
  it('round-trips a document and drops empty overrides and blank actions', () => {
    const draft = draftFromDocument(DOCUMENT, 'fallback')
    draft.actions.push(newActionDraft())

    expect(documentFromDraft(draft)).toEqual(DOCUMENT)
  })

  it('names a new environment after the fallback and an unnamed file after its fallback', () => {
    expect(draftFromDocument(null, 'dotcraft').name).toBe('dotcraft')
    expect(draftFromDocument({ ...DOCUMENT, name: '' }, 'environment-2').name).toBe('environment-2')
  })

  it('needs a change before an existing file can be saved', () => {
    const baseline = draftFromDocument(DOCUMENT, 'fallback')

    expect(reason(baseline, baseline)).toBe('unchanged')
    expect(reason({ ...baseline, setup: { ...baseline.setup, default: '  npm install \n' } }, baseline)).toBe('unchanged')
    expect(reason({ ...baseline, setup: { ...baseline.setup, linux: 'make' } }, baseline)).toBeNull()
  })

  it('treats a new or invalid file as changed', () => {
    const baseline = draftFromDocument(null, 'dotcraft')

    expect(reason(baseline, baseline, { alwaysChanged: true })).toBeNull()
  })

  it('blocks saving without a name or with an incomplete action', () => {
    const baseline = draftFromDocument(DOCUMENT, 'fallback')
    const incomplete = { ...newActionDraft(), name: 'Test' }

    expect(reason({ ...baseline, name: '  ' }, baseline)).toBe('name')
    expect(reason({ ...baseline, actions: [...baseline.actions, incomplete] }, baseline)).toBe('action')
    expect(reason({ ...baseline, actions: [...baseline.actions, newActionDraft()] }, baseline)).toBe('unchanged')
  })

  it('reports saving and unreadable files before content problems', () => {
    const baseline = draftFromDocument(DOCUMENT, 'fallback')
    const nameless = { ...baseline, name: '' }

    expect(reason(nameless, baseline, { saving: true })).toBe('saving')
    expect(reason(nameless, baseline, { readable: false })).toBe('unreadable')
  })
})
