import { promises as fs } from 'fs'
import * as path from 'path'
import {
  listWorkspaceFiles,
  type FileListResultWire
} from './workspaceComposerIpc'
import type {
  ClassifyResult,
  DirEntryWire,
  ListDirResult,
  ReadTextResult,
  WriteTextParams,
  WriteTextResult,
  ViewerContentClass
} from '../shared/viewer/types'


export const EDITABLE_TEXT_LIMIT_BYTES = 10 * 1024 * 1024
export const MAX_TEXT_OPEN_BYTES = 20 * 1024 * 1024


const IMAGE_EXTENSIONS = new Set([
  '.png', '.jpg', '.jpeg', '.gif', '.webp', '.svg', '.bmp', '.ico', '.tiff', '.tif', '.avif'
])

const PDF_EXTENSION = '.pdf'

/** Well-known text / source extensions (non-exhaustive; fallback uses magic byte check). */
const TEXT_EXTENSIONS = new Set([
  '.ts', '.tsx', '.js', '.jsx', '.mjs', '.cjs',
  '.json', '.jsonc', '.json5',
  '.md', '.mdx', '.txt', '.rst', '.adoc',
  '.css', '.scss', '.less', '.sass',
  '.html', '.htm', '.xml', '.xhtml', '.svg',
  '.yaml', '.yml', '.toml', '.ini', '.cfg', '.conf', '.env',
  '.py', '.pyi', '.pyx',
  '.rs', '.go', '.java', '.kt', '.kts',
  '.c', '.h', '.cpp', '.cc', '.cxx', '.hpp', '.hxx',
  '.cs', '.vb', '.fs', '.fsx',
  '.rb', '.php', '.swift', '.dart',
  '.sh', '.bash', '.zsh', '.fish', '.ps1', '.psm1', '.bat', '.cmd',
  '.sql', '.graphql', '.gql',
  '.proto', '.thrift',
  '.lua', '.r', '.jl',
  '.tf', '.hcl',
  '.dockerfile', '.gitignore', '.gitattributes',
  '.editorconfig', '.eslintrc', '.prettierrc', '.babelrc',
  '.lock', '.log'
])

const PDF_MAGIC = Buffer.from([0x25, 0x50, 0x44, 0x46])

const PNG_MAGIC = Buffer.from([0x89, 0x50, 0x4e, 0x47])

const JPEG_MAGIC = Buffer.from([0xff, 0xd8, 0xff])

const GIF_MAGIC_87 = Buffer.from([0x47, 0x49, 0x46, 0x38, 0x37, 0x61])
const GIF_MAGIC_89 = Buffer.from([0x47, 0x49, 0x46, 0x38, 0x39, 0x61])

const RIFF_MAGIC = Buffer.from([0x52, 0x49, 0x46, 0x46])

function startsWithBytes(buf: Buffer, magic: Buffer): boolean {
  if (buf.length < magic.length) return false
  return magic.equals(buf.subarray(0, magic.length))
}

function isWebp(buf: Buffer): boolean {
  return buf.length >= 12 &&
    startsWithBytes(buf, RIFF_MAGIC) &&
    buf.subarray(8, 12).toString('ascii') === 'WEBP'
}

function sniffMagicClass(header: Buffer): ViewerContentClass | null {
  if (startsWithBytes(header, PDF_MAGIC)) return 'pdf'
  if (startsWithBytes(header, PNG_MAGIC)) return 'image'
  if (startsWithBytes(header, JPEG_MAGIC)) return 'image'
  if (startsWithBytes(header, GIF_MAGIC_87) || startsWithBytes(header, GIF_MAGIC_89)) return 'image'
  if (isWebp(header)) return 'image'
  return null
}

function extToMime(ext: string): string {
  const map: Record<string, string> = {
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.jpeg': 'image/jpeg',
    '.gif': 'image/gif',
    '.webp': 'image/webp',
    '.svg': 'image/svg+xml',
    '.bmp': 'image/bmp',
    '.ico': 'image/x-icon',
    '.pdf': 'application/pdf',
    '.txt': 'text/plain',
    '.html': 'text/html',
    '.css': 'text/css',
    '.js': 'text/javascript',
    '.json': 'application/json'
  }
  return map[ext] ?? 'application/octet-stream'
}


