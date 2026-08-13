import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { CatalogBreadcrumb, CatalogTopBar } from '../components/catalog/CatalogSurface'

describe('CatalogTopBar', () => {
  it('renders a shared breadcrumb and returns through its parent action', () => {
    const onBack = vi.fn()
    render(
      <CatalogBreadcrumb
        parentLabel="Channels"
        currentLabel="Feishu"
        onBack={onBack}
      />
    )

    expect(screen.getByText('Feishu')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Channels' }))
    expect(onBack).toHaveBeenCalledOnce()
  })

  it('keeps a label in the same box when the trail gains depth around it', () => {
    const onBack = vi.fn()
    const onMiddle = vi.fn()

    const { rerender } = render(
      <CatalogBreadcrumb parentLabel="Channels" currentLabel="Feishu" onBack={onBack} />
    )
    const leadingPadding = screen.getByRole('button', { name: 'Channels' }).style.padding
    const asCurrent = screen.getByText('Feishu').style

    // Emphasis is colour, never weight: bold would re-measure the word.
    expect(asCurrent.fontWeight).toBe('')
    expect(asCurrent.color).toBe('var(--text-primary)')

    rerender(
      <CatalogBreadcrumb
        parentLabel="Channels"
        currentLabel="Manage"
        onBack={onBack}
        trail={[{ label: 'Feishu', onClick: onMiddle }]}
      />
    )

    // A hand-rolled deep trail loses these and shifts the row as you go deeper.
    expect(screen.getByRole('button', { name: 'Channels' }).style.padding).toBe(leadingPadding)
    expect(screen.getByRole('button', { name: 'Feishu' }).style.padding).toBe(asCurrent.padding)

    fireEvent.click(screen.getByRole('button', { name: 'Feishu' }))
    expect(onMiddle).toHaveBeenCalledOnce()
    expect(onBack).not.toHaveBeenCalled()
  })
})
