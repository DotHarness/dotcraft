import { normalizeLocale } from '../../../shared/locales'
import { petPose, type PetCommand, type PetEvent, type PetPoint, type PetRect, type PetSnapshot } from '../../../shared/desktopPet'
import { useComposerPreferencesStore } from '../../stores/composerPreferencesStore'
import { findPetEditor } from './editorBridge'
import { decidePetApproval, stopPetTurn } from './petOwnerActions'
import type { PetActivityHandle } from './usePetActivity'

/** How long an orphaned companion waits for a replacement composer before flying home. */
export const PET_ADOPTION_GRACE_MS = 300
const SNAPSHOT_INTERVAL_MS = 250

/** Chat composers own a draft; approval composers relay decisions; other decision surfaces send the companion home. */
export type PetSourceSurface = 'chat' | 'approval' | 'decision'

export interface PetSourceBinding {
  root: HTMLElement
  surface: () => PetSourceSurface
  name: () => string
  threadId: () => string | null
  activity: PetActivityHandle
  /** Drops this composer's drag visuals. */
  reset: () => void
  /** Releases pointer capture; restores focus when the companion came home. */
  release: (restoreFocus: boolean) => void
}

interface Session {
  binding: PetSourceBinding | null
  editRevision: number
  lastSnapshot: string
  detached: boolean
  pendingReturn: ReturnType<typeof setTimeout> | null
  pendingEdit: Extract<PetEvent, { type: 'edit' }> | null
  timer: ReturnType<typeof setInterval>
  unsubscribe: () => void
  unwatch: (() => void) | null
}

// One detached companion per renderer. The session outlives the composer that started it so
// a composer swap (welcome -> thread, builder -> panel) transfers ownership instead of ending it.
let session: Session | null = null

export function petSourceSeat(root: HTMLElement): PetRect | null {
  const mascot = root.querySelector<HTMLElement>('.composer-mascot-jelly')
  if (!mascot) return null
  const rect = mascot.getBoundingClientRect()
  return { x: rect.x, y: rect.y, width: rect.width, height: rect.height }
}

export function petSourceOwner(): PetSourceBinding | null {
  return session?.binding ?? null
}

export function petSourceDetached(): boolean {
  return session?.detached === true
}

export function prefersReducedMotion(): boolean {
  const configured = document.documentElement.dataset.reduceMotion
  return configured === 'on' || (configured !== 'off' && matchMedia('(prefers-reduced-motion: reduce)').matches)
}

export function petSourceCommand(command: PetCommand): void {
  const api = window.api?.desktopPet
  if (!api) return
  void api.command(command).catch(() => endPetSource(false))
}

export function startPetSource(binding: PetSourceBinding, detach: { seat: PetRect; point: PetPoint; pointerHeld: boolean }): void {
  const api = window.api?.desktopPet
  if (!api) return
  endPetSource(false)
  binding.activity.seedRead()
  const snapshot = snapshotOf(binding, 0)
  session = {
    binding,
    editRevision: 0,
    lastSnapshot: JSON.stringify(snapshot),
    detached: false,
    pendingReturn: null,
    pendingEdit: null,
    timer: setInterval(publish, SNAPSHOT_INTERVAL_MS),
    unsubscribe: api.onEvent(onEvent),
    unwatch: null
  }
  bind(session, binding)
  petSourceCommand({ type: 'detach', ...detach, snapshot })
}

export function adoptPetSource(binding: PetSourceBinding): boolean {
  const current = session
  if (!current || current.binding || !current.pendingReturn) return false
  clearTimeout(current.pendingReturn)
  current.pendingReturn = null
  if (binding.surface() === 'decision') {
    petSourceCommand({ type: 'return' })
    return false
  }
  bind(current, binding)
  current.lastSnapshot = ''
  publish()
  return true
}

export function releasePetSource(binding: PetSourceBinding): void {
  const current = session
  if (!current || current.binding !== binding) return
  unbind(current)
  current.pendingReturn = setTimeout(() => {
    if (session !== current) return
    current.pendingReturn = null
    petSourceCommand({ type: 'return' })
  }, PET_ADOPTION_GRACE_MS)
}

