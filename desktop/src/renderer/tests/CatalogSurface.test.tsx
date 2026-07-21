import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { CatalogBreadcrumb, CatalogTopBar } from '../components/catalog/CatalogSurface'

describe('CatalogTopBar', () => {
  it('uses the shared 48px control band', () => {
    const { container } = render(<CatalogTopBar navigation={<span>Plugins</span>} />)

    expect(container.firstElementChild).toHaveStyle({
      height: '48px',
      padding: '8px 12px'
    })
  })

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