/** Both paths go through `fs.realpath` so symlinks and `..` cannot escape the root. */
export async function isPathInsideWorkspace(targetPath: string, workspaceRoot: string): Promise<boolean> {
  if (!workspaceRoot) return false
  try {
    const resolvedRoot = await fs.realpath(path.resolve(workspaceRoot))
    const resolvedTarget = await fs.realpath(path.resolve(targetPath))
    const sep = path.sep
    return (
      resolvedTarget === resolvedRoot ||
      resolvedTarget.startsWith(resolvedRoot + sep)
    )
  } catch {
    return false
  }
}


/** Extension is checked first; ambiguous files fall back to magic-byte sniffing. */
export async function classifyFile(
  absolutePath: string,
  workspaceRoot: string
): Promise<ClassifyResult> {
  // Deep links are allowed to target readable local files outside workspace.
  void workspaceRoot

  const stat = await fs.stat(absolutePath)
  if (!stat.isFile()) {
    throw new Error(`Not a file: ${absolutePath}`)
  }

  const ext = path.extname(absolutePath).toLowerCase()
  const sizeBytes = stat.size

  if (ext === PDF_EXTENSION) {
    return { contentClass: 'pdf', mime: 'application/pdf', sizeBytes }
  }

  if (IMAGE_EXTENSIONS.has(ext)) {
    return { contentClass: 'image', mime: extToMime(ext), sizeBytes }
  }

  if (TEXT_EXTENSIONS.has(ext)) {
    return { contentClass: 'text', mime: extToMime(ext), sizeBytes }
  }

  try {
    const fh = await fs.open(absolutePath, 'r')
    const header = Buffer.alloc(16)
    const { bytesRead } = await fh.read(header, 0, 16, 0)
    await fh.close()
    const buf = header.subarray(0, bytesRead)
    const sniffed = sniffMagicClass(buf)
    if (sniffed) {
      return { contentClass: sniffed, mime: extToMime(ext) || 'application/octet-stream', sizeBytes }
    }

    let likelyText = true
    for (let i = 0; i < buf.length; i++) {
      const b = buf[i]!
      if (b < 9 || (b > 13 && b < 32 && b !== 27)) {
        likelyText = false
        break
      }
    }
    if (likelyText) {
      return { contentClass: 'text', mime: 'text/plain', sizeBytes }
    }
  } catch {
    // If we can't read the header, fall through to unsupported
  }

  return { contentClass: 'unsupported', mime: extToMime(ext) || 'application/octet-stream', sizeBytes }
}


/** A file over `limitBytes` yields its first `limitBytes` with `truncated` set. */
export async function readTextFile(
  absolutePath: string,
  workspaceRoot: string,
  limitBytes: number = MAX_TEXT_OPEN_BYTES
): Promise<ReadTextResult> {
  // Deep links are allowed to target readable local files outside workspace.
  void workspaceRoot

  limitBytes = Math.min(Math.max(1, limitBytes), MAX_TEXT_OPEN_BYTES)
  const handle = await fs.open(absolutePath, 'r')
  try {
    for (let attempt = 0; attempt < 3; attempt++) {
      const stat = await handle.stat()
      if (!stat.isFile()) throw new Error(`Not a file: ${absolutePath}`)
      if (stat.size > MAX_TEXT_OPEN_BYTES) throw new Error('File is larger than the 20 MiB viewer limit')
      const buffer = Buffer.alloc(Math.min(stat.size, limitBytes))
      let offset = 0
      while (offset < buffer.length) {
        const { bytesRead } = await handle.read(buffer, offset, buffer.length - offset, offset)
        if (!bytesRead) break
        offset += bytesRead
      }
      const latest = await handle.stat()
      if (latest.size !== stat.size || latest.mtimeMs !== stat.mtimeMs) continue
      const hasUtf8Bom = buffer.length >= 3 && buffer[0] === 0xef && buffer[1] === 0xbb && buffer[2] === 0xbf
      const text = new TextDecoder('utf-8', { fatal: false }).decode(buffer.subarray(0, offset))
      return {
        text,
        truncated: stat.size > limitBytes,
        encoding: 'utf-8',
        sizeBytes: stat.size,
        mtimeMs: stat.mtimeMs,
        hasUtf8Bom,
        lineEnding: detectLineEnding(text),
        ...(stat.size > EDITABLE_TEXT_LIMIT_BYTES ? { readOnlyReason: 'large-file' as const } : {})
      }
    }
    throw new Error('File changed while reading')
  } finally { await handle.close() }
}

const textWrites = new Map<string, Promise<unknown>>()

