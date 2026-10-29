import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mkdir, mkdtemp, readdir, readFile, rm, writeFile } from 'fs/promises'
import { existsSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import { setLocalChatsFolderRootsResolver } from '../chatFolderAccess'

const fakeHome = vi.hoisted(() => ({ path: '' }))

vi.mock('os', async (importOriginal) => {
  const actual = await importOriginal<typeof import('os')>()
  return { ...actual, homedir: () => fakeHome.path || actual.homedir() }
})
import {
  cleanupWorkspaceCache,
  ensureFileIndex,
  invalidateFileIndex,
  listWorkspaceFiles,
  readImageAsDataUrl,
  shouldInvalidateFileIndexForWatchEvent
} from '../workspaceComposerIpc'

describe('chat folder file index', () => {
  let tempRoot = ''

  afterEach(async () => {
    invalidateFileIndex()
    setLocalChatsFolderRootsResolver(() => [])
    fakeHome.path = ''
    if (tempRoot) await rm(tempRoot, { recursive: true, force: true })
    tempRoot = ''
  })

  it('keeps the index cache of a chat folder in the Chats workspace instead of the folder', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-chat-index-'))
    fakeHome.path = join(tempRoot, 'home')
    const chatsRoot = join(tempRoot, 'Chats')
    const chatFolder = join(chatsRoot, '2026-10-11', 'plan-a-trip')
    await mkdir(join(chatFolder, 'outputs'), { recursive: true })
    await writeFile(join(chatFolder, 'outputs', 'itinerary.md'), '# Trip', 'utf8')
    setLocalChatsFolderRootsResolver(() => [chatsRoot])

    const entries = await ensureFileIndex(chatFolder)

    expect(entries.map((entry) => entry.relativePath)).toContain('outputs/itinerary.md')
    expect(existsSync(join(chatFolder, '.craft'))).toBe(false)
    const cacheDir = join(fakeHome.path, '.craft', 'workspaces', 'chats', '.craft', 'cache')
    expect((await readdir(cacheDir)).some((name) => name.startsWith('desktop-file-index-v1-'))).toBe(true)
  }, 20_000)
})

describe('workspace image compatibility', () => {
  it('reads an existing image from the workspace tmp directory', async () => {
    const workspace = await mkdtemp(join(tmpdir(), 'dotcraft-workspace-image-'))
    try {
      const imageDir = join(workspace, '.craft', 'tmp', 'images')
      await mkdir(imageDir, { recursive: true })
      const imagePath = join(imageDir, 'legacy.png')
      await writeFile(imagePath, Buffer.from([0x89, 0x50, 0x4e, 0x47]))

      expect(await readImageAsDataUrl(workspace, imagePath)).toBe('data:image/png;base64,iVBORw==')
    } finally {
      await rm(workspace, { recursive: true, force: true })
    }
  })
})

describe('workspace file index watch events', () => {
  it('ignores DotCraft-owned state but invalidates for source changes and unknown filenames', () => {
    expect(shouldInvalidateFileIndexForWatchEvent('.craft/cache/desktop-file-index-v1.json')).toBe(false)
    expect(shouldInvalidateFileIndexForWatchEvent('.craft\\cache\\sessions.json')).toBe(false)
    expect(shouldInvalidateFileIndexForWatchEvent(Buffer.from('.craft/cache/state.json'))).toBe(false)
    expect(shouldInvalidateFileIndexForWatchEvent('src/main/index.ts')).toBe(true)
    expect(shouldInvalidateFileIndexForWatchEvent('.craft-notes.md')).toBe(true)
    expect(shouldInvalidateFileIndexForWatchEvent(null)).toBe(true)
  })
})

describe('workspace composer cache cleanup', () => {
  let tempRoot = ''

  afterEach(async () => {
    if (tempRoot) await rm(tempRoot, { recursive: true, force: true })
    tempRoot = ''
  })

  const otherCache = JSON.stringify({ SchemaVersion: 1 })

  it.each([
    ['desktop-file-index-v1.json', '{"schemaVersion":999}', false],
    ['other-cache.json.tmp', otherCache, false],
    ['other-cache.json', '{not-json', true]
  ])('cleans %s with content %#: kept=%s', async (fileName, content, kept) => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-workspace-cache-'))
    const cacheDir = join(tempRoot, '.craft', 'cache')
    await mkdir(cacheDir, { recursive: true })
    await writeFile(join(cacheDir, fileName), content, 'utf8')

    await cleanupWorkspaceCache(tempRoot)

    expect(existsSync(join(cacheDir, fileName))).toBe(kept)
  })
})

