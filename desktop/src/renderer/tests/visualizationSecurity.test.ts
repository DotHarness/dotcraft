import { describe, expect, it } from 'vitest'
import { buildVisualizationDocument } from '../components/conversation/visualizationSecurity'
import { VISUALIZATION_MAX_DOCUMENT_BYTES, MCP_APP_MAX_RESOURCE_BYTES } from '../../shared/mcpAppSandbox'

const INLINE = { widgetState: null, displayMode: 'inline', maxHeight: 10_000 } as const
const LABELS = { design: 'Design', designs: 'Designs', previousDesign: 'Previous design', nextDesign: 'Next design' }

describe('visualization host document', () => {
  it('uses the fixed CSP and denies network connections', () => {
    const html = buildVisualizationDocument('<div>ok</div>', 'dark', 'en', 'view-test', INLINE, LABELS)

    expect(html).toContain("default-src 'none'")
    expect(html).toContain("connect-src 'none'")
    expect(html).toContain("frame-src 'none'")
    expect(html).toContain("form-action 'none'")
    expect(html).toContain('https://cdnjs.cloudflare.com')
    expect(html).not.toContain('https://example.com')
  })

  it('embeds saved widget state and theme tokens without closing the bridge script', () => {
    const html = buildVisualizationDocument('<div>ok</div>', 'dark', 'en', 'view-test', {
      ...INLINE,
      widgetState: { modelContent: { note: '</script><script>alert(1)</script>' }, privateContent: null }
    }, LABELS, { fontFamily: '</script><script>alert(2)</script>' })

    expect(html.match(/<\/script>/g)).toHaveLength(1)
  })

  it('installs the runtime before the fragment and leaves room for the largest fragment', () => {
    const html = buildVisualizationDocument('<viz-calendar date="2026-07-14"></viz-calendar>', 'dark', 'en', 'view-test', INLINE, LABELS)

    expect(html.indexOf("customElements.define('viz-calendar'")).toBeGreaterThan(-1)
    expect(html.indexOf("customElements.define('viz-calendar'")).toBeLessThan(html.indexOf('<viz-calendar date='))
    expect(new TextEncoder().encode(html).byteLength + MCP_APP_MAX_RESOURCE_BYTES).toBeLessThanOrEqual(VISUALIZATION_MAX_DOCUMENT_BYTES)
  })
})
