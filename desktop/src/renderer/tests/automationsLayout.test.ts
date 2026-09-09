import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const styles = readFileSync(
  fileURLToPath(new URL('../components/automations/automations.css', import.meta.url)),
  'utf8'
)

describe('Automations layout', () => {
  it('uses the catalog content width only in browse mode', () => {
    expect(styles).toMatch(/\.dc-automations-list\s*{[^}]*width:\s*min\(760px, calc\(100% - 64px\)\)/s)
    expect(styles).toMatch(/\.dc-automations\[data-editing\] \.dc-automations-list\s*{[^}]*width:\s*auto/s)
  })

  it('keeps at least 16px side spacing on narrow canvases', () => {
    expect(styles).toMatch(/@media \(max-width: 820px\)[\s\S]*?\.dc-automations-list\s*{\s*width:\s*calc\(100% - 32px\)/)
  })

  it('matches the shared catalog hero typography without changing the detail title', () => {
    expect(styles).toMatch(/\.dc-automations-list > h1\s*{[^}]*font-size:\s*26px;[^}]*line-height:\s*1\.2;[^}]*font-weight:\s*700;[^}]*letter-spacing:\s*0;/s)
    expect(styles).toMatch(/\.dc-automations\[data-editing\] \.dc-automations-list > h1\s*{[^}]*font-size:\s*13px;/s)
  })
})
