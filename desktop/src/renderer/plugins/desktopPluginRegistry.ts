import { create } from 'zustand'

import type {
  DesktopLocalizedText,
  DesktopPluginAddOptions,
  DesktopPluginCommandContribution,
  DesktopPluginCommandContext,
  DesktopPluginConversationViewContribution,
  DesktopPluginDispose,
  DesktopPluginHost,
  DesktopPluginMainViewContribution,
  DesktopPluginMessageActionContribution,
  DesktopPluginSettingsPageContribution,
  DesktopPluginSurfaceComponent,
  DesktopPluginSurfaceContextMap,
  DesktopPluginSurfaceWrapper,
  DesktopPluginToolRendererContribution
} from '@dotcraft/plugin'
import { normalizeLocale, type AppLocale } from '../../shared/locales'
import type { DesktopPluginMainView } from '../stores/uiStore'
import type { DesktopPluginSettingsTab } from '../types/settings'

interface ActiveDesktopPluginContribution {
  pluginId: string
  revision: string
  host: DesktopPluginHost
}

type DesktopPluginSurfaceKind = 'add' | 'replace' | 'wrap'

export interface ActiveDesktopPluginSurface {
  pluginId: string
  host: DesktopPluginHost
  registrationId: number
  surface: string
  kind: DesktopPluginSurfaceKind
  order?: number
  component: DesktopPluginSurfaceComponent<any> | DesktopPluginSurfaceWrapper<any>
}

export interface ActiveDesktopPluginMainView extends DesktopPluginMainViewContribution, ActiveDesktopPluginContribution {
  viewKey: DesktopPluginMainView
}

export interface ActiveDesktopPluginSettingsPage extends DesktopPluginSettingsPageContribution, ActiveDesktopPluginContribution {
  settingsKey: DesktopPluginSettingsTab
}

export interface ActiveDesktopPluginConversationView extends DesktopPluginConversationViewContribution, ActiveDesktopPluginContribution {
  contributionKey: string
}

export interface ActiveDesktopPluginCommand extends DesktopPluginCommandContribution, ActiveDesktopPluginContribution {
  contributionKey: string
}

export interface ActiveDesktopPluginToolRenderer extends DesktopPluginToolRendererContribution, ActiveDesktopPluginContribution {
  contributionKey: string
}

export interface ActiveDesktopPluginMessageAction extends DesktopPluginMessageActionContribution, ActiveDesktopPluginContribution {
  contributionKey: string
}

export interface DesktopPluginGeneration {
  pluginId: string
  version: string
  revision: string
  mainViews: readonly ActiveDesktopPluginMainView[]
  settingsPages: readonly ActiveDesktopPluginSettingsPage[]
  conversationViews: readonly ActiveDesktopPluginConversationView[]
  commands: readonly ActiveDesktopPluginCommand[]
  toolRenderers: readonly ActiveDesktopPluginToolRenderer[]
  messageActions: readonly ActiveDesktopPluginMessageAction[]
}

interface DesktopPluginRegistryState {
  generations: ReadonlyMap<string, DesktopPluginGeneration>
  mainViews: readonly ActiveDesktopPluginMainView[]
  settingsPages: readonly ActiveDesktopPluginSettingsPage[]
  conversationViews: readonly ActiveDesktopPluginConversationView[]
  commands: readonly ActiveDesktopPluginCommand[]
  toolRenderers: readonly ActiveDesktopPluginToolRenderer[]
  messageActions: readonly ActiveDesktopPluginMessageAction[]
  surfaces: readonly ActiveDesktopPluginSurface[]
  conversationSelections: ReadonlyMap<string, string>
}

let nextDesktopPluginSurfaceRegistrationId = 1

export const useDesktopPluginRegistry = create<DesktopPluginRegistryState>(() =>
  registryState(new Map(), new Map(), [])
)

export function publishDesktopPluginGeneration(generation: DesktopPluginGeneration): void {
  useDesktopPluginRegistry.setState((state) => {
    const generations = new Map(state.generations)
    generations.set(generation.pluginId, generation)
    return registryState(generations, state.conversationSelections, state.surfaces)
  })
}

export function withdrawDesktopPluginGeneration(pluginId: string): void {
  useDesktopPluginRegistry.setState((state) => {
    if (!state.generations.has(pluginId)) return state
    const generations = new Map(state.generations)
    generations.delete(pluginId)
    return registryState(generations, state.conversationSelections, state.surfaces)
  })
}

