import { describe, expect, it } from 'vitest'
import { resolveDetailPanelWidth } from '../components/layout/ThreePanel'
import { DETAIL_DEFAULT_WIDTH_RATIO, DETAIL_MIN_WIDTH } from '../stores/uiStore'

describe('resolveDetailPanelWidth', () => {
  it.each([
    ['keeps the preferred width at a common full-screen width', 600, DETAIL_DEFAULT_WIDTH_RATIO, 1676, true, 600],
    ['scales the default width when the layout is windowed', 600, DETAIL_DEFAULT_WIDTH_RATIO, 1156, true, 414],
    ['caps the width to preserve the conversation minimum width', 900, 900 / 1156, 1156, true, 756],
    ['preserves a user preference below the cap', 360, 360 / 1156, 1156, true, 360],
    ['never shrinks below the detail panel minimum width', 600, DETAIL_DEFAULT_WIDTH_RATIO, 500, true, DETAIL_MIN_WIDTH],
    ['uses the fallback width before the main surface is measured', 600, DETAIL_DEFAULT_WIDTH_RATIO, null, true, 600],
    ['returns zero while the panel is hidden', 600, DETAIL_DEFAULT_WIDTH_RATIO, 1156, false, 0]
  ] as const)('%s', (_name, preferred, ratio, available, visible, expected) => {
    expect(resolveDetailPanelWidth(preferred, ratio, available, visible)).toBe(expected)
  })
})