describe('workspace file index build', () => {
  let tempRoot = ''
  const savedEnv: Record<string, string | undefined> = {}

  function rememberEnv(name: string): void {
    savedEnv[name] = process.env[name]
  }

  function restoreEnv(): void {
    for (const [name, value] of Object.entries(savedEnv)) {
      if (value === undefined) delete process.env[name]
      else process.env[name] = value
    }
    for (const k of Object.keys(savedEnv)) delete savedEnv[k]
  }

  beforeEach(() => {
    invalidateFileIndex()
  })

  afterEach(async () => {
    invalidateFileIndex()
    restoreEnv()
    if (tempRoot) await rm(tempRoot, { recursive: true, force: true })
    tempRoot = ''
  })

  async function buildFixture(): Promise<string> {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-file-index-'))
    await writeFile(join(tempRoot, '.gitignore'), '*.bin\n', 'utf8')
    await mkdir(join(tempRoot, 'Source'), { recursive: true })
    await writeFile(join(tempRoot, 'Source', 'Real.cs'), 'class Real {}\n', 'utf8')
    await writeFile(join(tempRoot, 'Source', 'big.bin'), 'binary\n', 'utf8')
    await mkdir(join(tempRoot, '.craft', 'cache'), { recursive: true })
    await writeFile(join(tempRoot, '.craft', 'cache', 'foo.json'), '{}', 'utf8')
    await mkdir(join(tempRoot, 'Intermediate'), { recursive: true })
    await writeFile(join(tempRoot, 'Intermediate', 'x.cpp'), 'int x;\n', 'utf8')
    return tempRoot
  }

  function expectIndexedPaths(entries: Array<{ relativePath: string }>): void {
    const paths = entries.map((e) => e.relativePath)
    expect(paths).toContain('.gitignore')
    expect(paths).toContain('Source/Real.cs')
    expect(paths).toContain('Intermediate/x.cpp')
    expect(paths).not.toContain('Source/big.bin')
    expect(paths.some((p) => p.startsWith('.craft/'))).toBe(false)
  }

  async function readIndexCache(root: string): Promise<{ source?: string; entries: unknown[] }> {
    return JSON.parse(await readFile(join(root, '.craft', 'cache', 'desktop-file-index-v1.json'), 'utf8'))
  }

  it('builds the index honouring .gitignore and excluding .craft/', async () => {
    const root = await buildFixture()
    const entries = await ensureFileIndex(root)

    expectIndexedPaths(entries)
    const cache = await readIndexCache(root)
    expect(cache.entries.length).toBe(entries.length)
    expect(['rg', 'walk']).toContain(cache.source)
  }, 20_000)

  it('reports building, then falls back to ignore-walk when the ripgrep binary is missing', async () => {
    rememberEnv('DOTCRAFT_RG_PATH_OVERRIDE')
    process.env.DOTCRAFT_RG_PATH_OVERRIDE = join(tmpdir(), `nonexistent-rg-${Date.now()}`)
    const root = await buildFixture()

    const first = await listWorkspaceFiles(root, 'Real', 5)
    expect(first.indexStatus).toBe('building')
    expect(first.stale).toBe(true)

    expectIndexedPaths(await ensureFileIndex(root))
    expect((await readIndexCache(root)).source).toBe('walk')
    const second = await listWorkspaceFiles(root, 'Real', 5)
    expect(second.indexStatus).toBe('ready')
    expect(second.files.some((f) => f.relativePath === 'Source/Real.cs')).toBe(true)
  })

  it('reports ready after building an empty workspace index', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-empty-file-index-'))
    await mkdir(join(tempRoot, '.craft', 'cache'), { recursive: true })

    const entries = await ensureFileIndex(tempRoot)
    expect(entries).toHaveLength(0)

    const result = await listWorkspaceFiles(tempRoot, 'anything', 5)
    expect(result).toEqual({
      files: [],
      indexStatus: 'ready',
      indexedCount: 0,
      stale: false
    })
  })

  it('recovers from a worker timeout by retrying on the next call', async () => {
    rememberEnv('DOTCRAFT_INDEX_BUILD_TIMEOUT_MS')
    rememberEnv('DOTCRAFT_RG_QUIET_FALLBACK_MS')
    process.env.DOTCRAFT_INDEX_BUILD_TIMEOUT_MS = '1'
    process.env.DOTCRAFT_RG_QUIET_FALLBACK_MS = '60000'

    const root = await buildFixture()
    await expect(ensureFileIndex(root)).rejects.toThrow(/timed out/i)

    delete process.env.DOTCRAFT_INDEX_BUILD_TIMEOUT_MS
    delete process.env.DOTCRAFT_RG_QUIET_FALLBACK_MS
    invalidateFileIndex()
    const entries = await ensureFileIndex(root)
    expect(entries.some((e) => e.relativePath === 'Source/Real.cs')).toBe(true)
  }, 20_000)
})
