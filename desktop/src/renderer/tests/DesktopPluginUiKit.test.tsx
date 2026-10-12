import './setupPluginRuntime'
import { CatalogPage, IconButton, Menu, SegmentedControl, SplitButton } from '@dotcraft/plugin'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

describe('Desktop Plugin UI kit', () => {
  it('renders the Core segmented control and reports through the kit onValueChange name', () => {
    const onValueChange = vi.fn()
    render(
      <SegmentedControl<'system' | 'dark'>
        value="system"
        options={[
          { value: 'system', label: 'System' },
          { value: 'dark', label: 'Dark' }
        ]}
        onValueChange={onValueChange}
        ariaLabel="Theme"
      />
    )

    expect(screen.getByRole('group', { name: 'Theme' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'System' })).toHaveAttribute('aria-pressed', 'true')

    fireEvent.click(screen.getByRole('button', { name: 'Dark' }))
    expect(onValueChange).toHaveBeenCalledWith('dark')

    fireEvent.click(screen.getByRole('button', { name: 'System' }))
    expect(onValueChange).toHaveBeenCalledOnce()
  })

  it('opens a Host menu from an IconButton with keyboard navigation and dismissal', () => {
    const onOpen = vi.fn()
    const onRemove = vi.fn()
    render(
      <Menu
        items={[
          { label: 'Open', onSelect: onOpen },
          { label: 'Archive', onSelect: vi.fn(), disabled: true },
          { type: 'separator' },
          { type: 'label', label: 'Danger zone' },
          { label: 'Remove', onSelect: onRemove, danger: true }
        ]}
      >
        <IconButton icon={<span />} label="More actions" />
      </Menu>
    )
    const trigger = screen.getByRole('button', { name: 'More actions' })
    expect(trigger).toHaveAttribute('aria-expanded', 'false')

    fireEvent.click(trigger, { detail: 0 })
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('menuitem', { name: 'Open' })).toHaveFocus()

    fireEvent.keyDown(document, { key: 'ArrowDown' })
    expect(screen.getByRole('menuitem', { name: 'Remove' })).toHaveFocus()
    fireEvent.keyDown(document, { key: 'ArrowDown' })
    expect(screen.getByRole('menuitem', { name: 'Open' })).toHaveFocus()

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(trigger).toHaveFocus()

    fireEvent.click(trigger)
    fireEvent.click(screen.getByRole('menuitem', { name: 'Remove' }))
    expect(onRemove).toHaveBeenCalledOnce()
    expect(onOpen).not.toHaveBeenCalled()
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('runs split button menu items through the kit onSelect name', () => {
    const onClick = vi.fn()
    const onClone = vi.fn()
    render(
      <SplitButton
        label="Add"
        menuLabel="More ways to add"
        onClick={onClick}
        items={[{ label: 'Clone', onSelect: onClone }]}
      />
    )

    fireEvent.click(screen.getByRole('button', { name: 'Add' }))
    expect(onClick).toHaveBeenCalledOnce()

    fireEvent.click(screen.getByRole('button', { name: 'More ways to add' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Clone' }))
    expect(onClone).toHaveBeenCalledOnce()
    expect(onClick).toHaveBeenCalledOnce()
  })

  it('reports catalog search and filter choices through the kit onValueChange name', () => {
    const onSearch = vi.fn()
    const onStatus = vi.fn()
    render(
      <CatalogPage
        title="Projects"
        search={{ value: '', placeholder: 'Search projects', onValueChange: onSearch }}
        filters={{
          ariaLabel: 'Filter',
          groups: [{
            label: 'Status',
            value: 'all',
            options: [{ value: 'all', label: 'All' }, { value: 'open', label: 'Open' }],
            onValueChange: onStatus
          }]
        }}
      >
        {null}
      </CatalogPage>
    )

    fireEvent.change(screen.getByPlaceholderText('Search projects'), { target: { value: 'demo' } })
    expect(onSearch).toHaveBeenCalledWith('demo')

    fireEvent.click(screen.getByRole('button', { name: 'Filter' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Status' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Open' }))
    expect(onStatus).toHaveBeenCalledWith('open')
  })
})
