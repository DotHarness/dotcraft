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
})
