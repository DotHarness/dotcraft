import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { basename, dirname, join, resolve } from 'node:path'
import {
  isProjectActionIcon,
  isProjectActionPlatform,
  type ProjectActionDraft
} from '../../shared/projectActions'
import { contentRevision, environmentsDirectory, type DiscoveredEnvironment } from './discovery'

export type WriteProjectActionResult =
  | { ok: true; path: string }
  | { ok: false; reason: 'conflict' | 'failed' }

function validDraft(draft: ProjectActionDraft): Record<string, string> | null {
  const name = typeof draft?.name === 'string' ? draft.name.trim() : ''
  const command = typeof draft?.command === 'string' ? draft.command.trim() : ''
  if (!name || !command || !isProjectActionIcon(draft.icon)) return null
  if (draft.platform !== undefined && !isProjectActionPlatform(draft.platform)) return null
  return { name, icon: draft.icon, command, ...(draft.platform ? { platform: draft.platform } : {}) }
}

function serialize(document: Record<string, unknown>): string {
  return `${JSON.stringify(document, null, 2)}\n`
}

function isErrorCode(error: unknown, code: string): boolean {
  return (error as NodeJS.ErrnoException | null)?.code === code
}

export async function writeProjectAction(params: {
  workspacePath: string
  target: DiscoveredEnvironment | null
  revision: string | null
  draft: ProjectActionDraft
}): Promise<WriteProjectActionResult> {
  const action = validDraft(params.draft)
  if (!action) return { ok: false, reason: 'failed' }

  if (!params.target) {
    if (params.revision !== null) return { ok: false, reason: 'conflict' }
    const path = join(environmentsDirectory(resolve(params.workspacePath)), 'environment.json')
    try {
      await mkdir(dirname(path), { recursive: true })
      await writeFile(path, serialize({ version: 1, name: basename(resolve(params.workspacePath)), actions: [action] }), {
        encoding: 'utf8',
        flag: 'wx'
      })
      return { ok: true, path }
    } catch (error) {
      return { ok: false, reason: isErrorCode(error, 'EEXIST') ? 'conflict' : 'failed' }
    }
  }

  const path = params.target.path
  let content: string
  try {
    content = await readFile(path, 'utf8')
  } catch (error) {
    return { ok: false, reason: isErrorCode(error, 'ENOENT') ? 'conflict' : 'failed' }
  }
  if (contentRevision(content) !== params.revision) return { ok: false, reason: 'conflict' }

  let document: unknown
  try {
    document = JSON.parse(content.replace(/^﻿/, ''))
  } catch {
    return { ok: false, reason: 'failed' }
  }
  if (!document || typeof document !== 'object' || Array.isArray(document)) return { ok: false, reason: 'failed' }
  const existing = (document as Record<string, unknown>).actions
  if (existing !== undefined && !Array.isArray(existing)) return { ok: false, reason: 'failed' }
  try {
    await writeFile(path, serialize({ ...document, actions: [...(existing ?? []), action] }), 'utf8')
    return { ok: true, path }
  } catch {
    return { ok: false, reason: 'failed' }
  }
}
