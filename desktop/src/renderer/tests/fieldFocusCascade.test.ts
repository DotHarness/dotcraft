import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * The field focus border is a cascade property, not a component property, so it
 * cannot be asserted by rendering: jsdom applies no stylesheet. These checks read
 * the rule order in the source instead.
 *
 * Both invariants come from a real regression — a `:hover` rule outranked
 * `:focus`, so a field the user had just clicked showed the neutral hover border
 * instead of the accent one, because the pointer was still resting on it.
 */
const fieldStyles = readFileSync(
  fileURLToPath(new URL('../styles/primitives/field.css', import.meta.url)),
  'utf8'
)

function ruleIndex(selector: string): number {
  const at = fieldStyles.indexOf(selector)
  expect(at, `${selector} is missing from field.css`).toBeGreaterThan(-1)
  return at
}

describe('shared field focus cascade', () => {
  it('gives fields no hover state to compete with the focus border', () => {
    const hoverRules = fieldStyles.match(/^\.dc-field[^,{\n]*:hover[^{\n]*/gm) ?? []
    expect(hoverRules).toEqual([])
  })

  it('declares the focus border after every resting shape', () => {
    const focus = ruleIndex('.dc-field:focus,')
    expect(focus).toBeGreaterThan(ruleIndex('.dc-field[data-frameless] {'))
    expect(focus).toBeGreaterThan(ruleIndex('.dc-field[data-invalid] {'))
    expect(focus).toBeGreaterThan(ruleIndex('.dc-field[data-multiline] {'))
  })

  // A shape selector carries one more attribute than `.dc-field:focus`, so the
  // focus rule has to name it explicitly to outrank it.
  it('names the frameless shape in the focus rule', () => {
    expect(fieldStyles).toContain('.dc-field[data-frameless]:focus,')
    expect(fieldStyles).toContain('.dc-field[data-frameless]:focus-visible {')
  })

  // Bare fields are the one shape that stays frameless while focused, because the
  // composed shell around them paints the frame instead.
  it('keeps the bare shape after the focus rule so the shell still wins', () => {
    expect(ruleIndex('.dc-field[data-bare],')).toBeGreaterThan(ruleIndex('.dc-field:focus,'))
    expect(fieldStyles).toContain('.dc-field[data-bare]:focus,')
  })
})
