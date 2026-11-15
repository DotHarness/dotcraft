import type { DesktopPluginBackdropPresentation, DesktopPluginHost } from '@dotcraft/plugin'
import { useSyncExternalStore } from 'react'
import { type Theme, type ThemeAppearance, type ThemeScope, type ThemeSlotsPatch } from '../../../shared/themes'
import { BUILTIN_THEMES } from './builtInThemes'
import { openComposerPicker } from './composerPicker'
import { resolvedChoice, setChoice, setChoiceAvailability } from './choices'
import { stringsFor, themeDisplayName } from './i18n'
import { getSettings, subscribeSettings } from './settings'

interface ThemeLibrarySnapshot {
  themes: readonly Theme[]
  preview: Theme | null
  loading: boolean
  error: string | null
}

let snapshot: ThemeLibrarySnapshot = { themes: BUILTIN_THEMES, preview: null, loading: false, error: null }
const listeners = new Set<() => void>()
let generation = 0
let refreshRevision = 0
let readyKey: string | null = null
let pendingPreviewId: string | null = null
let refreshLibrary: (() => Promise<void>) | null = null

export function subscribeThemeLibrary(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function getThemeLibrary(): ThemeLibrarySnapshot { return snapshot }

export function useThemeLibrary(): ThemeLibrarySnapshot {
  return useSyncExternalStore(subscribeThemeLibrary, getThemeLibrary, getThemeLibrary)
}

function publish(patch: Partial<ThemeLibrarySnapshot>): void {
  snapshot = { ...snapshot, ...patch }
  for (const listener of [...listeners]) listener()
}

export function themeKey(theme: Pick<Theme, 'id' | 'revision'>): string { return `${theme.id}:${theme.revision}` }

export function themeById(id: string): Theme | undefined {
  return snapshot.themes.find(theme => theme.id === id)
}

export function reportBackdropReady(theme: Theme, ready: boolean): void {
  const key = themeKey(theme)
  if (!ready && readyKey !== key) return
  if (ready && readyKey === key) return
  readyKey = ready ? key : null
  publish({})
}

export function isBackdropReady(theme: Theme): boolean {
  return theme.backdrop?.kind !== 'scene' || readyKey === themeKey(theme)
}

export function activeTheme(appearance: ThemeAppearance): Theme | null {
  if (!getSettings()?.enabled) return null
  if (snapshot.preview) return snapshot.preview
  const choice = resolvedChoice(appearance)
  return choice.kind === 'theme' ? themeById(choice.id) ?? null : null
}

export function activeGlass(appearance: ThemeAppearance): DesktopPluginBackdropPresentation | null {
  const theme = activeTheme(appearance)
  const settings = getSettings()
  if (!theme?.backdrop || !settings) return null
  return { blur: settings.blur, opacity: settings.surfaceOpacity, ...(theme.tint ? { tint: theme.tint } : {}) }
}

export function previewTheme(themeId: string): void {
  if (!getSettings()?.enabled) return
  const theme = themeById(themeId)
  if (theme) {
    pendingPreviewId = null
    publish({ preview: theme })
  } else {
    pendingPreviewId = themeId
    void refreshLibrary?.()
  }
}

export function cancelThemePreview(): void {
  pendingPreviewId = null
  if (snapshot.preview) publish({ preview: null })
}

export function applyTheme(theme: Theme, scope: ThemeScope): void {
  const patch: ThemeSlotsPatch = { [theme.colorScheme]: { kind: 'theme', id: theme.id } }
  const pair = theme.pair ? themeById(theme.pair) : undefined
  if (pair && pair.colorScheme !== theme.colorScheme) patch[pair.colorScheme] = { kind: 'theme', id: pair.id }
  setChoice(scope, patch)
}

export function initializeThemeLibrary(pluginHost: DesktopPluginHost): () => void {
  const ownGeneration = ++generation
  let threadId = pluginHost.session.threadId
  const refresh = async (): Promise<void> => {
    const revision = ++refreshRevision
    publish({ loading: true })
    try {
      const themes = await window.api.themes.list()
      if (generation !== ownGeneration || revision !== refreshRevision) return
      const all = [...BUILTIN_THEMES, ...themes]
      const requested = pendingPreviewId ?? snapshot.preview?.id
      const preview = requested && getSettings()?.enabled ? all.find(theme => theme.id === requested) ?? null : null
      pendingPreviewId = null
      publish({ themes: all, preview, loading: false, error: null })
      setChoiceAvailability((choice, appearance) =>
        choice.kind !== 'theme' || all.some(theme => theme.id === choice.id && theme.colorScheme === appearance))
    } catch (error) {
      if (generation === ownGeneration && revision === refreshRevision) publish({ loading: false, error: error instanceof Error ? error.message : String(error) })
    }
  }
  refreshLibrary = refresh
  setChoiceAvailability((choice, appearance) =>
    choice.kind !== 'theme' || !choice.id.startsWith('builtin-') || BUILTIN_THEMES.some(theme => theme.id === choice.id && theme.colorScheme === appearance))
  const stopChanged = window.api.themes.onChanged(() => { void refresh() })
  const stopPreview = window.api.themes.onPreview(request => {
    if (request.threadId !== pluginHost.session.threadId) return
    void refresh().then(() => {
      if (generation !== ownGeneration || request.threadId !== pluginHost.session.threadId) return
      previewTheme(request.themeId)
      if (snapshot.preview) openComposerPicker()
    })
  })
  const stopSession = pluginHost.session.onChange(session => {
    if (session.threadId !== threadId) cancelThemePreview()
    threadId = session.threadId
  })
  const stopSettings = subscribeSettings(settings => { if (!settings.enabled) cancelThemePreview() })
  void refresh()
  return () => {
    stopChanged(); stopPreview(); stopSession(); stopSettings()
    if (generation === ownGeneration) {
      generation += 1
      refreshLibrary = null
      pendingPreviewId = null
      readyKey = null
      setChoiceAvailability(null)
      publish({ preview: null, loading: false })
    }
  }
}

export async function startThemeAuthoring(pluginHost: DesktopPluginHost, theme?: Theme): Promise<void> {
  cancelThemePreview()
  if (theme?.authorThreadId) {
    try {
      await pluginHost.appServer.request('thread/read', { threadId: theme.authorThreadId })
      await pluginHost.navigation.openThread(theme.authorThreadId, pluginHost.session.workspacePath ?? undefined)
      return
    } catch { /* A deleted authoring conversation is replaced with a new one. */ }
  }
  const locale = pluginHost.environment.locale
  const strings = stringsFor(locale)
  const prompt = !theme
    ? strings.createPrompt
    : (theme.builtin ? strings.adaptPrompt : strings.editPrompt).replace('{name}', themeDisplayName(theme, locale))
  await pluginHost.navigation.startChat({ prompt, skill: 'themes' })
}
