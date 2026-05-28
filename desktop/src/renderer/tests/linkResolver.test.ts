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
})

describe('normalizeBrowserUrl', () => {
  it('normalizes protocol and host case, strips root slash and hash', () => {
    expect(normalizeBrowserUrl('HTTPS://Example.COM/#fragment')).toBe('https://example.com')
  })

  it('keeps query and non-root path unchanged', () => {
    expect(normalizeBrowserUrl('https://Example.com/path/?utm=1#frag')).toBe('https://example.com/path/?utm=1')
  })
})
