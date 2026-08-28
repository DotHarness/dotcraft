import { describe, it, expect } from 'vitest'
import { classifyWidth } from '../hooks/useResponsiveLayout'
import { resolveResponsivePanels } from '../stores/uiStore'

describe('classifyWidth (responsive breakpoint logic)', () => {
  it('classifies 1400px as "full" (all panels visible)', () => {
    expect(classifyWidth(1400)).toBe('full')
  })

  it('classifies 1250px (boundary) as "full"', () => {
    expect(classifyWidth(1250)).toBe('full')
  })

  it('classifies 1000px as "no-detail" (detail panel auto-collapses)', () => {
    expect(classifyWidth(1000)).toBe('no-detail')
  })

  it('classifies 1249px as "no-detail" (just below full threshold)', () => {
    expect(classifyWidth(1249)).toBe('no-detail')
  })

  it('classifies 1200px as "no-detail" with the wider default panel', () => {
    expect(classifyWidth(1200)).toBe('no-detail')
  })

  it('classifies 900px (boundary) as "no-detail"', () => {
    expect(classifyWidth(900)).toBe('no-detail')
  })

  it('classifies 800px as "collapsed" (sidebar icon-only, detail hidden)', () => {
    expect(classifyWidth(800)).toBe('collapsed')
  })

  it('classifies 899px as "collapsed" (just below no-detail threshold)', () => {
    expect(classifyWidth(899)).toBe('collapsed')
  })

  it('classifies very small widths as "collapsed"', () => {
    expect(classifyWidth(400)).toBe('collapsed')
    expect(classifyWidth(0)).toBe('collapsed')
  })
})

describe('resolveResponsivePanels', () => {
  it('keeps user-hidden detail panel hidden in full layout', () => {
    expect(resolveResponsivePanels('full', false, false)).toEqual({
      sidebarCollapsed: false,
      detailPanelVisible: false
    })
  })

  it('temporarily hides detail in no-detail layout without changing sidebar preference', () => {
    expect(resolveResponsivePanels('no-detail', true, true)).toEqual({
      sidebarCollapsed: true,
      detailPanelVisible: false
    })
  })

  it('temporarily collapses both panels in collapsed layout', () => {
    expect(resolveResponsivePanels('collapsed', false, true)).toEqual({
      sidebarCollapsed: true,
      detailPanelVisible: false
    })
  })

  it('restores the original preferences when returning to full layout', () => {
    const hiddenByBreakpoint = resolveResponsivePanels('collapsed', false, false)
    expect(hiddenByBreakpoint).toEqual({
      sidebarCollapsed: true,
      detailPanelVisible: false
    })

    expect(resolveResponsivePanels('full', false, false)).toEqual({
      sidebarCollapsed: false,
      detailPanelVisible: false
    })
  })
})
