import './setupPluginRuntime'
import type { DesktopPluginIconProps } from '@dotcraft/plugin'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { resolveDesktopPluginIcon } from '../components/desktopPlugins/DesktopPluginIcon'

describe('Desktop Plugin contribution icons', () => {
  it('renders a plugin-owned React icon component and forwards its props', () => {
    function PluginIcon(props: DesktopPluginIconProps): JSX.Element {
      const { size, ...svgProps } = props
      return <svg data-testid="plugin-icon" width={size} height={size} {...svgProps} />
    }

    const Icon = resolveDesktopPluginIcon(PluginIcon)
    expect(Icon).toBe(PluginIcon)

    render(<Icon size={16} strokeWidth={2} aria-hidden />)
    const rendered = screen.getByTestId('plugin-icon')
    expect(rendered).toHaveAttribute('width', '16')
    expect(rendered).toHaveAttribute('stroke-width', '2')
  })

  it('falls back to one Host glyph when a contribution supplies no icon', () => {
    const Fallback = resolveDesktopPluginIcon()
    expect(resolveDesktopPluginIcon(null)).toBe(Fallback)
    expect(resolveDesktopPluginIcon(undefined)).toBe(Fallback)

    const { container } = render(<Fallback size={14} aria-hidden />)
    const glyph = container.querySelector('svg')
    expect(glyph).toHaveAttribute('width', '14')
    expect(glyph).toHaveAttribute('aria-hidden', 'true')
  })
})
