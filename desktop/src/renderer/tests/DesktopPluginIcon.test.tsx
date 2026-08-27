import type { DesktopPluginIconProps } from '@dotcraft/plugin'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { resolveDesktopPluginIcon } from '../components/desktopPlugins/DesktopPluginIcon'

describe('Desktop Plugin contribution icons', () => {
  it('renders a plugin-owned React icon component without a Core token', () => {
    function PluginIcon(props: DesktopPluginIconProps): JSX.Element {
      const { size, ...svgProps } = props
      return <svg data-testid="plugin-icon" width={size} height={size} {...svgProps} />
    }

    const Icon = resolveDesktopPluginIcon(PluginIcon)
    render(<Icon size={16} strokeWidth={2} aria-hidden />)

    expect(screen.getByTestId('plugin-icon')).toHaveAttribute('width', '16')
  })
})
