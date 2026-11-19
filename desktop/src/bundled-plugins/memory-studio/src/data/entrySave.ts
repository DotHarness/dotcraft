import { joinHostPath } from './hostPath'
import { memoryFileName, parseMemoryFile, serializeMemoryFile, type MemoryFileFields } from './memoryFile'
import type { MemoryFs } from './memoryFs'
import { INDEX_FILE, removeIndexEntry, upsertIndexEntry } from './memoryIndex'

/** What the editor read when it opened an entry; a save proceeds only while the store still matches it. */
export interface EntrySnapshot {
  storePath: string
  /** `null` for a new entry. */
  fileName: string | null
  fileText: string | null
  indexText: string | null
}

export type EntryConflictReason = 'fileChanged' | 'indexChanged' | 'nameTaken'

export type EntryWriteOutcome =
  | { kind: 'saved'; snapshot: EntrySnapshot }
  | { kind: 'deleted' }
  | { kind: 'conflict'; reason: EntryConflictReason; current: EntrySnapshot }

export async function readSnapshot(fs: MemoryFs, storePath: string, fileName: string | null): Promise<EntrySnapshot> {
  const [fileText, indexText] = await Promise.all([
    fileName ? fs.readText(joinHostPath(storePath, fileName)) : Promise.resolve(null),
    fs.readText(joinHostPath(storePath, INDEX_FILE))
  ])
  return { storePath, fileName, fileText, indexText }
}

async function changedSince(fs: MemoryFs, snapshot: EntrySnapshot): Promise<{ reason: EntryConflictReason; current: EntrySnapshot } | null> {
  const current = await readSnapshot(fs, snapshot.storePath, snapshot.fileName)
  if (current.fileText !== snapshot.fileText) return { reason: 'fileChanged', current }
  if (current.indexText !== snapshot.indexText) return { reason: 'indexChanged', current }
  return null
}

export async function saveEntry(fs: MemoryFs, snapshot: EntrySnapshot, fields: MemoryFileFields): Promise<EntryWriteOutcome> {
  const changed = await changedSince(fs, snapshot)
  if (changed) return { kind: 'conflict', ...changed }

  const requested = memoryFileName(fields.name.trim())
  const renamed = snapshot.fileName == null || snapshot.fileName.toLowerCase() !== requested.toLowerCase()
  // A case-only rename keeps the file name, since a case-insensitive host would delete the file it just wrote.
  const fileName = renamed ? requested : snapshot.fileName!
  const path = joinHostPath(snapshot.storePath, fileName)
  if (renamed && await fs.readText(path) != null) {
    return { kind: 'conflict', reason: 'nameTaken', current: await readSnapshot(fs, snapshot.storePath, fileName) }
  }

  const previous = snapshot.fileText == null ? null : parseMemoryFile(snapshot.fileText)
  const fileText = serializeMemoryFile({ ...fields, name: fields.name.trim() }, previous?.extra)
  if (snapshot.fileName == null) await fs.ensureDirectory(snapshot.storePath)
  // The old file goes last, so a failure part-way leaves at most an orphan file, never a pointer to a missing one.
  await fs.writeText(path, fileText)
  const indexText = upsertIndexEntry(snapshot.indexText ?? '', {
    previousFile: snapshot.fileName ?? undefined,
    file: fileName,
    previousDescription: previous?.description,
    description: fields.description
  })
  if (indexText !== snapshot.indexText) await fs.writeText(joinHostPath(snapshot.storePath, INDEX_FILE), indexText)
  if (snapshot.fileName != null && snapshot.fileName !== fileName) await fs.remove(joinHostPath(snapshot.storePath, snapshot.fileName))
  return { kind: 'saved', snapshot: { storePath: snapshot.storePath, fileName, fileText, indexText } }
}

export async function deleteEntry(fs: MemoryFs, snapshot: EntrySnapshot): Promise<EntryWriteOutcome> {
  if (snapshot.fileName == null) return { kind: 'deleted' }
  const changed = await changedSince(fs, snapshot)
  if (changed) return { kind: 'conflict', ...changed }
  await fs.remove(joinHostPath(snapshot.storePath, snapshot.fileName))
  if (snapshot.indexText != null) {
    const indexText = removeIndexEntry(snapshot.indexText, snapshot.fileName)
    if (indexText !== snapshot.indexText) await fs.writeText(joinHostPath(snapshot.storePath, INDEX_FILE), indexText)
  }
  return { kind: 'deleted' }
}

export async function saveIndex(fs: MemoryFs, storePath: string, expected: string | null, next: string): Promise<{ kind: 'saved' } | { kind: 'conflict'; current: string | null }> {
  const path = joinHostPath(storePath, INDEX_FILE)
  const current = await fs.readText(path)
  if (current !== expected) return { kind: 'conflict', current }
  if (expected == null) await fs.ensureDirectory(storePath)
  await fs.writeText(path, next)
  return { kind: 'saved' }
}
