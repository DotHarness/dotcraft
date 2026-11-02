import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ThreadUsagePopover } from './ThreadUsagePopover'
import { stringsFor } from './i18n'

describe('thread usage popover', () => {
  it('sums turns of the same model into one row regardless of effort or speed', () => {
    const { container } = render(
      <ThreadUsagePopover
        usage={{
          threadId: 'thread-1',
          turns: 3,
          totalTokens: 300,
          costUsd: 0.3,
          groups: [
            { model: 'atlas-4', turns: 1, totalTokens: 100, costUsd: 0.1 },
            { model: 'boreal-mini', turns: 1, totalTokens: 60, costUsd: null },
            { model: 'atlas-4', turns: 1, totalTokens: 140, costUsd: 0.2 }
          ]
        }}
        strings={stringsFor('en')}
        formatTokens={(value) => String(value)}
        formatCost={(value) => `$${value.toFixed(2)}`}
        style={{}}
      />
    )

    const rows = Array.from(container.querySelectorAll('.token-hud-popover-rows li'))
    expect(rows.map((row) => row.querySelector('.token-hud-popover-label')?.textContent)).toEqual([
      'atlas-4',
      'boreal-mini'
    ])
    expect(rows[0].querySelector('.token-hud-popover-value')?.textContent).toBe('80% · 240 · $0.30')
    expect(rows[1].querySelector('.token-hud-popover-value')?.textContent).toBe('20% · 60')
  })
})
