import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { LayerBoundary } from '../contexts/LayerContext'
import { SidebarEntryDetailsCard } from '../components/sidebar/SidebarEntryDetailsCard'
import { useTransientOverlayStore } from '../stores/transientOverlayStore'

afterEach(() => {
  act(() => {
    useTransientOverlayStore.setState({ openDepths: [], topDepth: 0 })
  })
})

function Fixture({ layerOpen }: { layerOpen: boolean }): JSX.Element {
  return (
    <>
      <SidebarEntryDetailsCard label="Project" width={320} interactive content={<div>details</div>}>
        <button type="button" data-testid="row">row</button>
      </SidebarEntryDetailsCard>
      {layerOpen && (
        <LayerBoundary>
          <div>dialog body</div>
        </LayerBoundary>
      )}
    </>
  )
}

describe('SidebarEntryDetailsCard × transient-overlay suppression', () => {
  it('hides the shown card when a layer opens above it, without any pointer movement', async () => {
    const { rerender } = render(<Fixture layerOpen={false} />)

    // Focus the row → the interactive card shows immediately.
    fireEvent.focus(screen.getByTestId('row'))
    expect(screen.getByText('details')).toBeInTheDocument()

    // A dialog opens above (registers a layer). The stuck-card bug was that no
    // mouseleave fires under the new modal; layer suppression must still hide it.
    rerender(<Fixture layerOpen={true} />)
    await waitFor(() => expect(screen.queryByText('details')).not.toBeInTheDocument())
  })
})
