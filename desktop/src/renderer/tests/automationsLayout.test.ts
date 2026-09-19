import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const styles = readFileSync(
  fileURLToPath(new URL('../components/automations/automations.css', import.meta.url)),
  'utf8'
)
const view = readFileSync(
  fileURLToPath(new URL('../components/automations/AutomationsView.tsx', import.meta.url)),
  'utf8'
)
const catalog = readFileSync(
  fileURLToPath(new URL('../components/catalog/CatalogSurface.tsx', import.meta.url)),
  'utf8'
)

describe('Automations layout', () => {
  it('carries page actions in the shared catalog band', () => {
    expect(view).toMatch(/<CatalogTopBar\s/)
    expect(styles).not.toMatch(/\.dc-automations-topbar/)
  })

  it('shares the catalog browse frame, and drops it while editing', () => {
    const header = catalog.match(/browseHeader: {[^}]*padding: '([^']+)'/s)?.[1]
    const main = catalog.match(/browseMain: {[^}]*padding: '([^']+)'/s)?.[1]
    expect([header, main]).toEqual(['28px 64px 16px', '28px 64px 48px'])
    expect(catalog).toMatch(/browseColumn = { maxWidth: '760px'/)

    expect(styles).toMatch(new RegExp(String.raw`\.dc-automations-header\s*{[^}]*flex: 0 0 auto;\s*padding: ${header};`, 's'))
    expect(styles).toMatch(new RegExp(String.raw`\.dc-automations-groups\s*{[^}]*padding: ${main};\s*overflow-y: auto;`, 's'))
    expect(styles).toMatch(/\.dc-automations-group\s*{[^}]*max-width: 760px/s)
    expect(styles).toMatch(/\.dc-automations\[data-editing\] \.dc-automations-group { max-width: none/)
  })

  it('reads group titles and rows from the catalog', () => {
    expect(catalog).toMatch(/sectionTitle: {[^}]*fontSize: '16px'[^}]*fontWeight: 700/s)
    expect(catalog).toMatch(/compactGrid: {[^}]*columnGap: '34px',\s*rowGap: '18px'/s)
    expect(catalog).toMatch(/compactItem: {[^}]*height: '58px'/s)

    expect(styles).toMatch(/\.dc-automations-group > h2\s*{[^}]*font-size: 16px;[^}]*font-weight: 700;/s)
    expect(styles).toMatch(/\.dc-automation-suggestions\s*{[^}]*column-gap: 34px;\s*row-gap: 18px;/s)
    expect(styles).toMatch(/\.dc-automation-suggestion\s*{[^}]*height: 58px;[^}]*padding: 0 8px;[^}]*border-radius: 8px;/s)
  })

  it('keeps a long task name inside the list instead of scrolling it sideways', () => {
    expect(styles).toMatch(/\.dc-automation-list-rows { display: grid; grid-template-columns: minmax\(0, 1fr\)/)
  })

  it('keeps at least 16px side spacing on narrow canvases', () => {
    expect(styles).toMatch(
      /@media \(max-width: 820px\)[\s\S]*?\.dc-automations-groups { padding-inline: 16px; }/
    )
  })

  it('matches the shared catalog hero typography without changing the detail title', () => {
    expect(styles).toMatch(/\.dc-automations-header > h1\s*{[^}]*font-size: 26px;[^}]*line-height: 1\.2;[^}]*font-weight: 700;[^}]*letter-spacing: 0;/s)
    expect(styles).toMatch(/\.dc-automations\[data-editing\] \.dc-automations-header > h1\s*{[^}]*font-size: 13px;/s)
  })
})
