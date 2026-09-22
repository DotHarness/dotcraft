import { create } from 'zustand'
import type { EditorState } from '@codemirror/state'
import type { ReadTextResult } from '../../shared/viewer/types'
import { normalizeLocale, translate } from '../../shared/locales'
import { showToast } from './toastStore'

export type FileEditorMode = 'preview' | 'source'
export type FileEditorSaveState = 'idle' | 'saving' | 'failed' | 'conflict'
export interface EditorSnapshot {
  version: number
  state: EditorState
  scrollTop: number
  scrollLeft: number
}
export interface FileReview {
  disk: ReadTextResult
  oldText: string
  newText: string
}
export interface FileEditorSession {
  tabId: string
  absolutePath: string
  markdown: boolean
  status: 'loading' | 'ready' | 'error'
  text: string
  baseText: string
  version: number
  mtimeMs: number
  sizeBytes: number
  hasUtf8Bom: boolean
  lineEnding: ReadTextResult['lineEnding']
  readOnlyReason?: ReadTextResult['readOnlyReason']
  error?: string
  mode: FileEditorMode
  saveState: FileEditorSaveState
  review?: FileReview
  reviewWrite?: boolean
  focusRevision: number
  snapshots: Partial<Record<FileEditorMode, EditorSnapshot>>
}
interface FileEditorStore {
  sessions: Map<string, FileEditorSession>
  load(tabId: string, absolutePath: string, markdown: boolean): Promise<void>
  updateText(tabId: string, text: string): void
  setSnapshot(tabId: string, mode: FileEditorMode, snapshot: EditorSnapshot): void
  switchMode(tabId: string, mode: FileEditorMode): Promise<boolean>
  save(tabId: string): Promise<boolean>
  refreshFromDisk(tabId: string): Promise<void>
  resolveReview(tabId: string, choice: 'accept' | 'reject' | 'edit'): Promise<boolean>
  discard(tabId: string): void
}
interface Runtime {
  tail: Promise<unknown>
  save?: Promise<boolean>
  refresh?: Promise<void>
  refreshAgain?: boolean
  timer?: ReturnType<typeof setTimeout>
  stop?: () => void
  disposed: boolean
}
const runtimes = new Map<string, Runtime>()
const normalize = (text: string) => text.replace(/\r\n?/g, '\n')

function patch(tabId: string, update: (session: FileEditorSession) => FileEditorSession): void {
  useFileEditorStore.setState((state) => {
    const current = state.sessions.get(tabId)
    if (!current) return state
    return { sessions: new Map(state.sessions).set(tabId, update(current)) }
  })
}
function enqueue<T>(runtime: Runtime, task: () => Promise<T>): Promise<T> {
  const result = runtime.tail.then(task, task)
  runtime.tail = result.catch(() => {})
  return result
}
function schedule(tabId: string): void {
  const runtime = runtimes.get(tabId)
  if (!runtime) return
  clearTimeout(runtime.timer)
  const session = useFileEditorStore.getState().sessions.get(tabId)
  if (
    !session ||
    session.review ||
    (session.readOnlyReason && !session.reviewWrite) ||
    session.text === session.baseText
  )
    return
  runtime.timer = setTimeout(() => {
    void useFileEditorStore.getState().save(tabId)
  }, 3000)
}
function metadata(disk: ReadTextResult) {
  return {
    mtimeMs: disk.mtimeMs,
    sizeBytes: disk.sizeBytes,
    hasUtf8Bom: disk.hasUtf8Bom,
    lineEnding: disk.lineEnding,
    readOnlyReason: disk.readOnlyReason
  }
}
function applyDisk(tabId: string, result: ReadTextResult): void {
  const disk = { ...result, text: normalize(result.text) }
  patch(tabId, (session) => {
    if (
      disk.text === session.text ||
      (session.markdown && session.mode === 'preview' && session.text === session.baseText)
    ) {
      return {
        ...session,
        ...metadata(disk),
        text: disk.text,
        baseText: disk.text,
        version: session.version + Number(session.text !== disk.text),
        saveState: 'idle',
        review: undefined,
        reviewWrite: false,
        error: undefined
      }
    }
    if (disk.text === session.baseText) {
      return {
        ...session,
        ...metadata(disk),
        review: undefined,
        saveState: 'idle',
        error: undefined
      }
    }
    const clean = session.text === session.baseText
    return {
      ...session,
      saveState: 'conflict',
      review: {
        disk,
        oldText: clean ? session.text : disk.text,
        newText: clean ? disk.text : session.text
      }
    }
  })
  schedule(tabId)
}

