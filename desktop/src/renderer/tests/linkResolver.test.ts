import { describe, expect, it } from 'vitest'
import { isBareFileLocationLinkTarget, normalizeBrowserUrl, resolveConversationLink } from '../../shared/viewer/linkResolver'

describe('resolveConversationLink', () => {
  const workspacePath = 'C:/repo'

  it('resolves relative paths against workspace by default', () => {
    expect(resolveConversationLink({
      target: './src/App.tsx',
      workspacePath
    })).toEqual({
      kind: 'file',
      absolutePath: 'C:/repo/src/App.tsx'
    })
  })

  it('resolves relative paths with line and column hints', () => {
    expect(resolveConversationLink({
      target: './src/Foo.cs:42:7',
      workspacePath
    })).toEqual({
      kind: 'file',
      absolutePath: 'C:/repo/src/Foo.cs',
      hint: { line: 42, column: 7 }
    })
  })

  it('resolves relative paths against source context directory when provided', () => {
    expect(resolveConversationLink({
      target: '../shared/types.ts',
      workspacePath,
      sourceContextDir: 'C:/repo/src/renderer/components'
    })).toEqual({
      kind: 'file',
      absolutePath: 'C:/repo/src/renderer/shared/types.ts'
    })
  })

  it('resolves absolute windows path with line and column hints', () => {
    expect(resolveConversationLink({
      target: 'C:/logs/error.log:12:3',
      workspacePath
    })).toEqual({
      kind: 'file',
      absolutePath: 'C:/logs/error.log',
      hint: { line: 12, column: 3 }
    })
  })

  it('resolves file URL and keeps query/fragment hints', () => {
    expect(resolveConversationLink({
      target: 'file:///C:/repo/docs/readme.md?mode=preview#title',
      workspacePath
    })).toEqual({
      kind: 'file',
      absolutePath: 'C:/repo/docs/readme.md',
      hint: { query: 'mode=preview', fragment: 'title' }
    })
  })

  it('routes http(s) URLs to browser', () => {
    expect(resolveConversationLink({
      target: 'https://example.com/path?a=1#frag',
      workspacePath
    })).toEqual({
      kind: 'browser',
      url: 'https://example.com/path?a=1#frag'
    })
  })

  it('routes mailto to external handoff', () => {
    expect(resolveConversationLink({
      target: 'mailto:test@example.com',
      workspacePath
    })).toEqual({
      kind: 'external',
      url: 'mailto:test@example.com'
    })
  })

  it('rejects empty target', () => {
    expect(resolveConversationLink({ target: '   ', workspacePath })).toEqual({
      kind: 'reject',
      reason: 'empty'
    })
  })

  it('rejects unsupported schemes', () => {
    expect(resolveConversationLink({
      target: 'javascript:alert(1)',
      workspacePath
    })).toEqual({
      kind: 'reject',
      reason: 'unsupported-scheme'
    })
  })

  it('decodes percent-encoded local paths produced by markdown', () => {
    expect(resolveConversationLink({
      target: 'E:/%E6%96%87%E6%A1%A3/a%20b.md',
      workspacePath
    })).toEqual({ kind: 'file', absolutePath: 'E:/文档/a b.md' })
    expect(resolveConversationLink({
      target: 'docs/my%20notes.md',
      workspacePath
    })).toEqual({ kind: 'file', absolutePath: 'C:/repo/docs/my notes.md' })
  })

  it('resolves encoded backslash paths instead of treating the drive as a scheme', () => {
    expect(resolveConversationLink({
      target: 'D:%5Cref%5Cx.md:12',
      workspacePath
    })).toEqual({ kind: 'file', absolutePath: 'D:/ref/x.md', hint: { line: 12 } })
  })

  it.each([
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
  ])('keeps encoded filename characters in %s', (target, absolutePath) => {
    expect(resolveConversationLink({ target, workspacePath })).toEqual({ kind: 'file', absolutePath })
  })

  it.each(['docs/a%23b.md', 'C:%5Crepo%5Cdocs%5Ca%23b.md', 'file:///C:/repo/docs/a%23b.md'])(
    'parses raw decorations before decoding %s', (target) => {
      expect(resolveConversationLink({ target: `${target}?mode=%3Fpreview#L12C3`, workspacePath })).toEqual({
        kind: 'file',
        absolutePath: 'C:/repo/docs/a#b.md',
        hint: { query: 'mode=%3Fpreview', line: 12, column: 3 }
      })
    }
  )

  it('strips the leading slash from drive paths', () => {
    expect(resolveConversationLink({
      target: '/D:/notes/a.md',
      workspacePath
    })).toEqual({ kind: 'file', absolutePath: 'D:/notes/a.md' })
  })

  it('keeps UNC paths absolute', () => {
    expect(resolveConversationLink({
      target: '\\\\server\\share\\a.md',
      workspacePath
    })).toEqual({ kind: 'file', absolutePath: '//server/share/a.md' })
  })

  it('reads #L line fragments as line and column hints', () => {
    expect(resolveConversationLink({
      target: 'D:/notes/a.md#L12C3',
      workspacePath
    })).toEqual({ kind: 'file', absolutePath: 'D:/notes/a.md', hint: { line: 12, column: 3 } })
    expect(resolveConversationLink({
      target: './a.md#L4-L9',
      workspacePath
    })).toEqual({ kind: 'file', absolutePath: 'C:/repo/a.md', hint: { line: 4, endLine: 9 } })
  })

  it.each([
    ['Test.cpp:123', 'Test.cpp', { line: 123 }],
    ['Demo.h:12-20', 'Demo.h', { line: 12, endLine: 20 }],
    ['Test.cpp:123:7', 'Test.cpp', { line: 123, column: 7 }],
    ['Demo.h#L12-L20', 'Demo.h', { line: 12, endLine: 20 }],
    ['Demo.h#L12C3-L20C7', 'Demo.h', { line: 12, column: 3, endLine: 20 }],
    ['Demo.h#L12-20', 'Demo.h', { line: 12, endLine: 20 }],
    ['src/my%20demo.cpp:12-20', 'src/my demo.cpp', { line: 12, endLine: 20 }]
  ])('preserves navigation locations from %s', (target, file, hint) => {
    expect(resolveConversationLink({ target, workspacePath })).toEqual({
      kind: 'file', absolutePath: `${workspacePath}/${file}`, hint
    })
  })

  it.each([':0', ':1:0', ':9007199254740992', ':1:9007199254740992', ':20-12', ':1-0', ':1-9007199254740992'])(
    'ignores invalid numeric locations %s while retaining the file path', (suffix) => {
      expect(resolveConversationLink({ target: `./Demo.h${suffix}`, workspacePath })).toEqual({
        kind: 'file', absolutePath: 'C:/repo/Demo.h'
      })
    }
  )

  it.each(['L0', 'L9007199254740992', 'L12C0', 'L20-L12', 'L12C3-L12C2', 'L12-L20C0'])(
    'retains invalid line fragments %s as ordinary fragments', (fragment) => {
      expect(resolveConversationLink({ target: `Demo.h#${fragment}`, workspacePath })).toEqual({
        kind: 'file', absolutePath: 'C:/repo/Demo.h', hint: { fragment }
      })
    }
  )

  it('keeps query and ordinary fragment decorations on bare filename locations', () => {
    expect(resolveConversationLink({ target: 'Test.cpp:123?mode=source#details', workspacePath })).toEqual({
      kind: 'file', absolutePath: 'C:/repo/Test.cpp', hint: { line: 123, query: 'mode=source', fragment: 'details' }
    })
  })

  it('supports locations on file URLs without decoding filename decorations', () => {
    expect(resolveConversationLink({ target: 'file:///C:/repo/a%23b.cpp:12-20', workspacePath })).toEqual({
      kind: 'file', absolutePath: 'C:/repo/a#b.cpp', hint: { line: 12, endLine: 20 }
    })
  })

  it.each(['javascript:123', 'https:123', 'data:12-20', 'vbscript:123', 'custom:123'])(
    'does not reinterpret numeric schemes as files: %s', (target) => {
      expect(resolveConversationLink({ target, workspacePath })).toEqual({ kind: 'reject', reason: 'unsupported-scheme' })
    }
  )

  it('does not extract file navigation hints from browser URL fragments', () => {
    const target = 'https://github.com/org/repo/blob/main/Demo.h#L12-L20'
    expect(resolveConversationLink({ target, workspacePath })).toEqual({ kind: 'browser', url: target })
  })

  it('does not decode http URLs', () => {
    expect(resolveConversationLink({
      target: 'https://example.com/a%20b',
      workspacePath
    })).toEqual({ kind: 'browser', url: 'https://example.com/a%20b' })
  })
})

