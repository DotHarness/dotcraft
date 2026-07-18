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

  it('uses an unframed root and Desktop action geometry', () => {
    const html = buildInlineVisualizationDocument('<div class="viz-root"><button>Run</button></div>', 'dark', 'en', 'view-test')

    expect(html).toContain('.viz-root{min-width:0;margin:0;padding:0;border:0;border-radius:0;background:transparent}')
    expect(html).toContain('body>.card:only-child{padding:0;border:0;border-radius:0;background:transparent}')
    expect(html).toContain('.btn,button{display:inline-flex;min-height:32px')
    expect(html).toContain('.btn-primary{border-color:var(--foreground);color:var(--background);background:var(--foreground)')
    expect(html).not.toContain('body{padding:5px')
  })

  it('injects and updates whitelisted Desktop theme tokens', () => {
    const html = buildInlineVisualizationDocument('<div>ok</div>', 'dark', 'en', 'view-test', {
      background: 'rgb(1 2 3)',
      fontFamily: 'Test Sans'
    })

    expect(html).toContain('"background":"rgb(1 2 3)"')
    expect(html).toContain('"fontFamily":"Test Sans"')
    expect(html).toContain("applyTokens(message.params?.tokens)")
  })

  it('binds bridge messages to the generated view id', () => {
    const html = buildInlineVisualizationDocument('<div>ok</div>', 'dark', 'en', 'view-test')

    expect(html).toContain('const viewId = "view-test"')
    expect(html).toContain('message?.params?.viewId !== viewId')
  })
})