export const useFileEditorStore = create<FileEditorStore>((set, get) => ({
  sessions: new Map(),
  async load(tabId, absolutePath, markdown) {
    if (get().sessions.get(tabId)?.absolutePath === absolutePath) return
    get().discard(tabId)
    const runtime: Runtime = { tail: Promise.resolve(), disposed: false }
    runtimes.set(tabId, runtime)
    set((state) => ({
      sessions: new Map(state.sessions).set(tabId, {
        tabId,
        absolutePath,
        markdown,
        status: 'loading',
        text: '',
        baseText: '',
        version: 0,
        mtimeMs: 0,
        sizeBytes: 0,
        hasUtf8Bom: false,
        lineEnding: 'lf',
        mode: markdown ? 'preview' : 'source',
        saveState: 'idle',
        snapshots: {},
        focusRevision: 0
      })
    }))
    try {
      const disk = await window.api.workspace.viewer.readText({ absolutePath })
      if (runtime.disposed) return
      patch(tabId, (session) => ({
        ...session,
        ...metadata(disk),
        status: 'ready',
        text: normalize(disk.text),
        baseText: normalize(disk.text)
      }))
      void window.api.workspace.viewer
        .watchText({ absolutePath }, () => {
          if (!runtime.disposed) void get().refreshFromDisk(tabId)
        })
        .then((stop) => {
          if (runtime.disposed) stop()
          else {
            runtime.stop = stop
            void get().refreshFromDisk(tabId)
          }
        })
        .catch(() => {})
    } catch (error) {
      if (!runtime.disposed)
        patch(tabId, (session) => ({ ...session, status: 'error', error: String(error) }))
    }
  },
  updateText(tabId, text) {
    patch(tabId, (session) =>
      session.review || session.readOnlyReason || session.text === text
        ? session
        : {
            ...session,
            text,
            version: session.version + 1,
            saveState: session.saveState === 'failed' ? 'idle' : session.saveState,
            error: undefined
          }
    )
    schedule(tabId)
  },
  setSnapshot(tabId, mode, snapshot) {
    patch(tabId, (session) => ({
      ...session,
      snapshots: { ...session.snapshots, [mode]: snapshot }
    }))
  },
  async switchMode(tabId, mode) {
    const session = get().sessions.get(tabId)
    if (!session || session.mode === mode) return true
    if (!(await get().save(tabId))) return false
    const latest = get().sessions.get(tabId)
    if (!latest || latest.mode !== session.mode || latest.review || latest.text !== latest.baseText)
      return false
    patch(tabId, (item) => ({ ...item, mode }))
    return true
  },
  save(tabId) {
    const runtime = runtimes.get(tabId)
    if (!runtime) return Promise.resolve(!get().sessions.get(tabId))
    if (runtime.save) return runtime.save
    clearTimeout(runtime.timer)
    runtime.save = (async () => {
      while (!runtime.disposed) {
        const ok = await enqueue(runtime, async () => {
          const session = get().sessions.get(tabId)
          if (!session || runtime.disposed) return false
          if (session.review) return false
          if (session.text === session.baseText) return true
          if (session.status !== 'ready') return false
          if (session.readOnlyReason && !session.reviewWrite) return false
          patch(tabId, (item) => ({ ...item, saveState: 'saving', error: undefined }))
          try {
            const result = await window.api.workspace.viewer.writeText({
              absolutePath: session.absolutePath,
              text: session.text,
              expectedMtimeMs: session.mtimeMs,
              hasUtf8Bom: session.hasUtf8Bom,
              lineEnding: session.lineEnding
            })
            if (runtime.disposed) return false
            if (result.outcome === 'conflict') {
              applyDisk(tabId, result.current)
              return !get().sessions.get(tabId)?.review
            }
            patch(tabId, (item) => ({
              ...item,
              baseText: session.text,
              mtimeMs: result.mtimeMs,
              sizeBytes: result.sizeBytes,
              saveState: 'idle',
              readOnlyReason: result.sizeBytes > 10 * 1024 * 1024 ? 'large-file' : undefined,
              reviewWrite: item.text !== session.text && item.reviewWrite
            }))
            return true
          } catch (error) {
            if (runtime.disposed) return false
            patch(tabId, (item) => ({ ...item, saveState: 'failed', error: String(error) }))
            if (session.mode === 'source') {
              showToast({
                message: translate(
                  normalizeLocale(document.documentElement.lang),
                  'viewer.saveFailed'
                ),
                type: 'error',
                key: 'file-save-' + tabId
              })
            }
            return false
          }
        })
        if (!ok) return false
        const latest = get().sessions.get(tabId)
        if (!latest || latest.review) return false
        if (latest.text === latest.baseText) return true
      }
      return false
    })().finally(() => {
      runtime.save = undefined
    })
    return runtime.save
  },
  refreshFromDisk(tabId) {
    const runtime = runtimes.get(tabId)
    if (!runtime || runtime.disposed) return Promise.resolve()
    runtime.refreshAgain = true
    if (runtime.refresh) return runtime.refresh
    runtime.refresh = enqueue(runtime, async () => {
      do {
        runtime.refreshAgain = false
        const session = get().sessions.get(tabId)
        if (!session || runtime.disposed || session.status !== 'ready') return
        try {
          const disk = await window.api.workspace.viewer.readText({
            absolutePath: session.absolutePath
          })
          if (!runtime.disposed) applyDisk(tabId, disk)
        } catch (error) {
          if (!runtime.disposed) patch(tabId, (item) => ({ ...item, error: String(error) }))
        }
      } while (runtime.refreshAgain && !runtime.disposed)
    }).finally(() => {
      runtime.refresh = undefined
    })
    return runtime.refresh
  },
  async resolveReview(tabId, choice) {
    const reviewed = get().sessions.get(tabId)?.review
    if (!reviewed) return false
    await get().refreshFromDisk(tabId)
    const session = get().sessions.get(tabId)
    const review = session?.review
    if (!session || session.error) return false
    if (!review) return true
    if (review.disk.mtimeMs !== reviewed.disk.mtimeMs || review.disk.text !== reviewed.disk.text)
      return false
    const text = choice === 'reject' ? review.oldText : review.newText
    patch(tabId, (item) => ({
      ...item,
      ...metadata(review.disk),
      text,
      baseText: review.disk.text,
      version: item.version + Number(item.text !== text),
      saveState: 'idle',
      review: undefined,
      reviewWrite: text !== review.disk.text,
      error: undefined,
      focusRevision: item.focusRevision + Number(choice === 'edit')
    }))
    schedule(tabId)
    return true
  },
  discard(tabId) {
    const runtime = runtimes.get(tabId)
    if (runtime) {
      runtime.disposed = true
      clearTimeout(runtime.timer)
      runtime.stop?.()
      runtimes.delete(tabId)
    }
    set((state) => {
      const sessions = new Map(state.sessions)
      sessions.delete(tabId)
      return { sessions }
    })
  }
}))
