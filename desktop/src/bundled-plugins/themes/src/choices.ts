import type { DesktopPluginHost } from '@dotcraft/plugin'
import { useSyncExternalStore } from 'react'
import {
  patchThemeChoices, resolveThemeChoice, slotsAt,
  type ThemeAppearance, type ThemeChoice, type ThemeChoiceContext, type ThemeChoices,
  type ThemeScope, type ThemeSlotsPatch
} from '../../../shared/themes'

interface ChoicesSnapshot {
  readonly choices: ThemeChoices | null
  readonly context: ThemeChoiceContext
}

const NONE: ThemeChoice = { kind: 'none' }
let stored: ThemeChoices | null = null
let snapshot: ChoicesSnapshot = { choices: null, context: { threadId: null } }
let generation = 0
let isAvailable: (choice: ThemeChoice, appearance: ThemeAppearance) => boolean = () => true
const listeners = new Set<() => void>()

function publish(patch: Partial<ChoicesSnapshot>): void {
  snapshot = { ...snapshot, ...patch }
  for (const listener of [...listeners]) listener()
}

export function subscribeChoices(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function getChoices(): ChoicesSnapshot { return snapshot }

export function useChoices(): ChoicesSnapshot {
  return useSyncExternalStore(subscribeChoices, getChoices, getChoices)
}

export function resolvedChoice(appearance: ThemeAppearance): ThemeChoice {
  return snapshot.choices ? resolveThemeChoice(snapshot.choices, appearance, snapshot.context, isAvailable).choice : NONE
}

export function setChoiceAvailability(predicate: ((choice: ThemeChoice, appearance: ThemeAppearance) => boolean) | null): void {
  isAvailable = predicate ?? (() => true)
  publish({})
}

export function scopeOf(kind: ThemeScope['kind']): ThemeScope | null {
  const { threadId } = snapshot.context
  if (kind === 'everywhere') return { kind }
  return threadId ? { kind, threadId } : null
}

export function choiceAt(scope: ThemeScope, appearance: ThemeAppearance): ThemeChoice | undefined {
  return snapshot.choices ? slotsAt(snapshot.choices, scope)[appearance] : undefined
}

function previewChoice(scope: ThemeScope, patch: ThemeSlotsPatch): void {
  if (snapshot.choices) publish({ choices: patchThemeChoices(snapshot.choices, scope, patch, Date.now()) })
}

export function setChoice(scope: ThemeScope, patch: ThemeSlotsPatch): void {
  previewChoice(scope, patch)
  const own = generation
  void window.api.themes.setChoice(scope, patch)
    .then((choices) => { if (own === generation) accept(choices) })
    .catch((error: unknown) => {
      if (own === generation && stored) publish({ choices: stored })
      console.error('Themes could not write its choice:', error)
    })
}

function accept(choices: ThemeChoices): void {
  stored = choices
  publish({ choices })
}

export function initializeChoices(host: DesktopPluginHost): () => void {
  const own = ++generation
  publish({ context: { threadId: host.session.threadId } })
  const stopSession = host.session.onChange((session) => {
    if (session.threadId !== snapshot.context.threadId) publish({ context: { threadId: session.threadId } })
  })
  const stopChanged = window.api.themes.onChoicesChanged((choices) => { if (own === generation) accept(choices) })
  void window.api.themes.choices()
    .then((choices) => { if (own === generation) accept(choices) })
    .catch((error: unknown) => console.error('Themes could not read its choices:', error))
  return () => {
    stopSession()
    stopChanged()
    if (own !== generation) return
    generation += 1
    stored = null
    publish({ choices: null })
  }
}
