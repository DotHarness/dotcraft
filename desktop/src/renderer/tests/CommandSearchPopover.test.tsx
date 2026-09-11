import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { CommandSearchPopover } from '../components/conversation/CommandSearchPopover'
import { LocaleProvider } from '../contexts/LocaleContext'
import type { CustomCommandInfo } from '../hooks/useCustomCommandCatalog'

function command(name: string): CustomCommandInfo {
  return {
    name: `/${name}`,
    aliases: [],
    description: `${name} description`,
    category: 'test',
    requiresAdmin: false
  }
}

function popover(
  commands: CustomCommandInfo[],
  options: {
    query?: string
    visible?: boolean
    onSelectCommand?: (name: string) => void
  } = {}
): JSX.Element {
  return (
    <LocaleProvider loadSettings={false}>
      <CommandSearchPopover
        query={options.query ?? ''}
        visible={options.visible ?? true}
        loading={false}
        commands={commands}
        onSelectCommand={options.onSelectCommand ?? (() => {})}
        onDismiss={() => {}}
      />
    </LocaleProvider>
  )
}

describe('CommandSearchPopover keyboard navigation', () => {
  it('keeps the next option selected when optional catalogs are omitted', async () => {
    const onSelectCommand = vi.fn()
    render(popover([command('first'), command('second')], { onSelectCommand }))

    fireEvent.keyDown(window, { key: 'ArrowDown' })

    await waitFor(() => {
      expect(screen.getByRole('option', { name: /second/i })).toHaveAttribute('aria-selected', 'true')
    })
    fireEvent.keyDown(window, { key: 'Enter' })
    expect(onSelectCommand).toHaveBeenCalledWith('/second')
  })

  it('preserves selection across an equivalent parent rerender', async () => {
    const { rerender } = render(popover([command('first'), command('second')]))
    fireEvent.keyDown(window, { key: 'ArrowDown' })

    rerender(popover([command('first'), command('second')]))

    await waitFor(() => {
      expect(screen.getByRole('option', { name: /second/i })).toHaveAttribute('aria-selected', 'true')
    })
  })

  it('resets selection when the query changes or the popover reopens', async () => {
    const commands = [command('alpha'), command('alpine')]
    const { rerender } = render(popover(commands))
    fireEvent.keyDown(window, { key: 'ArrowDown' })

    rerender(popover(commands, { query: 'a' }))
    await waitFor(() => {
      expect(screen.getByRole('option', { name: /alpha/i })).toHaveAttribute('aria-selected', 'true')
    })

    fireEvent.keyDown(window, { key: 'ArrowDown' })
    rerender(popover(commands, { query: 'a', visible: false }))
    rerender(popover(commands, { query: 'a', visible: true }))
    await waitFor(() => {
      expect(screen.getByRole('option', { name: /alpha/i })).toHaveAttribute('aria-selected', 'true')
    })
  })

  it('clamps selection when the result list shrinks', async () => {
    const { rerender } = render(popover([command('first'), command('second'), command('third')]))
    fireEvent.keyDown(window, { key: 'ArrowDown' })
    fireEvent.keyDown(window, { key: 'ArrowDown' })

    rerender(popover([command('first')]))

    await waitFor(() => {
      expect(screen.getByRole('option', { name: /first/i })).toHaveAttribute('aria-selected', 'true')
    })
  })
})
