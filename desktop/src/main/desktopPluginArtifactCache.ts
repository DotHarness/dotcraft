import { createHash } from 'crypto'
import { promises as fs } from 'fs'
import * as path from 'path'
import { unzipSync } from 'fflate'
import type { PluginDesktopReadParams, PluginDesktopReadResult } from '@dotcraft/sdk/contracts'
import { readDesktopPluginBundle, type DesktopPluginModuleRequest } from './pluginFileProtocol'

const MAX_BYTES = 512 * 1024 * 1024
const MAX_ARCHIVE_BYTES = MAX_BYTES + 8 * 1024 * 1024
const MAX_MANIFEST_BYTES = 64 * 1024
const MAX_CHUNK_BYTES = 1024 * 1024
const MAX_CHUNK_BASE64_LENGTH = Math.ceil(MAX_CHUNK_BYTES / 3) * 4
const MAX_ENTRIES = 20_004

export type ArtifactReader = (params: PluginDesktopReadParams) => Promise<PluginDesktopReadResult>

export function desktopPluginSourceKey(source: string, workspacePath: string): string {
  return createHash('sha256').update(JSON.stringify([source, workspacePath])).digest('hex')
}

export class DesktopPluginArtifactCache {
  constructor(private readonly root: string) {}

  async prepare(source: string, request: DesktopPluginModuleRequest, read: ArtifactReader, current: () => boolean): Promise<string> {
    validateKey(source)
    validateKey(request.revision)
    if (!/^[a-z0-9][a-z0-9._-]*$/i.test(request.pluginId) || request.pluginId.includes('..')) throw new Error('Invalid plugin id.')
    const parent = path.join(this.root, source, request.pluginId.toLowerCase())
    const target = path.join(parent, request.revision)
    try {
      await verifyBundle(target, request)
      if (!current()) throw new Error('Desktop plugin source changed.')
      return target
    } catch {
      if (!current()) throw new Error('Desktop plugin source changed.')
    }
    await fs.mkdir(parent, { recursive: true })
    const staging = await fs.mkdtemp(path.join(parent, '.download-'))
    const archivePath = path.join(staging, 'artifact.zip')
    const bundlePath = path.join(staging, 'bundle')
    let complete = false
    try {
      const file = await fs.open(archivePath, 'wx')
      try {
        let offset = 0
        let total: number | undefined
        do {
          if (!current()) throw new Error('Desktop plugin source changed.')
          const chunk = await read({ id: request.pluginId, revision: request.revision, offset })
          if (!Number.isSafeInteger(chunk.totalBytes) || chunk.totalBytes <= 0 || chunk.totalBytes > MAX_ARCHIVE_BYTES
            || (total !== undefined && total !== chunk.totalBytes)) throw new Error('Invalid Desktop artifact length.')
          total = chunk.totalBytes
          if (typeof chunk.dataBase64 !== 'string' || chunk.dataBase64.length > MAX_CHUNK_BASE64_LENGTH) throw new Error('Invalid Desktop artifact chunk.')
          const bytes = Buffer.from(chunk.dataBase64, 'base64')
          if (!bytes.length || bytes.length > MAX_CHUNK_BYTES || offset + bytes.length > total) throw new Error('Invalid Desktop artifact chunk.')
          await file.writeFile(bytes)
          offset += bytes.length
        } while (offset < total)
        complete = true
      } finally { await file.close() }
      if (!current()) throw new Error('Desktop plugin source changed.')
      await extractDesktopArtifact(await fs.readFile(archivePath), bundlePath)
      await verifyBundle(bundlePath, request)
      if (!current()) throw new Error('Desktop plugin source changed.')
      await fs.rm(target, { recursive: true, force: true })
      if (!current()) throw new Error('Desktop plugin source changed.')
      await fs.rename(bundlePath, target)
      return target
    } finally {
      if (!complete) await read({ id: request.pluginId, revision: request.revision, offset: -1 }).catch(() => {})
      await fs.rm(staging, { recursive: true, force: true })
    }
  }

  async prune(source: string, keep: ReadonlyMap<string, ReadonlySet<string>>): Promise<void> {
    validateKey(source)
    const root = path.join(this.root, source)
    for (const plugin of await fs.readdir(root, { withFileTypes: true }).catch(() => [])) {
      if (!plugin.isDirectory() || plugin.isSymbolicLink()) continue
      const pluginRoot = path.join(root, plugin.name)
      for (const revision of await fs.readdir(pluginRoot)) {
        if (revision.startsWith('.download-') || keep.get(plugin.name)?.has(revision)) continue
        await fs.rm(path.join(pluginRoot, revision), { recursive: true, force: true })
      }
    }
  }
}

