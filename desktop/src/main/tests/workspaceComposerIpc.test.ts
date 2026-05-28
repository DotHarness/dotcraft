import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'fs/promises'
import { existsSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import {
  cleanupWorkspaceCache,
  ensureFileIndex,
  invalidateFileIndex,
  listWorkspaceFiles
} from '../workspaceComposerIpc'

describe('workspace composer cache cleanup', () => {
  let tempRoot = ''

  afterEach(async () => {
    if (tempRoot) await rm(tempRoot, { recursive: true, force: true })
    tempRoot = ''
  })

  async function createCacheDir(): Promise<string> {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-workspace-cache-'))
    const cacheDir = join(tempRoot, '.craft', 'cache')
    await mkdir(cacheDir, { recursive: true })
    return cacheDir
  }

  const pascalCaseWelcomeSuggestionsCache = JSON.stringify({
    SchemaVersion: 1,
    Result: {
      Source: 'dynamic',
      Fingerprint: 'abc123',
      GeneratedAt: '2026-05-06T00:00:00.0000000+00:00',
      Items: [
        {
          Title: 'Review cache cleanup',
          Prompt: 'Review Desktop welcome suggestion cache cleanup.'
        }
      ]
    }
  })

  it('removes invalid Desktop-owned file index caches', async () => {
    const cacheDir = await createCacheDir()
    const fileIndex = join(cacheDir, 'desktop-file-index-v1.json')
    await writeFile(fileIndex, '{"schemaVersion":999}', 'utf8')

    await cleanupWorkspaceCache(tempRoot)

    expect(existsSync(fileIndex)).toBe(false)
  })

  it('keeps PascalCase welcome suggestions persisted by AppServer', async () => {
    const cacheDir = await createCacheDir()
    const suggestions = join(cacheDir, 'welcome-suggestions.json')
    await writeFile(suggestions, pascalCaseWelcomeSuggestionsCache, 'utf8')

    await cleanupWorkspaceCache(tempRoot)

    expect(existsSync(suggestions)).toBe(true)
  })

  it('keeps BOM-prefixed welcome suggestions persisted by AppServer', async () => {
    const cacheDir = await createCacheDir()
    const suggestions = join(cacheDir, 'welcome-suggestions.json')
    await writeFile(suggestions, `\uFEFF${pascalCaseWelcomeSuggestionsCache}`, 'utf8')

    await cleanupWorkspaceCache(tempRoot)

    expect(existsSync(suggestions)).toBe(true)
  })

  it('keeps malformed welcome suggestions for AppServer to validate', async () => {
    const cacheDir = await createCacheDir()
    const suggestions = join(cacheDir, 'welcome-suggestions.json')
    await writeFile(suggestions, '{not-json', 'utf8')

    await cleanupWorkspaceCache(tempRoot)

    expect(existsSync(suggestions)).toBe(true)
  })

  it('removes welcome suggestions temp files', async () => {
    const cacheDir = await createCacheDir()
    const suggestionsTemp = join(cacheDir, 'welcome-suggestions.json.tmp')
    await writeFile(suggestionsTemp, pascalCaseWelcomeSuggestionsCache, 'utf8')

    await cleanupWorkspaceCache(tempRoot)

    expect(existsSync(suggestionsTemp)).toBe(false)
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

  it(
    'builds the index via ripgrep, honours .gitignore and excludes .craft/',
    async () => {
      const root = await buildFixture()
      const entries = await ensureFileIndex(root)
      const paths = entries.map((e) => e.relativePath).sort()

      expect(paths).toContain('.gitignore')
      expect(paths).toContain('Source/Real.cs')
      // Not ignored by user .gitignore, so must remain (no hardcoded blacklist).
      expect(paths).toContain('Intermediate/x.cpp')
      // Excluded by .gitignore (*.bin).
      expect(paths).not.toContain('Source/big.bin')
      // Always excluded — DotCraft owns this directory.
      expect(paths.some((p) => p.startsWith('.craft/'))).toBe(false)

      const cachePath = join(root, '.craft', 'cache', 'desktop-file-index-v1.json')
      expect(existsSync(cachePath)).toBe(true)
      const cacheRaw = await readFile(cachePath, 'utf8')
      const parsed = JSON.parse(cacheRaw) as { source?: string; entries: unknown[] }
      expect(parsed.entries.length).toBe(entries.length)
      // Either the rg main path or the ignore-walk fallback may have produced
      // the cache depending on whether ripgrep is available in this CI env;
      // both must mark the source explicitly.
      expect(parsed.source === 'rg' || parsed.source === 'walk').toBe(true)
    },
    20_000
  )

  it('falls back to ignore-walk when the ripgrep binary is missing', async () => {
    rememberEnv('DOTCRAFT_RG_PATH_OVERRIDE')
    process.env.DOTCRAFT_RG_PATH_OVERRIDE = join(tmpdir(), `nonexistent-rg-${Date.now()}`)

    const root = await buildFixture()
    const entries = await ensureFileIndex(root)
    const paths = entries.map((e) => e.relativePath).sort()

    expect(paths).toContain('.gitignore')
    expect(paths).toContain('Source/Real.cs')
    expect(paths).toContain('Intermediate/x.cpp')
    expect(paths).not.toContain('Source/big.bin')
    expect(paths.some((p) => p.startsWith('.craft/'))).toBe(false)

    const cacheRaw = await readFile(
      join(root, '.craft', 'cache', 'desktop-file-index-v1.json'),
      'utf8'
    )
    const parsed = JSON.parse(cacheRaw) as { source?: string; rgError?: string | null }
    expect(parsed.source).toBe('walk')
  })

  it('listWorkspaceFiles returns building status while the index has no entries yet', async () => {
    rememberEnv('DOTCRAFT_RG_PATH_OVERRIDE')
    process.env.DOTCRAFT_RG_PATH_OVERRIDE = join(tmpdir(), `nonexistent-rg-${Date.now()}`)

    const root = await buildFixture()
    // Don't await the build — first call should immediately observe building.
    const first = await listWorkspaceFiles(root, 'Real', 5)
    expect(first.indexStatus).toBe('building')
    expect(first.stale).toBe(true)

    // Now wait for completion via ensureFileIndex, then re-query.
    await ensureFileIndex(root)
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
    // Force the timeout to fire almost immediately so the test stays fast.
    rememberEnv('DOTCRAFT_INDEX_BUILD_TIMEOUT_MS')
    rememberEnv('DOTCRAFT_RG_QUIET_FALLBACK_MS')
    process.env.DOTCRAFT_INDEX_BUILD_TIMEOUT_MS = '1'
    // Keep the rg quiet-fallback long enough that the orchestrator timeout
    // wins the race instead of the worker reaching its own fallback path.
    process.env.DOTCRAFT_RG_QUIET_FALLBACK_MS = '60000'

    const root = await buildFixture()
    await expect(ensureFileIndex(root)).rejects.toThrow(/timed out/i)

    // Now drop the timeout override so the next call can complete normally.
    delete process.env.DOTCRAFT_INDEX_BUILD_TIMEOUT_MS
    delete process.env.DOTCRAFT_RG_QUIET_FALLBACK_MS
    invalidateFileIndex()
    const entries = await ensureFileIndex(root)
    expect(entries.some((e) => e.relativePath === 'Source/Real.cs')).toBe(true)
  }, 20_000)
})
