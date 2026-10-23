import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { mkdtempSync, rmSync, writeFileSync, mkdirSync, truncateSync, utimesSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import {
  EDITABLE_TEXT_LIMIT_BYTES,
  MAX_TEXT_OPEN_BYTES,
  classifyFile,
  readTextFile,
  writeTextFile,
  isPathInsideWorkspace
} from '../viewerIpc'

const tempDirs: string[] = []

function createTempDir(): string {
  const dir = mkdtempSync(join(tmpdir(), 'viewer-ipc-test-'))
  tempDirs.push(dir)
  return dir
}

afterEach(() => {
  for (const dir of tempDirs.splice(0)) {
    rmSync(dir, { recursive: true, force: true })
  }
})

describe('isPathInsideWorkspace', () => {
  it('accepts existing nested files and rejects siblings, traversal, missing targets, and an empty root', async () => {
    const root = createTempDir()
    mkdirSync(join(root, 'src', 'utils'), { recursive: true })
    const nested = join(root, 'src', 'utils', 'helper.ts')
    writeFileSync(nested, '')
    const sibling = join(createTempDir(), 'secret.ts')
    writeFileSync(sibling, 'secret')

    expect(await isPathInsideWorkspace(nested, root)).toBe(true)
    expect(await isPathInsideWorkspace(sibling, root)).toBe(false)
    expect(await isPathInsideWorkspace(join(root, '..', 'escape.ts'), root)).toBe(false)
    expect(await isPathInsideWorkspace(join(root, 'missing.ts'), root)).toBe(false)
    expect(await isPathInsideWorkspace(nested, '')).toBe(false)
  })
})

describe('classifyFile', () => {
  it('classifies by extension and magic bytes, falling back to text or unsupported', async () => {
    const root = createTempDir()
    const cases: Array<[string, Buffer, string]> = [
      ['index.ts', Buffer.from('export {}'), 'text'],
      ['logo.png', Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), 'image'],
      ['report.pdf', Buffer.from([0x25, 0x50, 0x44, 0x46, 0x2d]), 'pdf'],
      ['nodoc', Buffer.from([0x25, 0x50, 0x44, 0x46, 0x2d, 0x31, 0x2e, 0x34]), 'pdf'],
      ['jpeg_no_ext', Buffer.from([0xff, 0xd8, 0xff, 0xe1, 0x00, 0x18]), 'image'],
      ['gif_no_ext', Buffer.from([0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00]), 'image'],
      ['plainfile', Buffer.from('just some plain text content here\n'), 'text'],
      ['binary_blob', Buffer.from([0x00, 0x01, 0x02, 0x03, 0x04, 0x80, 0x90, 0xff]), 'unsupported']
    ]
    const outside = createTempDir()
    for (const [name, bytes, expected] of cases) {
      const file = join(outside, name)
      writeFileSync(file, bytes)

      await expect(classifyFile(file, root), name).resolves.toMatchObject({ contentClass: expected, sizeBytes: bytes.length })
    }
  })
})

describe('readTextFile', () => {
  let root: string

  beforeEach(() => {
    root = createTempDir()
  })

  it('reads UTF-8 files outside the workspace and truncates only past the limit', async () => {
    const content = '// 你好世界\nexport const hello = "世界"\n'
    const f = join(createTempDir(), 'chinese.ts')
    writeFileSync(f, content, 'utf-8')
    const size = Buffer.byteLength(content)

    const result = await readTextFile(f, root)
    expect(result).toMatchObject({
      text: content,
      truncated: false,
      encoding: 'utf-8',
      sizeBytes: size,
      lineEnding: 'lf',
      hasUtf8Bom: false
    })
    expect(result.mtimeMs).toBeGreaterThan(0)
    await expect(readTextFile(f, root, size)).resolves.toMatchObject({ truncated: false })
    await expect(readTextFile(f, root, 9)).resolves.toMatchObject({ truncated: true, text: content.slice(0, 5) })
  })

  it('keeps the 10 MiB boundary editable and opens the next byte read-only', async () => {
    const editable = join(root, 'editable.txt')
    const readOnly = join(root, 'read-only.txt')
    writeFileSync(editable, '')
    writeFileSync(readOnly, '')
    truncateSync(editable, EDITABLE_TEXT_LIMIT_BYTES)
    truncateSync(readOnly, EDITABLE_TEXT_LIMIT_BYTES + 1)

    await expect(readTextFile(editable, root)).resolves.not.toHaveProperty('readOnlyReason')
    await expect(readTextFile(readOnly, root)).resolves.toMatchObject({ readOnlyReason: 'large-file' })
  })

  it('opens the 20 MiB boundary and rejects the next byte', async () => {
    const maximum = join(root, 'maximum.txt')
    const tooLarge = join(root, 'too-large.txt')
    writeFileSync(maximum, '')
    writeFileSync(tooLarge, '')
    truncateSync(maximum, MAX_TEXT_OPEN_BYTES)
    truncateSync(tooLarge, MAX_TEXT_OPEN_BYTES + 1)

    await expect(readTextFile(maximum, root)).resolves.toMatchObject({
      sizeBytes: MAX_TEXT_OPEN_BYTES,
      readOnlyReason: 'large-file'
    })
    await expect(readTextFile(tooLarge, root)).rejects.toThrow('20 MiB viewer limit')
  })
})

