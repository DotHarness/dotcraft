import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { WriteEnvironmentResult } from '../../../../../shared/projectActions'
import {
  documentFromDraft,
  draftFromDocument,
  isDraftDirty,
  saveBlockReason,
  type EnvironmentDraft,
  type SaveBlockReason
} from './environmentDraft'

export type EnvironmentLoadState =
  | { status: 'loading' }
  | { status: 'unreadable' }
  | { status: 'ready'; revision: string | null; error?: string }

export type EnvironmentSaveState = 'idle' | 'saving' | 'conflict' | 'failed'

export interface EnvironmentDraftController {
  load: EnvironmentLoadState
  draft: EnvironmentDraft
  update(change: (draft: EnvironmentDraft) => EnvironmentDraft): void
  dirty: boolean
  blockReason: SaveBlockReason | null
  saveState: EnvironmentSaveState
  save(): Promise<Extract<WriteEnvironmentResult, { ok: true }> | null>
  reload(): Promise<void>
}

const EMPTY_DRAFT = draftFromDocument(null, '')

export function useEnvironmentDraft(projectPath: string, path: string, fallbackName: string): EnvironmentDraftController {
  const [load, setLoad] = useState<EnvironmentLoadState>({ status: 'loading' })
  const [baseline, setBaseline] = useState<EnvironmentDraft>(EMPTY_DRAFT)
  const [draft, setDraft] = useState<EnvironmentDraft>(EMPTY_DRAFT)
  const [saveState, setSaveState] = useState<EnvironmentSaveState>('idle')
  const request = useRef(0)
  const fallbackNameRef = useRef(fallbackName)
  fallbackNameRef.current = fallbackName

  const reload = useCallback(async (): Promise<void> => {
    const id = ++request.current
    setLoad({ status: 'loading' })
    setSaveState('idle')
    let result: Awaited<ReturnType<typeof window.api.projectActions.readEnvironment>>
    try {
      result = await window.api.projectActions.readEnvironment({ projectPath, path })
    } catch {
      result = { ok: false, reason: 'failed' }
    }
    if (id !== request.current) return
    if (!result.ok) {
      setLoad({ status: 'unreadable' })
      setBaseline(EMPTY_DRAFT)
      setDraft(EMPTY_DRAFT)
      return
    }
    const loaded = draftFromDocument(result.document, fallbackNameRef.current)
    setBaseline(loaded)
    setDraft(loaded)
    setLoad({ status: 'ready', revision: result.revision, ...(result.error ? { error: result.error } : {}) })
  }, [path, projectPath])

  useEffect(() => {
    void reload()
  }, [reload])

  const alwaysChanged = load.status === 'ready' && (load.revision === null || load.error !== undefined)
  const dirty = load.status === 'ready' && isDraftDirty(draft, baseline, alwaysChanged)
  const blockReason = useMemo(() => saveBlockReason({
    saving: saveState === 'saving',
    readable: load.status === 'ready',
    draft,
    dirty
  }), [dirty, draft, load.status, saveState])

  const update = useCallback((change: (current: EnvironmentDraft) => EnvironmentDraft) => {
    setDraft((current) => change(current))
  }, [])

  const save = useCallback(async (): Promise<Extract<WriteEnvironmentResult, { ok: true }> | null> => {
    if (load.status !== 'ready' || blockReason) return null
    setSaveState('saving')
    const submitted = draft
    let result: WriteEnvironmentResult
    try {
      result = await window.api.projectActions.writeEnvironment({
        projectPath,
        path,
        revision: load.revision,
        document: documentFromDraft(submitted)
      })
    } catch {
      result = { ok: false, reason: 'failed' }
    }
    if (!result.ok) {
      setSaveState(result.reason === 'conflict' ? 'conflict' : 'failed')
      return null
    }
    setBaseline(submitted)
    setLoad({ status: 'ready', revision: result.revision })
    setSaveState('idle')
    return result
  }, [blockReason, draft, load, path, projectPath])

  return { load, draft, update, dirty, blockReason, saveState, save, reload }
}