export function clearDesktopPluginRegistry(): void {
  nextDesktopPluginSurfaceRegistrationId = 1
  useDesktopPluginRegistry.setState(registryState(new Map(), new Map(), []))
}

/** Core's own surface names. Typed against the SDK map so a typo in this list is a compile error. */
const CORE_SURFACES: ReadonlySet<string> = new Set<keyof DesktopPluginSurfaceContextMap>([
  'app',
  'app.background',
  'app.overlay',
  'app.status',
  'composer',
  'composer.mascot',
  'composer.before',
  'composer.after',
  'composer.input',
  'composer.input.attachments',
  'composer.input.editor',
  'composer.toolbar',
  'composer.toolbar.leading',
  'composer.toolbar.trailing',
  'composer.toolbar.commands',
  'composer.toolbar.permissions',
  'composer.toolbar.mode',
  'composer.toolbar.goal',
  'composer.toolbar.context-usage',
  'composer.toolbar.model',
  'composer.toolbar.voice',
  'composer.toolbar.submit',
  'composer.status',
  'composer.status.workspace',
  'composer.status.subscription'
])

export function registerDesktopPluginSurface<S extends string>(
  pluginId: string,
  host: DesktopPluginHost,
  surface: S,
  kind: 'add',
  component: DesktopPluginSurfaceComponent<S>,
  options?: DesktopPluginAddOptions
): DesktopPluginDispose
export function registerDesktopPluginSurface<S extends string>(
  pluginId: string,
  host: DesktopPluginHost,
  surface: S,
  kind: 'replace',
  component: DesktopPluginSurfaceComponent<S>
): DesktopPluginDispose
export function registerDesktopPluginSurface<S extends string>(
  pluginId: string,
  host: DesktopPluginHost,
  surface: S,
  kind: 'wrap',
  component: DesktopPluginSurfaceWrapper<S>
): DesktopPluginDispose
export function registerDesktopPluginSurface<S extends string>(
  pluginId: string,
  host: DesktopPluginHost,
  surface: S,
  kind: DesktopPluginSurfaceKind,
  component: DesktopPluginSurfaceComponent<S> | DesktopPluginSurfaceWrapper<S>,
  options?: DesktopPluginAddOptions
): DesktopPluginDispose {
  reportUnknownCoreSurface(pluginId, surface)
  const registration: ActiveDesktopPluginSurface = {
    pluginId,
    host,
    surface,
    kind,
    order: options?.order,
    component,
    registrationId: nextDesktopPluginSurfaceRegistrationId++
  }
  useDesktopPluginRegistry.setState((state) => ({
    surfaces: [...state.surfaces, registration]
  }))

  return () => {
    useDesktopPluginRegistry.setState((state) => {
      if (!state.surfaces.includes(registration)) return state
      return { surfaces: state.surfaces.filter((candidate) => candidate !== registration) }
    })
  }
}

/** Only the `app` and `composer` roots are Core's to close; a plugin-declared name may legally be unmounted. */
function reportUnknownCoreSurface(pluginId: string, surface: string): void {
  const root = surface.split('.')[0]
  if (root !== 'app' && root !== 'composer') return
  if (CORE_SURFACES.has(surface)) return
  console.warn(
    `Desktop Plugin '${pluginId}' registered into '${surface}', which Core does not define. `
      + 'Check the spelling; the registration stays and renders only if a surface with that name is mounted.'
  )
}

export function compareDesktopPluginAddition(
  left: ActiveDesktopPluginSurface,
  right: ActiveDesktopPluginSurface
): number {
  return (left.order ?? 100) - (right.order ?? 100)
    || left.registrationId - right.registrationId
}

export function buildDesktopPluginContributionKey(pluginId: string, contributionId: string): string {
  return `${encodeURIComponent(pluginId)}:${encodeURIComponent(contributionId)}`
}

export function buildDesktopPluginMainViewKey(pluginId: string, contributionId: string): DesktopPluginMainView {
  return `desktop-plugin:${encodeURIComponent(pluginId)}:${encodeURIComponent(contributionId)}`
}

export function buildDesktopPluginSettingsKey(pluginId: string, contributionId: string): DesktopPluginSettingsTab {
  return `desktop-plugin-settings:${encodeURIComponent(pluginId)}:${encodeURIComponent(contributionId)}`
}

export function findDesktopPluginMainView(view: string): ActiveDesktopPluginMainView | null {
  return useDesktopPluginRegistry.getState().mainViews.find((entry) => entry.viewKey === view) ?? null
}

