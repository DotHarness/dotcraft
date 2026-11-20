import { describe, expect, it } from 'vitest'
import { validateTheme } from '../../../shared/themes'
import { BUILTIN_THEMES } from './builtInThemes'
import { buildSceneDocument, svgDataUrl } from './sceneDocument'

describe('themes documents', () => {
  it('keeps example posters static and accepts every authorable example under the import contract', () => {
    for (const theme of BUILTIN_THEMES) {
      if (theme.backdrop?.kind === 'image') continue
      const { pair: _pair, ...input } = theme
      expect(() => validateTheme({ ...input, id: theme.id.replace('builtin-', '') })).not.toThrow()
      if (theme.backdrop?.kind === 'scene') expect(theme.backdrop.source).not.toBe(theme.backdrop.poster)
    }
  })

  it('pairs every example with a theme made for the other appearance', () => {
    for (const theme of BUILTIN_THEMES) {
      if (!theme.pair) continue
      const pair = BUILTIN_THEMES.find((candidate) => candidate.id === theme.pair)
      expect(pair?.pair).toBe(theme.id)
      expect(pair?.colorScheme).not.toBe(theme.colorScheme)
    }
  })

  it('encodes SVG data URLs without letting malformed Unicode crash the host', () => {
    expect(() => svgDataUrl('<svg><text>\uD800</text></svg>')).not.toThrow()
    expect(decodeURIComponent(svgDataUrl('<svg/>').split(',')[1])).toBe('<svg/>')
  })

  it('emits a network-denying policy before content and removes document navigation metadata', () => {
    const source = '<html><head><meta http-equiv="refresh" content="0;url=https://example.com"><base href="https://example.com"></head><body><canvas></canvas><script>window.draw = true</script></body></html>'
    const html = buildSceneDocument(source, 'test-nonce')
    expect(html.indexOf('Content-Security-Policy')).toBeLessThan(html.indexOf('<canvas>'))
    expect(html).toContain("connect-src 'none'")
    expect(html).toContain("frame-src 'none'")
    expect(html).toContain("worker-src 'none'")
    expect(html).not.toContain('http-equiv="refresh"')
    expect(html).not.toContain('<base')
    expect(html).toContain('<script>window.draw = true</script>')
  })
})
