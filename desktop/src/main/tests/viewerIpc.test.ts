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
  let root: string

  beforeEach(() => {
    root = createTempDir()
  })

  it('returns true for a direct child file', async () => {
    const file = join(root, 'hello.ts')
    writeFileSync(file, 'hello')
    expect(await isPathInsideWorkspace(file, root)).toBe(true)
  })

  it('returns true for a nested path', async () => {
    mkdirSync(join(root, 'src', 'utils'), { recursive: true })
    const file = join(root, 'src', 'utils', 'helper.ts')
    writeFileSync(file, '')
    expect(await isPathInsideWorkspace(file, root)).toBe(true)
  })

  it('returns false for a sibling directory', async () => {
    const sibling = createTempDir()
    const file = join(sibling, 'secret.ts')
    writeFileSync(file, 'secret')
    expect(await isPathInsideWorkspace(file, root)).toBe(false)
  })

  it('returns false for a path-traversal attempt', async () => {
    const outsideFile = join(root, '..', 'escape.ts')
    // The real file on disk is the parent's directory; just test with a non-existent path
    expect(await isPathInsideWorkspace(outsideFile, root)).toBe(false)
  })

  it('returns false when workspaceRoot is empty string', async () => {
    const file = join(root, 'a.ts')
    writeFileSync(file, '')
    expect(await isPathInsideWorkspace(file, '')).toBe(false)
  })

  it('returns false when target does not exist', async () => {
    const nonExistent = join(root, 'does-not-exist.ts')
    expect(await isPathInsideWorkspace(nonExistent, root)).toBe(false)
  })
})


describe('classifyFile — extension', () => {
  let root: string

  beforeEach(() => {
    root = createTempDir()
  })

  it('classifies .ts files as text', async () => {
    const f = join(root, 'index.ts')
    writeFileSync(f, 'export {}')
    const result = await classifyFile(f, root)
    expect(result.contentClass).toBe('text')
  })

  it('classifies .md files as text', async () => {
    const f = join(root, 'README.md')
    writeFileSync(f, '# Hello')
    const result = await classifyFile(f, root)
    expect(result.contentClass).toBe('text')
  })

  it('classifies .png files as image', async () => {
    const f = join(root, 'logo.png')
    // Write valid PNG magic bytes followed by padding
    const magic = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])
    writeFileSync(f, magic)
    const result = await classifyFile(f, root)
    expect(result.contentClass).toBe('image')
  })

  it('classifies .jpg / .jpeg files as image', async () => {
    const f = join(root, 'photo.jpg')
    writeFileSync(f, Buffer.from([0xff, 0xd8, 0xff, 0xe0]))
    const result = await classifyFile(f, root)
    expect(result.contentClass).toBe('image')
  })

  it('classifies .pdf extension as pdf', async () => {
    const f = join(root, 'report.pdf')
    writeFileSync(f, Buffer.from([0x25, 0x50, 0x44, 0x46, 0x2d]))
    const result = await classifyFile(f, root)
    expect(result.contentClass).toBe('pdf')
  })

  it('includes sizeBytes in result', async () => {
    const content = 'hello world'
    const f = join(root, 'hello.txt')
    writeFileSync(f, content)
    const result = await classifyFile(f, root)
    expect(result.sizeBytes).toBe(Buffer.byteLength(content))
  })

  it('allows classifying files outside workspace (deep-link surface)', async () => {
    const other = createTempDir()
    const f = join(other, 'outside.ts')
    writeFileSync(f, '')
    await expect(classifyFile(f, root)).resolves.toMatchObject({ contentClass: 'text' })
  })
})


