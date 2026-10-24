import {
  PROJECT_ACTION_PLATFORMS,
  isProjectActionIcon,
  isProjectActionPlatform,
  type EnvironmentDocument,
  type EnvironmentScript,
  type ProjectActionDraft
} from '../../shared/projectActions'
import { isObject, readEnvironmentObject, text } from './discovery'

function scriptFromFile(value: unknown): EnvironmentScript {
  if (!isObject(value)) return { default: '' }
  const script: EnvironmentScript = { default: text(value.script) }
  for (const platform of PROJECT_ACTION_PLATFORMS) {
    const override = value[platform]
    const content = isObject(override) ? text(override.script) : ''
    if (content) script[platform] = content
  }
  return script
}

export function documentFromContent(content: string): { document: EnvironmentDocument } | { error: string } {
  const parsed = readEnvironmentObject(content)
  if ('error' in parsed) return parsed
  const { raw } = parsed
  const actions: ProjectActionDraft[] = []
  for (const entry of (raw.actions as Record<string, unknown>[] | undefined) ?? []) {
    const name = text(entry.name)
    const command = text(entry.command)
    if (!name || !command) continue
    const icon = text(entry.icon)
    const platform = text(entry.platform)
    actions.push({
      name,
      icon: isProjectActionIcon(icon) ? icon : 'tool',
      command,
      ...(isProjectActionPlatform(platform) ? { platform } : {})
    })
  }
  return {
    document: {
      name: text(raw.name),
      setup: scriptFromFile(raw.setup),
      cleanup: scriptFromFile(raw.cleanup),
      actions
    }
  }
}

export function platformScript(script: EnvironmentScript, platform: NodeJS.Platform): string {
  return (isProjectActionPlatform(platform) ? script[platform] : undefined) || script.default
}

export function emptyEnvironmentDocument(name: string): EnvironmentDocument {
  return { name, setup: { default: '' }, cleanup: { default: '' }, actions: [] }
}

export function actionDraft(value: unknown): ProjectActionDraft | 'incomplete' | null {
  if (!isObject(value) || typeof value.name !== 'string' || typeof value.command !== 'string') return null
  if (!isProjectActionIcon(value.icon)) return null
  if (value.platform !== undefined && !isProjectActionPlatform(value.platform)) return null
  const name = value.name.trim()
  const command = value.command.trim()
  if (!name || !command) return 'incomplete'
  return { name, icon: value.icon, command, ...(value.platform ? { platform: value.platform } : {}) }
}

function scriptFromRequest(value: unknown): EnvironmentScript | null {
  if (!isObject(value) || typeof value.default !== 'string') return null
  const script: EnvironmentScript = { default: value.default.trim() }
  for (const platform of PROJECT_ACTION_PLATFORMS) {
    const override = value[platform]
    if (override === undefined) continue
    if (typeof override !== 'string') return null
    if (override.trim()) script[platform] = override.trim()
  }
  return script
}

export function normalizeEnvironmentDocument(value: unknown): EnvironmentDocument | null {
  if (!isObject(value) || typeof value.name !== 'string' || !Array.isArray(value.actions)) return null
  const setup = scriptFromRequest(value.setup)
  const cleanup = scriptFromRequest(value.cleanup)
  if (!setup || !cleanup) return null
  const actions: ProjectActionDraft[] = []
  for (const entry of value.actions) {
    const draft = actionDraft(entry)
    if (draft === null) return null
    if (draft !== 'incomplete') actions.push(draft)
  }
  return { name: value.name.trim(), setup, cleanup, actions }
}

function scriptToFile(script: EnvironmentScript): Record<string, unknown> | null {
  const overrides = PROJECT_ACTION_PLATFORMS.filter((platform) => script[platform])
  if (!script.default && overrides.length === 0) return null
  return {
    script: script.default,
    ...Object.fromEntries(overrides.map((platform) => [platform, { script: script[platform] }]))
  }
}

function existingObject(content: string | null): Record<string, unknown> {
  if (content === null) return {}
  try {
    const raw: unknown = JSON.parse(content.replace(/^﻿/, ''))
    return isObject(raw) ? raw : {}
  } catch {
    return {}
  }
}

export function serializeEnvironment(existing: string | null, document: EnvironmentDocument): string {
  const next: Record<string, unknown> = { version: 1, ...existingObject(existing) }
  next.version = 1
  if (document.name) next.name = document.name
  else delete next.name
  for (const key of ['setup', 'cleanup'] as const) {
    const script = scriptToFile(document[key])
    if (script) next[key] = script
    else delete next[key]
  }
  next.actions = document.actions.map((action) => ({
    name: action.name,
    icon: action.icon,
    command: action.command,
    ...(action.platform ? { platform: action.platform } : {})
  }))
  return `${JSON.stringify(next, null, 2)}\n`
}
