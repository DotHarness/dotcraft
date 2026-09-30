import { describe, expect, it } from 'vitest'
import { normalizeBrowserUrl, resolveConversationLink } from '../../shared/viewer/linkResolver'

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
    })).toEqual({ kind: 'file', absolutePath: 'C:/repo/a.md', hint: { line: 4 } })
  })

  it('does not decode http URLs', () => {
    expect(resolveConversationLink({
      target: 'https://example.com/a%20b',
      workspacePath
    })).toEqual({ kind: 'browser', url: 'https://example.com/a%20b' })
  })
})

describe('normalizeBrowserUrl', () => {
  it('normalizes protocol and host case, strips root slash and hash', () => {
    expect(normalizeBrowserUrl('HTTPS://Example.COM/#fragment')).toBe('https://example.com')
  })

  it('keeps query and non-root path unchanged', () => {
    expect(normalizeBrowserUrl('https://Example.com/path/?utm=1#frag')).toBe('https://example.com/path/?utm=1')
  })
})
