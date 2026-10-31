import { promises as fs } from 'fs'
import type { ReadPresentationResult } from '../../shared/viewer/presentation'
import { buildPresentation } from './slides'

const PRESENTATION_MAX_BYTES = 40 * 1024 * 1024
const CACHE_LIMIT = 3

interface CachedPresentation {
  stamp: string
  result: ReadPresentationResult
}

const cache = new Map<string, Promise<CachedPresentation>>()

export async function readPresentation(absolutePath: string): Promise<ReadPresentationResult> {
  const stat = await fs.stat(absolutePath)
  if (!stat.isFile()) throw new Error(`Not a file: ${absolutePath}`)
  if (stat.size > PRESENTATION_MAX_BYTES) throw new Error('File is larger than the 40 MiB presentation limit')
  const stamp = `${stat.mtimeMs}:${stat.size}`
  let entry = cache.get(absolutePath)
  if (!entry || (await entry.catch(() => null))?.stamp !== stamp) {
    const pending = fs.readFile(absolutePath).then((buffer) => ({ stamp, result: buildPresentation(buffer) }))
    entry = pending
    cache.set(absolutePath, pending)
    pending.catch(() => {
      if (cache.get(absolutePath) === pending) cache.delete(absolutePath)
    })
  } else {
    cache.delete(absolutePath)
    cache.set(absolutePath, entry)
  }
  while (cache.size > CACHE_LIMIT) cache.delete(cache.keys().next().value!)
  return (await entry).result
}
