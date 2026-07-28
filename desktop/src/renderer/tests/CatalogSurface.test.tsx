import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { CatalogBreadcrumb, CatalogSection, CatalogTopBar, styles } from '../components/catalog/CatalogSurface'

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

// Space and heading weight separate catalog controls and groups. Rules here read
// as a frame around the header rather than as a boundary anything needs.
describe('catalog surface rules', () => {
  it('caps the hero and manage headers without a rule', () => {
    expect(styles.browseHeader.borderBottom).toBeUndefined()
    expect(styles.manageHeader.borderBottom).toBeUndefined()
  })

  it('heads a group without a rule above it', () => {
    render(<CatalogSection title="Installed locally"><div /></CatalogSection>)

    const heading = screen.getByRole('heading', { name: 'Installed locally' })
    expect(heading.style.borderTop).toBe('')
    expect(heading.style.paddingTop).toBe('')
  })
})
