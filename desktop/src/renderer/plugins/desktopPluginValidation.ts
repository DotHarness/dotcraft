import type {
  DesktopPluginActivate,
  DesktopPluginCommandContribution,
  DesktopPluginConversationViewContribution,
  DesktopPluginHost,
  DesktopPluginMainViewContribution,
  DesktopPluginMessageActionContribution,
  DesktopPluginSettingsPageContribution,
  DesktopPluginToolRendererContribution
} from '@dotcraft/plugin'

import {
  buildDesktopPluginContributionKey,
  buildDesktopPluginMainViewKey,
  buildDesktopPluginSettingsKey,
  type ActiveDesktopPluginCommand,
  type ActiveDesktopPluginConversationView,
  type ActiveDesktopPluginMainView,
  type ActiveDesktopPluginMessageAction,
  type ActiveDesktopPluginSettingsPage,
  type ActiveDesktopPluginToolRenderer,
  type DesktopPluginGeneration
} from './desktopPluginRegistry'

interface DesktopPluginModule { activate: DesktopPluginActivate }

export function requireDesktopPluginModule(value: unknown): DesktopPluginModule {
  if (!isRecord(value) || typeof value.activate !== 'function') {
    throw new Error('Desktop Plugin entry must export activate(host).')
  }
  return value as unknown as DesktopPluginModule
}

export function validateActivation(
  pluginId: string,
  version: string,
  revision: string,
  host: DesktopPluginHost,
  value: unknown
): DesktopPluginGeneration {
  if (!isRecord(value)) throw new Error('Desktop Plugin activate(host) must return an activation object.')
  const allowedKeys = new Set([
    'mainViews',
    'settingsPages',
    'conversationViews',
    'commands',
    'toolRenderers',
    'messageActions',
    'dispose'
  ])
  const unknownKind = Object.keys(value).find((key) => !allowedKeys.has(key))
  if (unknownKind) throw new Error(`Desktop Plugin activation contains unknown contribution '${unknownKind}'.`)
  if (value.dispose != null && typeof value.dispose !== 'function') {
    throw new Error('Desktop Plugin activation dispose must be a function.')
  }
  const ids = new Set<string>()
  const active = { pluginId, revision, host }
  const mainViews = validateViewContributions<DesktopPluginMainViewContribution>(value.mainViews, 'main view', ids)
    .map<ActiveDesktopPluginMainView>((contribution) => Object.freeze({
      ...contribution,
      ...active,
      viewKey: buildDesktopPluginMainViewKey(pluginId, contribution.id),
    }))
  const settingsPages = validateViewContributions<DesktopPluginSettingsPageContribution>(
    value.settingsPages,
    'settings page',
    ids
  ).map<ActiveDesktopPluginSettingsPage>((contribution) => Object.freeze({
    ...contribution,
    ...active,
    settingsKey: buildDesktopPluginSettingsKey(pluginId, contribution.id),
  }))
  const conversationViews = validateViewContributions<DesktopPluginConversationViewContribution>(
    value.conversationViews,
    'conversation view',
    ids
  ).map<ActiveDesktopPluginConversationView>((contribution) => Object.freeze({
    ...contribution,
    ...active,
    contributionKey: buildDesktopPluginContributionKey(pluginId, contribution.id)
  }))
  const commands = validateCallbackContributions<DesktopPluginCommandContribution>(
    value.commands,
    'command',
    ids
  ).map<ActiveDesktopPluginCommand>((contribution) => Object.freeze({
    ...contribution,
    ...active,
    contributionKey: buildDesktopPluginContributionKey(pluginId, contribution.id)
  }))
  const toolRenderers = validateToolRenderers(value.toolRenderers, ids)
    .map<ActiveDesktopPluginToolRenderer>((contribution) => Object.freeze({
      ...contribution,
      ...active,
      contributionKey: buildDesktopPluginContributionKey(pluginId, contribution.id)
    }))
  const messageActions = validateCallbackContributions<DesktopPluginMessageActionContribution>(
    value.messageActions,
    'message action',
    ids
  ).map<ActiveDesktopPluginMessageAction>((contribution) => Object.freeze({
    ...contribution,
    ...active,
    contributionKey: buildDesktopPluginContributionKey(pluginId, contribution.id)
  }))
  return Object.freeze({
    pluginId,
    version,
    revision,
    mainViews: Object.freeze(mainViews),
    settingsPages: Object.freeze(settingsPages),
    conversationViews: Object.freeze(conversationViews),
    commands: Object.freeze(commands),
    toolRenderers: Object.freeze(toolRenderers),
    messageActions: Object.freeze(messageActions)
  })
}