export async function writeTextFile(
  params: WriteTextParams,
  workspaceRoot: string,
  authorize?: (absolutePath: string) => Promise<string>
): Promise<WriteTextResult> {
  const validate = authorize ?? (async (target: string) => {
    await fs.stat(target)
    if (!await isPathInsideWorkspace(target, workspaceRoot)) throw new Error('Viewer access denied')
    return fs.realpath(target)
  })
  const resolved = await validate(params.absolutePath)
  const previous = textWrites.get(resolved) ?? Promise.resolve()
  const task = previous.catch(() => {}).then(async (): Promise<WriteTextResult> => {
    const currentPath = await validate(params.absolutePath)
    if (currentPath !== resolved) throw new Error('Viewer file target changed')
    const handle = await fs.open(resolved, 'r+')
    try {
      const stat = await handle.stat()
      if (!stat.isFile()) throw new Error(`Not a file: ${resolved}`)
      if (stat.size > MAX_TEXT_OPEN_BYTES) throw new Error('File is larger than the 20 MiB viewer limit')
      if (stat.mtimeMs !== params.expectedMtimeMs) {
        return { outcome: 'conflict', current: await readTextFile(resolved, workspaceRoot) }
      }
      const normalized = params.text.replace(/\r\n?/g, '\n')
      const separator = params.lineEnding === 'crlf' ? '\r\n' : params.lineEnding === 'cr' ? '\r' : '\n'
      const content = Buffer.from(`${params.hasUtf8Bom ? '\uFEFF' : ''}${normalized.replace(/\n/g, separator)}`, 'utf8')
      if (content.byteLength > MAX_TEXT_OPEN_BYTES) throw new Error('File is larger than the 20 MiB viewer limit')
      if (await validate(params.absolutePath) !== resolved) throw new Error('Viewer file target changed')
      const latest = await fs.stat(resolved)
      if (latest.ino !== stat.ino || latest.dev !== stat.dev || latest.mtimeMs !== stat.mtimeMs) {
        return { outcome: 'conflict', current: await readTextFile(resolved, workspaceRoot) }
      }
      await handle.writeFile(content)
      await handle.truncate(content.length)
      const updated = await handle.stat()
      return { outcome: 'saved', mtimeMs: updated.mtimeMs, sizeBytes: updated.size }
    } finally { await handle.close() }
  })
  textWrites.set(resolved, task)
  try { return await task } finally { if (textWrites.get(resolved) === task) textWrites.delete(resolved) }
}

function detectLineEnding(text: string): 'lf' | 'crlf' | 'cr' {
  if (text.includes('\r\n')) return 'crlf'
  if (text.includes('\r')) return 'cr'
  return 'lf'
}


/** Directory names hidden from the explorer tree regardless of git status. */
const EXPLORER_SKIP_NAMES = new Set(['.git'])

/**
 * Deliberately NOT gitignore-filtered, so ignored dirs stay browsable like a native
 * file tree; only `.git` is skipped. Symlinked directories are reported as files so
 * the tree cannot follow links outside the workspace.
 */
export async function listDirectory(
  absoluteDir: string,
  workspaceRoot: string
): Promise<ListDirResult> {
  const stat = await fs.stat(absoluteDir)
  if (!stat.isDirectory()) {
    throw new Error(`Not a directory: ${absoluteDir}`)
  }

  const dirents = await fs.readdir(absoluteDir, { withFileTypes: true })
  const wsRoot = path.resolve(workspaceRoot)
  const entries: DirEntryWire[] = []

  for (const dirent of dirents) {
    const name = dirent.name
    if (EXPLORER_SKIP_NAMES.has(name)) continue
    const absolutePath = path.join(absoluteDir, name)
    const isDir = dirent.isDirectory()
    const relativePath = path.relative(wsRoot, absolutePath).replace(/\\/g, '/')
    entries.push({ name, relativePath, absolutePath, isDir })
  }

  entries.sort((a, b) => {
    if (a.isDir !== b.isDir) return a.isDir ? -1 : 1
    return a.name.localeCompare(b.name, undefined, { sensitivity: 'base' })
  })

  return { dirPath: path.resolve(absoluteDir), entries }
}


export async function listViewerFiles(
  workspaceRoot: string,
  query: string,
  limit: number
): Promise<FileListResultWire> {
  if (!workspaceRoot) {
    return { files: [], indexStatus: 'empty', indexedCount: 0, stale: false }
  }

  return listWorkspaceFiles(workspaceRoot, query, limit)
}