describe('classifyFile — magic byte sniffing', () => {
  let root: string

  beforeEach(() => {
    root = createTempDir()
  })

  it('detects PDF by magic bytes even without .pdf extension', async () => {
    // %PDF- magic
    const f = join(root, 'nodoc')
    writeFileSync(f, Buffer.from([0x25, 0x50, 0x44, 0x46, 0x2d, 0x31, 0x2e, 0x34]))
    const result = await classifyFile(f, root)
    expect(result.contentClass).toBe('pdf')
  })

  it('detects PNG by magic bytes even without .png extension', async () => {
    const f = join(root, 'image_no_ext')
    const pngMagic = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])
    writeFileSync(f, pngMagic)
    const result = await classifyFile(f, root)
    expect(result.contentClass).toBe('image')
  })

  it('detects JPEG by magic bytes even without extension', async () => {
    const f = join(root, 'jpeg_no_ext')
    writeFileSync(f, Buffer.from([0xff, 0xd8, 0xff, 0xe1, 0x00, 0x18]))
    const result = await classifyFile(f, root)
    expect(result.contentClass).toBe('image')
  })

  it('detects GIF89a by magic bytes', async () => {
    const f = join(root, 'gif_no_ext')
    // GIF89a magic
    writeFileSync(f, Buffer.from([0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00]))
    const result = await classifyFile(f, root)
    expect(result.contentClass).toBe('image')
  })

  it('classifies printable-text-only content as text when extension is unknown', async () => {
    const f = join(root, 'plainfile')
    writeFileSync(f, 'just some plain text content here\n')
    const result = await classifyFile(f, root)
    expect(result.contentClass).toBe('text')
  })

  it('classifies binary content without known magic as unsupported', async () => {
    const f = join(root, 'binary_blob')
    // Write bytes that include non-printable, non-control characters
    const buf = Buffer.from([0x00, 0x01, 0x02, 0x03, 0x04, 0x80, 0x90, 0xff])
    writeFileSync(f, buf)
    const result = await classifyFile(f, root)
    expect(result.contentClass).toBe('unsupported')
  })
})


describe('readTextFile', () => {
  let root: string

  beforeEach(() => {
    root = createTempDir()
  })

  it('reads small files without truncation', async () => {
    const content = 'const x = 42\n'
    const f = join(root, 'a.ts')
    writeFileSync(f, content)

    const result = await readTextFile(f, root)
    expect(result.text).toBe(content)
    expect(result.truncated).toBe(false)
    expect(result.encoding).toBe('utf-8')
    expect(result.sizeBytes).toBe(Buffer.byteLength(content))
    expect(result.mtimeMs).toBeGreaterThan(0)
    expect(result.lineEnding).toBe('lf')
    expect(result.hasUtf8Bom).toBe(false)
  })

  it('truncates content when file exceeds limitBytes', async () => {
    const limitBytes = 10
    const content = '0123456789ABCDEF'
    const f = join(root, 'large.ts')
    writeFileSync(f, content)

    const result = await readTextFile(f, root, limitBytes)
    expect(result.truncated).toBe(true)
    expect(result.text.length).toBeLessThanOrEqual(limitBytes)
    expect(result.text).toBe(content.slice(0, limitBytes))
  })

  it('reads UTF-8 content correctly', async () => {
    const content = '// 你好世界\nexport const hello = "世界"\n'
    const f = join(root, 'chinese.ts')
    writeFileSync(f, content, 'utf-8')

    const result = await readTextFile(f, root)
    expect(result.text).toBe(content)
    expect(result.truncated).toBe(false)
  })

  it('allows reading text files outside workspace (deep-link surface)', async () => {
    const other = createTempDir()
    const f = join(other, 'secret.ts')
    writeFileSync(f, 'secret')
    await expect(readTextFile(f, root)).resolves.toMatchObject({ text: 'secret', truncated: false })
  })

  it('does NOT truncate when file size equals limitBytes exactly', async () => {
    const content = 'abcde'
    const f = join(root, 'exact.txt')
    writeFileSync(f, content, 'ascii')

    const result = await readTextFile(f, root, 5)
    expect(result.truncated).toBe(false)
    expect(result.text).toBe(content)
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