export function endPetSource(restoreFocus: boolean): void {
  const current = session
  session = null
  if (!current) return
  const binding = current.binding
  clearInterval(current.timer)
  if (current.pendingReturn) clearTimeout(current.pendingReturn)
  current.unsubscribe()
  unbind(current)
  document.documentElement.removeAttribute('data-desktop-pet-detached')
  binding?.reset()
  binding?.release(restoreFocus)
}

function bind(current: Session, binding: PetSourceBinding): void {
  current.binding = binding
  binding.root.setAttribute('data-pet-owner', '')
  const offActivity = binding.activity.subscribe(publish)
  const observer = new MutationObserver(() => { binding.activity.refresh(); publish() })
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'lang', 'data-reduce-motion'] })
  const media = matchMedia('(prefers-reduced-motion: reduce)')
  media.addEventListener?.('change', publish)
  current.unwatch = () => {
    offActivity()
    observer.disconnect()
    media.removeEventListener?.('change', publish)
  }
}

function unbind(current: Session): void {
  current.unwatch?.()
  current.unwatch = null
  current.binding?.root.removeAttribute('data-pet-owner')
  current.binding = null
}

function snapshotOf(binding: PetSourceBinding, editRevision: number): PetSnapshot {
  const editor = findPetEditor(binding.root)
  const status = binding.activity.read() ?? undefined
  return {
    activity: petPose(status?.status ?? 'idle'),
    status,
    name: binding.name(),
    text: editor?.getText() ?? '',
    editRevision,
    theme: document.documentElement.dataset.theme === 'light' ? 'light' : 'dark',
    locale: normalizeLocale(document.documentElement.lang),
    reducedMotion: prefersReducedMotion(),
    canChat: binding.surface() === 'chat' && !!editor,
    busy: !editor?.enabled,
    followUpMode: status?.status === 'running' ? useComposerPreferencesStore.getState().followUpQueueMode : 'queue'
  }
}

function publish(): void {
  const current = session
  const binding = current?.binding
  if (!current || !binding) return
  if (current.pendingEdit && writeEdit(binding, current.pendingEdit)) current.pendingEdit = null
  const next = snapshotOf(binding, current.editRevision)
  const serialized = JSON.stringify(next)
  if (serialized === current.lastSnapshot) return
  current.lastSnapshot = serialized
  petSourceCommand({ type: 'snapshot', snapshot: next })
}

function writeEdit(binding: PetSourceBinding, edit: Extract<PetEvent, { type: 'edit' }>): boolean {
  const editor = findPetEditor(binding.root)
  if (!editor) return false
  if (editor.getText() !== edit.text) editor.setText(edit.text)
  if (edit.submit && editor.enabled) {
    // Let the source composer commit its draft-dependent state before submission.
    setTimeout(() => { if (session?.binding === binding) findPetEditor(binding.root)?.submit() }, 0)
  }
  return true
}

function onEvent(event: PetEvent): void {
  const current = session
  if (!current) return
  if (event.type === 'ownership') {
    current.detached = event.detached
    document.documentElement.toggleAttribute('data-desktop-pet-detached', event.detached)
    if (event.detached) {
      petSourceCommand({ type: 'hidden' })
      current.binding?.reset()
    } else {
      endPetSource(true)
    }
  } else if (event.type === 'return-seat') {
    const binding = current.binding
    if (!binding) return
    binding.reset()
    petSourceCommand({ type: 'seat', seat: petSourceSeat(binding.root) })
  } else if (event.type === 'edit') {
    current.editRevision = event.revision
    const binding = current.binding
    if (!binding) {
      current.pendingEdit = event
      return
    }
    if (binding.surface() !== 'chat' || !writeEdit(binding, event)) petSourceCommand({ type: 'return' })
  } else if (event.type === 'stop') {
    const binding = current.binding
    if (binding) void stopPetTurn(binding.threadId(), event.turnId)
  } else if (event.type === 'read') {
    current.binding?.activity.markRead(event.turnId)
  } else if (event.type === 'decision') {
    if (current.binding) void decidePetApproval(event.id, event.value)
  }
}