function validateViewContributions<T extends {
  id: string
  label: { default: string; translations?: Readonly<Record<string, string>> }
  component: unknown
  order?: number
}>(
  value: unknown,
  kind: string,
  ids: Set<string>
): T[] {
  return contributionArray(value, kind).map((candidate) => {
    validateLabeledContribution(candidate, kind, ids)
    if (typeof candidate.component !== 'function') {
      throw new Error(`Desktop Plugin ${kind} '${candidate.id}' requires a component.`)
    }
    return candidate as unknown as T
  })
}

function validateCallbackContributions<T extends {
  id: string
  label: { default: string; translations?: Readonly<Record<string, string>> }
  description?: unknown
  execute: unknown
  isAvailable?: unknown
}>(value: unknown, kind: string, ids: Set<string>): T[] {
  return contributionArray(value, kind).map((candidate) => {
    validateLabeledContribution(candidate, kind, ids)
    if (candidate.description != null) validateLocalizedText(candidate.description, `${kind} '${candidate.id}' description`)
    if (candidate.isAvailable != null && typeof candidate.isAvailable !== 'function') {
      throw new Error(`Desktop Plugin ${kind} '${candidate.id}' has an invalid availability predicate.`)
    }
    if (typeof candidate.execute !== 'function') {
      throw new Error(`Desktop Plugin ${kind} '${candidate.id}' requires execute.`)
    }
    return candidate as unknown as T
  })
}

function validateToolRenderers(value: unknown, ids: Set<string>): DesktopPluginToolRendererContribution[] {
  return contributionArray(value, 'tool renderer').map((candidate) => {
    validateContributionId(candidate, 'tool renderer', ids)
    if (typeof candidate.presentationId !== 'string' || !candidate.presentationId.trim()
      || candidate.presentationId !== candidate.presentationId.trim()) {
      throw new Error(`Desktop Plugin tool renderer '${candidate.id}' requires a presentationId.`)
    }
    if (candidate.priority != null && (typeof candidate.priority !== 'number' || !Number.isFinite(candidate.priority))) {
      throw new Error(`Desktop Plugin tool renderer '${candidate.id}' has an invalid priority.`)
    }
    if (typeof candidate.component !== 'function') {
      throw new Error(`Desktop Plugin tool renderer '${candidate.id}' requires a component.`)
    }
    return candidate as unknown as DesktopPluginToolRendererContribution
  })
}

function contributionArray(value: unknown, kind: string): Record<string, unknown>[] {
  if (value == null) return []
  if (!Array.isArray(value)) throw new Error(`Desktop Plugin ${kind} contributions must be an array.`)
  return value.map((candidate) => {
    if (!isRecord(candidate)) throw new Error(`Desktop Plugin ${kind} contribution must be an object.`)
    return candidate
  })
}

function validateLabeledContribution(candidate: Record<string, unknown>, kind: string, ids: Set<string>): void {
  validateContributionId(candidate, kind, ids)
  validateLocalizedText(candidate.label, `${kind} '${candidate.id}' label`)
  if (candidate.order != null && (typeof candidate.order !== 'number' || !Number.isFinite(candidate.order))) {
    throw new Error(`Desktop Plugin ${kind} '${candidate.id}' has an invalid order.`)
  }
}

function validateContributionId(candidate: Record<string, unknown>, kind: string, ids: Set<string>): void {
  if (typeof candidate.id !== 'string' || !candidate.id.trim()) {
    throw new Error(`Desktop Plugin ${kind} id is required.`)
  }
  if (candidate.id !== candidate.id.trim() || ids.has(candidate.id)) {
    throw new Error(`Desktop Plugin ${kind} id '${candidate.id}' is duplicated or invalid.`)
  }
  ids.add(candidate.id)
}

function validateLocalizedText(value: unknown, field: string): void {
  if (!isRecord(value) || typeof value.default !== 'string' || !value.default.trim()) {
    throw new Error(`Desktop Plugin ${field} is required.`)
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}
