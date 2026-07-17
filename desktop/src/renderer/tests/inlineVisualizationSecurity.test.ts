import { describe, expect, it } from 'vitest'
import { buildInlineVisualizationDocument } from '../components/conversation/inlineVisualizationSecurity'

describe('inline visualization host document', () => {
  it('uses the fixed CSP and denies network connections', () => {
    const html = buildInlineVisualizationDocument('<div>ok</div>', 'dark', 'en', 'view-test')

    expect(html).toContain("default-src 'none'")
    expect(html).toContain("connect-src 'none'")
    expect(html).toContain("frame-src 'none'")
    expect(html).toContain("form-action 'none'")
    expect(html).toContain("navigate-to 'none'")
    expect(html).toContain('https://cdnjs.cloudflare.com')
    expect(html).not.toContain('https://example.com')
  })

  it('packages 16px Lucide icons and initializes static placeholders', () => {
    const html = buildInlineVisualizationDocument('<i data-lucide="circle"></i>', 'light', 'en', 'view-test')

    expect(html).toContain("width:'16'")
    expect(html).toContain('createIcons({ attrs: { width: 16, height: 16 } })')
    expect(html).toContain("addEventListener('DOMContentLoaded'")
  })

  it('provides responsive utilities and tooltip support', () => {
    const html = buildInlineVisualizationDocument('<div class="viz-grid"></div>', 'light', 'en', 'view-test')

    expect(html).toContain('@media(max-width:736px)')
    expect(html).toContain('@media(max-width:320px)')
    expect(html).toContain('[data-tooltip]')
    expect(html).toContain('.form-switch')
  })

  it('binds bridge messages to the generated view id', () => {
    const html = buildInlineVisualizationDocument('<div>ok</div>', 'dark', 'en', 'view-test')

    expect(html).toContain('const viewId = "view-test"')
    expect(html).toContain('message?.params?.viewId !== viewId')
  })
})
