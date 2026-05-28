import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { SparkIcon } from '../components/ui/AppIcons'

describe('AppIcons', () => {
  it('forwards style and strokeWidth through SparkIcon', () => {
    const { container } = render(
      <SparkIcon size={18} strokeWidth={2.4} style={{ flexShrink: 0 }} />
    )

    const svg = container.querySelector('svg')
    expect(svg).not.toBeNull()
    expect(svg).toHaveAttribute('stroke-width', '2.4')
    expect(svg).toHaveStyle({ flexShrink: '0' })
  })
})
