import { existsSync } from 'node:fs'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { basename, dirname, isAbsolute, join, resolve } from 'node:path'
import type {
  EnvironmentDocument,
  ReadEnvironmentResult,
  WriteEnvironmentResult
} from '../../shared/projectActions'
import { contentRevision, DEFAULT_ENVIRONMENT_FILE, environmentFolders, environmentsDirectory } from './discovery'
import { documentFromContent, serializeEnvironment } from './environmentDocument'

function isErrorCode(error: unknown, code: string): boolean {
  return (error as NodeJS.ErrnoException | null)?.code === code
}

export function environmentFilePath(projectPath: string, path: unknown): string | null {
  if (typeof path !== 'string' || !isAbsolute(path)) return null
  const resolved = resolve(path)
  if (!basename(resolved).toLowerCase().endsWith('.json')) return null
  const directory = dirname(resolved)
  return environmentFolders(projectPath).some((folder) => environmentsDirectory(folder) === directory)
    ? resolved
    : null
}

export function newEnvironmentPath(projectPath: string): string {
  const directory = environmentsDirectory(projectPath)
  let path = join(directory, DEFAULT_ENVIRONMENT_FILE)
  for (let index = 2; existsSync(path); index++) path = join(directory, `environment-${index}.json`)
  return path
}

async function readContent(path: string): Promise<string | null> {
  try {
    return await readFile(path, 'utf8')
  } catch (error) {
    if (isErrorCode(error, 'ENOENT')) return null
    throw error
  }
}

export async function readEnvironmentFile(path: string): Promise<ReadEnvironmentResult> {
  let content: string | null
  try {
    content = await readContent(path)
  } catch {
    return { ok: false, reason: 'failed' }
  }
  if (content === null) return { ok: true, path, revision: null, document: null }
  const revision = contentRevision(content)
  const parsed = documentFromContent(content)
  return 'error' in parsed
    ? { ok: true, path, revision, document: null, error: parsed.error }
    : { ok: true, path, revision, document: parsed.document }
}

export async function writeEnvironmentFile(
  path: string,
  revision: string | null,
  document: EnvironmentDocument
): Promise<WriteEnvironmentResult> {
  let existing: string | null
  try {
    existing = await readContent(path)
  } catch {
    return { ok: false, reason: 'failed' }
  }
  if (existing === null ? revision !== null : revision !== contentRevision(existing)) {
    return { ok: false, reason: 'conflict' }
  }
  const content = serializeEnvironment(existing, document)
  try {
    if (existing === null) {
      await mkdir(dirname(path), { recursive: true })
      await writeFile(path, content, { encoding: 'utf8', flag: 'wx' })
    } else if (content !== existing) {
      await writeFile(path, content, 'utf8')
    }
    return { ok: true, path, revision: contentRevision(content) }
  } catch (error) {
    return { ok: false, reason: isErrorCode(error, 'EEXIST') ? 'conflict' : 'failed' }
  }
}
