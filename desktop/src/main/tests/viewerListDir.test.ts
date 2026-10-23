import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { mkdtempSync, rmSync, writeFileSync, mkdirSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import { listDirectory } from '../viewerIpc'

describe('listDirectory', () => {
  let root: string

  beforeEach(() => {
    root = mkdtempSync(join(tmpdir(), 'viewer-listdir-test-'))
    mkdirSync(join(root, '.git'))
    mkdirSync(join(root, 'src', 'inner'), { recursive: true })
    mkdirSync(join(root, 'Build'))
    writeFileSync(join(root, 'README.md'), '# hi')
    writeFileSync(join(root, 'app.ts'), 'x')
  })

  afterEach(() => {
    rmSync(root, { recursive: true, force: true })
  })

  it('lists immediate children without .git, directories first, with workspace-relative POSIX paths', async () => {
    const { entries } = await listDirectory(root, root)

    expect(entries.map((e) => [e.name, e.isDir, e.relativePath])).toEqual([
      ['Build', true, 'Build'],
      ['src', true, 'src'],
      ['app.ts', false, 'app.ts'],
      ['README.md', false, 'README.md']
    ])

    const nested = await listDirectory(join(root, 'src'), root)
    expect(nested.entries.find((e) => e.name === 'inner')?.relativePath).toBe('src/inner')
  })

  it('throws when the target is not a directory', async () => {
    await expect(listDirectory(join(root, 'app.ts'), root)).rejects.toThrow()
  })
})
