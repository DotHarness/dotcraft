/**
 * Tests for viewerIpc: classify (extension + magic byte sniffing), read-text
 * (content + truncation), and list-surface workspace boundary helper behavior.
 */
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { mkdtempSync, rmSync, writeFileSync, mkdirSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import { classifyFile, readTextFile, isPathInsideWorkspace } from '../viewerIpc'

// ─── Temp directory helpers ────────────────────────────────────────────────────

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

// ─── isPathInsideWorkspace ──────────────────────────────────────────────────

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

// ─── classifyFile — extension-based ───────────────────────────────────────────

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

// ─── classifyFile — magic-byte sniffing ────────────────────────────────────────

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

// ─── readTextFile ────────────────────────────────────────────────────────────

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
})
