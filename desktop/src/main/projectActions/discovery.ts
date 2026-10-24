import { createHash } from 'node:crypto'
import { existsSync } from 'node:fs'
import { readdir, readFile } from 'node:fs/promises'
import { basename, dirname, join, relative, resolve, sep } from 'node:path'
import {
  PROJECT_ACTION_PLATFORMS,
  isProjectActionIcon,
  isProjectActionPlatform,
  type ProjectAction,
  type ProjectEnvironment
} from '../../shared/projectActions'

const MAX_LEVELS = 50
export const DEFAULT_ENVIRONMENT_FILE = 'environment.json'

export interface DiscoveredEnvironment extends ProjectEnvironment {
  root: string
}

export function environmentFolders(workspacePath: string): string[] {
  const folders: string[] = []
  let folder = resolve(workspacePath)
  for (let level = 0; level < MAX_LEVELS; level++) {
    folders.push(folder)
    if (existsSync(join(folder, '.git'))) break
    const parent = dirname(folder)
    if (parent === folder) break
    folder = parent
  }
  return folders
}

export function repositoryRoot(workspacePath: string): string {
  const folders = environmentFolders(workspacePath)
  const top = folders[folders.length - 1]
  return existsSync(join(top, '.git')) ? top : folders[0]
}

export function environmentsDirectory(folder: string): string {
  return join(folder, '.craft', 'environments')
}

export function contentRevision(content: string): string {
  return createHash('sha256').update(content).digest('hex')
}

type ParsedEnvironment = { name?: string; actions: ProjectAction[] } | { error: string }

export function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export function text(value: unknown): string {
  return typeof value === 'string' ? value.trim() : ''
}

function scriptError(key: string, value: unknown): string | null {
  if (value === undefined) return null
  if (!isObject(value) || typeof value.script !== 'string') return `"${key}" must be an object with a string "script"`
  for (const platform of PROJECT_ACTION_PLATFORMS) {
    const override = value[platform]
    if (override !== undefined && (!isObject(override) || typeof override.script !== 'string')) {
      return `"${key}.${platform}" must be an object with a string "script"`
    }
  }
  return null
}

export function readEnvironmentObject(content: string): { raw: Record<string, unknown> } | { error: string } {
  let raw: unknown
  try {
    raw = JSON.parse(content.replace(/^﻿/, ''))
  } catch (error) {
    return { error: `Failed to parse: ${error instanceof Error ? error.message : String(error)}` }
  }
  if (!isObject(raw)) return { error: 'The file must contain a JSON object' }
  if (raw.version !== undefined && raw.version !== 1) return { error: '"version" must be 1' }
  if (raw.name !== undefined && typeof raw.name !== 'string') return { error: '"name" must be a string' }
  const scriptProblem = scriptError('setup', raw.setup) ?? scriptError('cleanup', raw.cleanup)
  if (scriptProblem) return { error: scriptProblem }
  if (raw.actions !== undefined && !Array.isArray(raw.actions)) return { error: '"actions" must be an array' }
  for (const [index, entry] of ((raw.actions as unknown[] | undefined) ?? []).entries()) {
    if (!isObject(entry)) return { error: `actions[${index}] must be an object` }
    if (entry.platform !== undefined && !isProjectActionPlatform(text(entry.platform))) {
      return { error: `actions[${index}].platform must be win32, darwin, or linux` }
    }
  }
  return { raw }
}

export function parseEnvironment(content: string, platform: NodeJS.Platform): ParsedEnvironment {
  const parsed = readEnvironmentObject(content)
  if ('error' in parsed) return parsed
  const { raw } = parsed
  const actions: ProjectAction[] = []
  const seen = new Map<string, number>()
  for (const entry of (raw.actions as Record<string, unknown>[] | undefined) ?? []) {
    const actionPlatform = text(entry.platform)
    const name = text(entry.name)
    const command = text(entry.command)
    if (!name || !command || (actionPlatform && actionPlatform !== platform)) continue
    const icon = text(entry.icon)
    let occurrence = (seen.get(name) ?? 0) + 1
    let id = occurrence === 1 ? name : `${name}#${occurrence}`
    while (actions.some((action) => action.id === id)) {
      occurrence++
      id = `${name}#${occurrence}`
    }
    seen.set(name, occurrence)
    actions.push({
      id,
      name,
      icon: isProjectActionIcon(icon) ? icon : 'tool',
      command
    })
  }
  return { name: text(raw.name) || undefined, actions }
}

function environmentId(workspacePath: string, filePath: string): string {
  return relative(resolve(workspacePath), filePath).split(sep).join('/')
}

async function readEnvironmentFiles(folder: string): Promise<string[]> {
  try {
    const entries = await readdir(environmentsDirectory(folder), { withFileTypes: true })
    return entries
      .filter((entry) => entry.isFile() && entry.name.toLowerCase().endsWith('.json'))
      .map((entry) => join(environmentsDirectory(folder), entry.name))
      .sort((a, b) => a.localeCompare(b))
  } catch {
    return []
  }
}

export async function discoverProjectEnvironments(
  workspacePath: string,
  platform: NodeJS.Platform = process.platform
): Promise<DiscoveredEnvironment[]> {
  const found: DiscoveredEnvironment[] = []
  const distances = new Map<DiscoveredEnvironment, number>()
  for (const [distance, folder] of environmentFolders(workspacePath).entries()) {
    for (const filePath of await readEnvironmentFiles(folder)) {
      let content = ''
      let parsed: ParsedEnvironment
      try {
        content = await readFile(filePath, 'utf8')
        parsed = parseEnvironment(content, platform)
      } catch (error) {
        parsed = { error: `Failed to read: ${error instanceof Error ? error.message : String(error)}` }
      }
      const environment: DiscoveredEnvironment = {
        id: environmentId(workspacePath, filePath),
        name: ('name' in parsed && parsed.name) || basename(filePath, '.json'),
        path: filePath,
        root: folder,
        isDefault: false,
        ...('error' in parsed ? { error: parsed.error, actions: [] } : { actions: parsed.actions }),
        revision: contentRevision(content)
      }
      distances.set(environment, distance)
      found.push(environment)
    }
  }

  const nearestDefault = found.find((environment) => basename(environment.path).toLowerCase() === DEFAULT_ENVIRONMENT_FILE)
  if (nearestDefault) nearestDefault.isDefault = true
  return found.sort((a, b) =>
    Number(b.isDefault) - Number(a.isDefault)
    || distances.get(a)! - distances.get(b)!
    || a.path.localeCompare(b.path))
}