async function verifyBundle(root: string, request: DesktopPluginModuleRequest): Promise<void> {
  const bundle = await readDesktopPluginBundle(root)
  if (bundle.pluginId.toLowerCase() !== request.pluginId.toLowerCase() || bundle.revision !== request.revision) {
    throw new Error('Desktop artifact identity or revision does not match.')
  }
}

export async function extractDesktopArtifact(bytes: Buffer, destination: string): Promise<void> {
  const names = validateArchive(bytes)
  let total = 0
  const files = unzipSync(bytes, { filter(entry) {
    total += entry.originalSize
    if (total > MAX_BYTES + MAX_MANIFEST_BYTES) throw new Error('Desktop artifact exceeds the content limit.')
    if (!names.has(entry.name)) throw new Error('Desktop artifact headers disagree.')
    return true
  } })
  if (Object.keys(files).length !== names.size) throw new Error('Desktop artifact entries disagree.')
  await fs.mkdir(destination, { recursive: true })
  for (const [name, data] of Object.entries(files)) {
    const target = path.join(destination, ...name.split('/'))
    if (name.endsWith('/')) await fs.mkdir(target, { recursive: true })
    else {
      await fs.mkdir(path.dirname(target), { recursive: true })
      await fs.writeFile(target, data, { flag: 'wx' })
    }
  }
}

function validateArchive(bytes: Buffer): Set<string> {
  if (bytes.length < 22) throw new Error('Invalid Desktop artifact ZIP.')
  let end = bytes.length - 22
  while (end >= Math.max(0, bytes.length - 65_557) && bytes.readUInt32LE(end) !== 0x06054b50) end--
  if (end < 0 || bytes.readUInt32LE(end) !== 0x06054b50) throw new Error('Invalid Desktop artifact ZIP.')
  const count = bytes.readUInt16LE(end + 10)
  if (count > MAX_ENTRIES || bytes.readUInt16LE(end + 4) !== 0 || bytes.readUInt16LE(end + 6) !== 0) throw new Error('Unsupported Desktop artifact ZIP.')
  let offset = bytes.readUInt32LE(end + 16)
  const names = new Set<string>()
  const portableNames = new Set<string>()
  let total = 0
  for (let index = 0; index < count; index++) {
    if (offset + 46 > end || bytes.readUInt32LE(offset) !== 0x02014b50) throw new Error('Invalid Desktop artifact entry.')
    const nameSize = bytes.readUInt16LE(offset + 28)
    const name = bytes.subarray(offset + 46, offset + 46 + nameSize).toString('utf8')
    const segments = name.replace(/\/$/u, '').split('/')
    const allowed = name === '.craft-plugin/' || name === '.craft-plugin/plugin.json'
      || name === 'desktop/' || name === 'desktop/dist/' || name.startsWith('desktop/dist/')
    const mode = bytes.readUInt32LE(offset + 38) >>> 16
    if (!allowed || segments.length > 66 || segments.some(part => !part || part === '.' || part === '..'
      || /[\\:\x00-\x1f]/u.test(part) || /[. ]$/u.test(part) || /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/iu.test(part))
      || (mode & 0xf000) === 0xa000 || portableNames.has(name.toLowerCase())) throw new Error('Unsafe Desktop artifact entry.')
    const size = bytes.readUInt32LE(offset + 24)
    if ((name === '.craft-plugin/plugin.json' && size > MAX_MANIFEST_BYTES) || (name.endsWith('/') && size !== 0)) {
      throw new Error('Invalid Desktop artifact entry size.')
    }
    total += size
    if (total > MAX_BYTES + MAX_MANIFEST_BYTES) throw new Error('Desktop artifact exceeds the content limit.')
    names.add(name)
    portableNames.add(name.toLowerCase())
    offset += 46 + nameSize + bytes.readUInt16LE(offset + 30) + bytes.readUInt16LE(offset + 32)
  }
  if (!names.has('.craft-plugin/plugin.json')) throw new Error('Desktop artifact manifest is missing.')
  return names
}

function validateKey(value: string): void {
  if (!/^[0-9a-f]{64}$/u.test(value)) throw new Error('Invalid Desktop artifact key.')
}