describe('writeTextFile', () => {
  let root: string

  beforeEach(() => {
    root = createTempDir()
  })

  it('preserves a UTF-8 BOM and CRLF line endings', async () => {
    const file = join(root, 'notes.txt')
    writeFileSync(file, '\uFEFFone\r\ntwo\r\n', 'utf8')
    const opened = await readTextFile(file, root)

    const result = await writeTextFile({
      absolutePath: file,
      text: 'one\nchanged\n',
      expectedMtimeMs: opened.mtimeMs,
      hasUtf8Bom: opened.hasUtf8Bom,
      lineEnding: opened.lineEnding
    }, root)

    expect(result.outcome).toBe('saved')
    const reopened = await readTextFile(file, root)
    expect(reopened.text).toBe('one\r\nchanged\r\n')
    expect(reopened.hasUtf8Bom).toBe(true)
    expect(reopened.lineEnding).toBe('crlf')
  })

  it('returns the current disk version when mtime changed', async () => {
    const file = join(root, 'notes.txt')
    writeFileSync(file, 'base', 'utf8')
    const opened = await readTextFile(file, root)
    writeFileSync(file, 'disk', 'utf8')
    utimesSync(file, new Date(), new Date(opened.mtimeMs + 1000))

    const result = await writeTextFile({
      absolutePath: file,
      text: 'local',
      expectedMtimeMs: opened.mtimeMs,
      hasUtf8Bom: false,
      lineEnding: 'lf'
    }, root)

    expect(result).toMatchObject({ outcome: 'conflict', current: { text: 'disk' } })
  })

  it('does not create a missing file', async () => {
    const file = join(root, 'missing.txt')
    await expect(writeTextFile({
      absolutePath: file,
      text: 'new',
      expectedMtimeMs: 0,
      hasUtf8Bom: false,
      lineEnding: 'lf'
    }, root)).rejects.toThrow('ENOENT')
  })

  it('allows an explicit review write after the disk file crosses 10 MiB', async () => {
    const file = join(root, 'grown.txt')
    writeFileSync(file, '')
    truncateSync(file, EDITABLE_TEXT_LIMIT_BYTES + 1)
    const disk = await readTextFile(file, root)
    await expect(writeTextFile({ absolutePath: file, text: 'accepted local version', expectedMtimeMs: disk.mtimeMs,
      hasUtf8Bom: false, lineEnding: 'lf' }, root)).resolves.toMatchObject({ outcome: 'saved' })
    expect((await readTextFile(file, root)).text).toBe('accepted local version')
  })

  it('rejects output beyond 20 MiB without modifying the existing file', async () => {
    const file = join(root, 'output-limit.txt')
    writeFileSync(file, 'base')
    const disk = await readTextFile(file, root)
    await expect(writeTextFile({ absolutePath: file, text: 'x'.repeat(MAX_TEXT_OPEN_BYTES + 1),
      expectedMtimeMs: disk.mtimeMs, hasUtf8Bom: false, lineEnding: 'lf' }, root)).rejects.toThrow('20 MiB')
    expect((await readTextFile(file, root)).text).toBe('base')
  })

  it('denies external writes unless the exact target is authorized again', async () => {
    const external = join(createTempDir(), 'external.txt')
    writeFileSync(external, 'base')
    const disk = await readTextFile(external, root)
    const request = { absolutePath: external, text: 'local', expectedMtimeMs: disk.mtimeMs, hasUtf8Bom: false, lineEnding: 'lf' as const }
    await expect(writeTextFile(request, root)).rejects.toThrow('access denied')
    let checks = 0
    await expect(writeTextFile(request, root, async (target) => {
      checks++
      if (target !== external || checks > 2) throw new Error('authorization revoked')
      return target
    })).rejects.toThrow('authorization revoked')
    expect((await readTextFile(external, root)).text).toBe('base')
  })
})
