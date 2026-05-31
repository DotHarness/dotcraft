/**
 * Tests for the built-in explorer's directory listing (`listDirectory`):
 * dirs-first alpha ordering, `.git` filtering, relative-path shape, and the
 * not-a-directory error path.
 */
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { mkdtempSync, rmSync, writeFileSync, mkdirSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import { listDirectory } from '../viewerIpc'

const tempDirs: string[] = []

function createTempDir(): string {
  const dir = mkdtempSync(join(tmpdir(), 'viewer-listdir-test-'))
  tempDirs.push(dir)
  return dir
}

afterEach(() => {
  for (const dir of tempDirs.splice(0)) {
    rmSync(dir, { recursive: true, force: true })
  }
})

describe('listDirectory', () => {
  let root: string

  beforeEach(() => {
    root = createTempDir()
    mkdirSync(join(root, '.git'))
    mkdirSync(join(root, 'src'))
    mkdirSync(join(root, 'Build'))
    writeFileSync(join(root, 'README.md'), '# hi')
    writeFileSync(join(root, 'app.ts'), 'x')
  })

  it('lists immediate children, directories first then files, alpha-sorted', async () => {
    const { entries } = await listDirectory(root, root)
    expect(entries.map((e) => e.name)).toEqual(['Build', 'src', 'app.ts', 'README.md'])
  })

  it('skips the .git directory', async () => {
    const { entries } = await listDirectory(root, root)
    expect(entries.some((e) => e.name === '.git')).toBe(false)
  })

  it('reports workspace-relative POSIX paths and directory flags', async () => {
    const { entries } = await listDirectory(root, root)
    const src = entries.find((e) => e.name === 'src')!
    expect(src.isDir).toBe(true)
    expect(src.relativePath).toBe('src')
    const readme = entries.find((e) => e.name === 'README.md')!
    expect(readme.isDir).toBe(false)
    expect(readme.relativePath).toBe('README.md')
  })

  it('reports nested relative paths with forward slashes', async () => {
    mkdirSync(join(root, 'src', 'inner'))
    const { entries } = await listDirectory(join(root, 'src'), root)
    expect(entries.find((e) => e.name === 'inner')?.relativePath).toBe('src/inner')
  })

  it('throws when the target is not a directory', async () => {
    await expect(listDirectory(join(root, 'app.ts'), root)).rejects.toThrow()
  })
})