export function selectDesktopPluginConversationView(threadId: string, contributionKey: string | null): void {
  useDesktopPluginRegistry.setState((state) => {
    const selections = new Map(state.conversationSelections)
    if (contributionKey && state.conversationViews.some((entry) => entry.contributionKey === contributionKey)) {
      selections.set(threadId, contributionKey)
    } else {
      selections.delete(threadId)
    }
    return { conversationSelections: selections }
  })
}

export function findSelectedDesktopPluginConversationView(threadId: string): ActiveDesktopPluginConversationView | null {
  const state = useDesktopPluginRegistry.getState()
  const key = state.conversationSelections.get(threadId)
  return key ? state.conversationViews.find((entry) => entry.contributionKey === key) ?? null : null
}

export function resolveDesktopPluginToolRenderer(presentationId: string): ActiveDesktopPluginToolRenderer | null {
  return useDesktopPluginRegistry.getState().toolRenderers
    .find((entry) => entry.presentationId === presentationId) ?? null
}

export function executeDesktopPluginCommand(
  contributionKey: string,
  context: DesktopPluginCommandContext
): void | Promise<void> {
  const command = useDesktopPluginRegistry.getState().commands
    .find((candidate) => candidate.contributionKey === contributionKey)
  if (!command || !isDesktopPluginContributionAvailable(command, context)) return
  return command.execute(context, command.host)
}

export function isDesktopPluginContributionAvailable<TContext>(
  contribution: {
    pluginId: string
    id: string
    isAvailable?: (context: TContext) => boolean
  },
  context: TContext
): boolean {
  try {
    return contribution.isAvailable?.(context) ?? true
  } catch (error) {
    console.error(
      `Desktop Plugin contribution '${contribution.pluginId}/${contribution.id}' availability failed:`,
      error
    )
    return false
  }
}

export function isDesktopPluginMainView(view: string): view is DesktopPluginMainView {
  return view.startsWith('desktop-plugin:')
}

export function isDesktopPluginSettingsTab(tab: string): tab is DesktopPluginSettingsTab {
  return tab.startsWith('desktop-plugin-settings:')
}

export function resolveDesktopPluginLabel(label: DesktopLocalizedText, locale: string): string {
  const translations = label.translations
  if (!translations) return label.default
  const appLocale = normalizeLocale(locale)
  return translations[appLocale]?.trim()
    || Object.entries(translations)
      .find(([key, text]) => sameLocale(key, appLocale) && text.trim())?.[1].trim()
    || label.default
}

function sameLocale(key: string, appLocale: AppLocale): boolean {
  if (normalizeLocale(key) !== appLocale) return false
  // `normalizeLocale` falls back to English, so an unsupported tag must not claim the English entry.
  return key.split('-')[0].toLowerCase() === appLocale.split('-')[0].toLowerCase()
}

function registryState(
  generations: ReadonlyMap<string, DesktopPluginGeneration>,
  previousSelections: ReadonlyMap<string, string>,
  surfaces: readonly ActiveDesktopPluginSurface[]
): DesktopPluginRegistryState {
  const values = [...generations.values()]
  const conversationViews = values.flatMap((generation) => generation.conversationViews).sort(compareContribution)
  const activeConversationKeys = new Set(conversationViews.map((entry) => entry.contributionKey))
  const conversationSelections = new Map(
    [...previousSelections].filter(([, contributionKey]) => activeConversationKeys.has(contributionKey))
  )
  return {
    generations,
    mainViews: values.flatMap((generation) => generation.mainViews).sort(compareContribution),
    settingsPages: values.flatMap((generation) => generation.settingsPages).sort(compareContribution),
    conversationViews,
    commands: values.flatMap((generation) => generation.commands).sort(compareContribution),
    toolRenderers: values.flatMap((generation) => generation.toolRenderers).sort(compareToolRenderer),
    messageActions: values.flatMap((generation) => generation.messageActions).sort(compareContribution),
    surfaces,
    conversationSelections
  }
}

function compareContribution(
  left: { order?: number; pluginId: string; id: string },
  right: { order?: number; pluginId: string; id: string }
): number {
  return (left.order ?? 100) - (right.order ?? 100)
    || ordinalCompare(left.pluginId, right.pluginId)
    || ordinalCompare(left.id, right.id)
}

function compareToolRenderer(left: ActiveDesktopPluginToolRenderer, right: ActiveDesktopPluginToolRenderer): number {
  return (left.priority ?? 100) - (right.priority ?? 100)
    || ordinalCompare(left.pluginId, right.pluginId)
    || ordinalCompare(left.id, right.id)
}

function ordinalCompare(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0
}
