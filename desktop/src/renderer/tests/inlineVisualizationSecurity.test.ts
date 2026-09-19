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