describe('isBareFileLocationLinkTarget', () => {
  it.each(['Test.cpp:123', 'Demo.h:12-20', 'File.tsx:50:3', 'my%20file.cpp:12', 'Test.cpp:123?mode=source#details'])(
    'allows a bare filename with a safe location: %s', (target) => {
      expect(isBareFileLocationLinkTarget(target)).toBe(true)
    }
  )

  it.each(['javascript:123', 'https:123', 'data:123', 'file:123', 'Test.cpp:0', 'Demo.h:20-12', 'Demo.h:9007199254740992', 'javascript%3Aalert.cpp:12', 'Test%00.cpp:12', 'src/Test.cpp:123', 'Test.cpp'])(
    'does not relax the URL sanitizer for %s', (target) => {
      expect(isBareFileLocationLinkTarget(target)).toBe(false)
    }
  )
})

describe('normalizeBrowserUrl', () => {
  it('normalizes protocol and host case, strips root slash and hash', () => {
    expect(normalizeBrowserUrl('HTTPS://Example.COM/#fragment')).toBe('https://example.com')
  })

  it('keeps query and non-root path unchanged', () => {
    expect(normalizeBrowserUrl('https://Example.com/path/?utm=1#frag')).toBe('https://example.com/path/?utm=1')
  })
})
