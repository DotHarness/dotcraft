import { describe, expect, it } from 'vitest'
import { isBareFileLocationLinkTarget, normalizeBrowserUrl, resolveConversationLink } from '../../shared/viewer/linkResolver'

const workspacePath = 'C:/repo'
const resolve = (target: string, sourceContextDir?: string) =>
  resolveConversationLink({ target, workspacePath, sourceContextDir })

describe('resolveConversationLink', () => {
  it('resolves relative, context-relative, absolute and file URL targets with their hints', () => {
    expect(resolve('./src/App.tsx')).toEqual({ kind: 'file', absolutePath: 'C:/repo/src/App.tsx' })
    expect(resolve('./src/Foo.cs:42:7')).toEqual({
      kind: 'file',
      absolutePath: 'C:/repo/src/Foo.cs',
      hint: { line: 42, column: 7 }
    })
    expect(resolve('../shared/types.ts', 'C:/repo/src/renderer/components')).toEqual({
      kind: 'file',
      absolutePath: 'C:/repo/src/renderer/shared/types.ts'
    })
    expect(resolve('C:/logs/error.log:12:3')).toEqual({
      kind: 'file',
      absolutePath: 'C:/logs/error.log',
      hint: { line: 12, column: 3 }
    })
    expect(resolve('file:///C:/repo/docs/readme.md?mode=preview#title')).toEqual({
      kind: 'file',
      absolutePath: 'C:/repo/docs/readme.md',
      hint: { query: 'mode=preview', fragment: 'title' }
    })
    expect(resolve('/D:/notes/a.md')).toEqual({ kind: 'file', absolutePath: 'D:/notes/a.md' })
    expect(resolve('\\\\server\\share\\a.md')).toEqual({ kind: 'file', absolutePath: '//server/share/a.md' })
  })

  it('routes web links to the browser without decoding or reading line fragments, mailto externally, and rejects the rest', () => {
    expect(resolve('https://example.com/path?a=1#frag')).toEqual({ kind: 'browser', url: 'https://example.com/path?a=1#frag' })
    expect(resolve('https://example.com/a%20b')).toEqual({ kind: 'browser', url: 'https://example.com/a%20b' })
    const github = 'https://github.com/org/repo/blob/main/Demo.h#L12-L20'
    expect(resolve(github)).toEqual({ kind: 'browser', url: github })
    expect(resolve('mailto:test@example.com')).toEqual({ kind: 'external', url: 'mailto:test@example.com' })
    expect(resolve('   ')).toEqual({ kind: 'reject', reason: 'empty' })

    for (const target of ['javascript:alert(1)', 'javascript:123', 'https:123', 'data:12-20', 'vbscript:123', 'custom:123']) {
      expect(resolve(target), target).toEqual({ kind: 'reject', reason: 'unsupported-scheme' })
    }
  })

  it('decodes percent-encoded local paths while keeping encoded filename characters', () => {
    expect(resolve('E:/%E6%96%87%E6%A1%A3/a%20b.md')).toEqual({ kind: 'file', absolutePath: 'E:/文档/a b.md' })
    expect(resolve('docs/my%20notes.md')).toEqual({ kind: 'file', absolutePath: 'C:/repo/docs/my notes.md' })
    expect(resolve('D:%5Cref%5Cx.md:12')).toEqual({ kind: 'file', absolutePath: 'D:/ref/x.md', hint: { line: 12 } })

    const encoded: Array<[string, string]> = [
      ['docs/a%23b.md', 'C:/repo/docs/a#b.md'],
      ['docs/a%3Fb.md', 'C:/repo/docs/a?b.md'],
      ['D:/docs/a%23b.md', 'D:/docs/a#b.md'],
      ['/docs/a%3Fb.md', '/docs/a?b.md'],
      ['D:%5Cdocs%5Ca%23b.md', 'D:/docs/a#b.md'],
      ['%5C%5Cserver%5Cshare%5Ca%23b.md', '//server/share/a#b.md'],
      ['file:///C:/repo/docs/a%23b.md', 'C:/repo/docs/a#b.md'],
      ['file:///C:/repo/docs/a%3Fb.md', 'C:/repo/docs/a?b.md'],
      ['docs/a%3A12', 'C:/repo/docs/a:12'],
      ['docs/a%2523b.md', 'C:/repo/docs/a%23b.md']
    ]
    for (const [target, absolutePath] of encoded) {
      expect(resolve(target), target).toEqual({ kind: 'file', absolutePath })
    }

    for (const target of ['docs/a%23b.md', 'C:%5Crepo%5Cdocs%5Ca%23b.md', 'file:///C:/repo/docs/a%23b.md']) {
      expect(resolve(`${target}?mode=%3Fpreview#L12C3`), target).toEqual({
        kind: 'file',
        absolutePath: 'C:/repo/docs/a#b.md',
        hint: { query: 'mode=%3Fpreview', line: 12, column: 3 }
      })
    }
  })

  it('preserves navigation locations from suffixes and #L fragments', () => {
    const locations: Array<[string, string, Record<string, number>]> = [
      ['Test.cpp:123', 'Test.cpp', { line: 123 }],
      ['Demo.h:12-20', 'Demo.h', { line: 12, endLine: 20 }],
      ['Test.cpp:123:7', 'Test.cpp', { line: 123, column: 7 }],
      ['Demo.h#L12-L20', 'Demo.h', { line: 12, endLine: 20 }],
      ['Demo.h#L12C3-L20C7', 'Demo.h', { line: 12, column: 3, endLine: 20 }],
      ['Demo.h#L12-20', 'Demo.h', { line: 12, endLine: 20 }],
      ['src/my%20demo.cpp:12-20', 'src/my demo.cpp', { line: 12, endLine: 20 }],
      ['./a.md#L4-L9', 'a.md', { line: 4, endLine: 9 }]
    ]
    for (const [target, file, hint] of locations) {
      expect(resolve(target), target).toEqual({ kind: 'file', absolutePath: `${workspacePath}/${file}`, hint })
    }

    expect(resolve('D:/notes/a.md#L12C3')).toEqual({ kind: 'file', absolutePath: 'D:/notes/a.md', hint: { line: 12, column: 3 } })
    expect(resolve('Test.cpp:123?mode=source#details')).toEqual({
      kind: 'file', absolutePath: 'C:/repo/Test.cpp', hint: { line: 123, query: 'mode=source', fragment: 'details' }
    })
    expect(resolve('file:///C:/repo/a%23b.cpp:12-20')).toEqual({
      kind: 'file', absolutePath: 'C:/repo/a#b.cpp', hint: { line: 12, endLine: 20 }
    })
  })

  it('drops invalid numeric locations and keeps invalid line fragments as ordinary fragments', () => {
    for (const suffix of [':0', ':1:0', ':9007199254740992', ':1:9007199254740992', ':20-12', ':1-0', ':1-9007199254740992']) {
      expect(resolve(`./Demo.h${suffix}`), suffix).toEqual({ kind: 'file', absolutePath: 'C:/repo/Demo.h' })
    }
    for (const fragment of ['L0', 'L9007199254740992', 'L12C0', 'L20-L12', 'L12C3-L12C2', 'L12-L20C0']) {
      expect(resolve(`Demo.h#${fragment}`), fragment).toEqual({
        kind: 'file', absolutePath: 'C:/repo/Demo.h', hint: { fragment }
      })
    }
  })
})

describe('isBareFileLocationLinkTarget', () => {
  it('allows only a bare filename with a safe location', () => {
    for (const target of ['Test.cpp:123', 'Demo.h:12-20', 'File.tsx:50:3', 'my%20file.cpp:12', 'Test.cpp:123?mode=source#details']) {
      expect(isBareFileLocationLinkTarget(target), target).toBe(true)
    }
    for (const target of ['javascript:123', 'https:123', 'data:123', 'file:123', 'Test.cpp:0', 'Demo.h:20-12', 'Demo.h:9007199254740992', 'javascript%3Aalert.cpp:12', 'Test%00.cpp:12', 'src/Test.cpp:123', 'Test.cpp']) {
      expect(isBareFileLocationLinkTarget(target), target).toBe(false)
    }
  })
})

describe('normalizeBrowserUrl', () => {
  it('normalizes protocol and host case and strips the root slash and hash, keeping query and path', () => {
    expect(normalizeBrowserUrl('HTTPS://Example.COM/#fragment')).toBe('https://example.com')
    expect(normalizeBrowserUrl('https://Example.com/path/?utm=1#frag')).toBe('https://example.com/path/?utm=1')
  })
})
