import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const rendererRoot = resolve(__dirname, '..')

function readRendererFile(path: string): string {
  return readFileSync(resolve(rendererRoot, path), 'utf8')
}

describe('sidebar radius styling', () => {
  it('uses control radius tokens and superellipse support for sidebar controls', () => {
    const tokensCss = readRendererFile('styles/tokens.css')

    expect(tokensCss).toContain('--control-radius-md-base: 0.5rem')
    expect(tokensCss).toContain('--control-radius-lg-base: 0.625rem')
    expect(tokensCss).toContain('--sidebar-control-radius: var(--control-radius-md)')
    expect(tokensCss).toContain('@supports (corner-shape: superellipse(1.5))')
    expect(tokensCss).toContain('corner-shape: superellipse(1.5)')
  })

  it('applies the shared sidebar radius to primary and thread controls', () => {
    const newThreadSource = readRendererFile('components/sidebar/NewThreadButton.tsx')
    const threadEntrySource = readRendererFile('components/sidebar/ThreadEntry.tsx')
    const navRowSource = readRendererFile('components/sidebar/sidebarNavRowStyles.ts')
    const sidebarSource = readRendererFile('components/layout/Sidebar.tsx')
    const sidebarFooterSource = readRendererFile('components/sidebar/SidebarFooter.tsx')

    expect(newThreadSource).toContain('dotcraft-sidebar-control-radius')
    expect(newThreadSource).toContain("borderRadius: 'var(--sidebar-control-radius)'")
    expect(newThreadSource).toContain("import { SquarePen } from 'lucide-react'")
    expect(newThreadSource).toContain('SIDEBAR_NAV_ROW_OUTER')
    expect(newThreadSource).toContain('SIDEBAR_NAV_ICON_SLOT')
    expect(newThreadSource).toContain('<SquarePen size={16} strokeWidth={1.8} aria-hidden="true" style={{ display: \'block\' }} />')
    expect(newThreadSource).not.toContain("fontWeight: 'var(--type-ui-emphasis-weight)'")
    expect(newThreadSource).not.toContain('<span aria-hidden="true">+</span>')
    expect(threadEntrySource).toContain('dotcraft-sidebar-control-radius')
    expect(threadEntrySource).toContain("borderRadius: 'var(--sidebar-control-radius)'")
    expect(threadEntrySource).not.toContain('borderLeft')
    expect(navRowSource).toContain("borderRadius: 'var(--sidebar-control-radius)'")
    expect(sidebarSource).toContain('SquarePen')
    expect(sidebarSource).not.toContain('>\n          +\n        </button>')
    expect(sidebarSource).not.toContain("borderLeft: '3px solid var(--accent)'")
    expect(sidebarFooterSource).not.toContain("borderLeft: '3px solid var(--accent)'")
  })
})
