import { describe, expect, it } from 'vitest'
import { resolveMarkdownImageSource } from './markdownImageSource'

const doc = 'D:\\docs\\notes\\issues.md'

describe('resolveMarkdownImageSource', () => {
  it('resolves local references against the document folder', () => {
    expect(resolveMarkdownImageSource('images/shot%201.png', doc)).toBe('dotcraft-viewer://workspace/D%3A/docs/notes/images/shot%201.png')
    expect(resolveMarkdownImageSource('<../assets/a.png>', doc)).toBe('dotcraft-viewer://workspace/D%3A/docs/assets/a.png')
    expect(resolveMarkdownImageSource('C:\\pics\\a.png', doc)).toBe('dotcraft-viewer://workspace/C%3A/pics/a.png')
    expect(resolveMarkdownImageSource('file:///C:/pics/a.png', doc)).toBe('dotcraft-viewer://workspace/C%3A/pics/a.png')
    expect(resolveMarkdownImageSource('/srv/a.png', '/home/u/readme.md')).toBe('dotcraft-viewer://workspace/srv/a.png')
  })

  it('keeps data images and refuses remote or script references', () => {
    expect(resolveMarkdownImageSource('data:image/png;base64,AA==', doc)).toBe('data:image/png;base64,AA==')
    expect(resolveMarkdownImageSource('https://example.com/a.png', doc)).toBeNull()
    expect(resolveMarkdownImageSource('javascript:alert(1)', doc)).toBeNull()
    expect(resolveMarkdownImageSource('  ', doc)).toBeNull()
  })
})
