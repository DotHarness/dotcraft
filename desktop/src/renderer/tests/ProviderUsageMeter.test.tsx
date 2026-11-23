import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { ProviderUsageMeter } from '../components/settings/providers/ProviderUsageMeter'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useProvidersStore } from '../stores/providersStore'
import { activateTestWorkspace } from './workspaceResourceFixtures'

afterEach(() => {
  cleanup()
  useProvidersStore.getState().reset()
})

it('dismisses usage with Escape while the trigger keeps focus without forwarding keys to the provider row', () => {
  activateTestWorkspace()
  useProvidersStore.getState().applyUsage('provider', {
    available: true,
    planType: null,
    primary: null,
    secondary: null,
    credits: null,
    balances: [{ currency: 'USD', total: '10', granted: '0', toppedUp: '10' }],
    limitReachedKind: null,
    fetchedAt: null,
    pluginId: null
  })
  const rowKeyDown = vi.fn()
  render(
    <LocaleProvider loadSettings={false}>
      <div onKeyDown={rowKeyDown}>
        <ProviderUsageMeter provider={{
          id: 'provider', displayName: 'Provider', protocol: 'openai', hasApiKey: true, endPoint: ''
        }} />
      </div>
    </LocaleProvider>
  )
  const trigger = screen.getByRole('button', { expanded: false })
  trigger.focus()
  fireEvent.click(trigger)
  expect(screen.getByRole('dialog')).toBeTruthy()

  fireEvent.keyDown(trigger, { key: 'Escape' })

  expect(screen.queryByRole('dialog')).toBeNull()
  expect(trigger.getAttribute('aria-expanded')).toBe('false')
  expect(document.activeElement).toBe(trigger)
  expect(rowKeyDown).not.toHaveBeenCalled()
})

it('shows named quota pools inside the usage popover when pool_order metadata is present', () => {
  activateTestWorkspace()
  useProvidersStore.getState().applyUsage('plugin-provider', {
    available: true,
    planType: null,
    primary: { usedPercent: 14, windowSeconds: 18_000, resetAt: '2099-01-01T05:00:00Z' },
    secondary: { usedPercent: 58, windowSeconds: 604_800, resetAt: '2099-01-08T00:00:00Z' },
    credits: null,
    balances: null,
    limitReachedKind: null,
    fetchedAt: '2099-01-01T00:00:00Z',
    pluginId: 'sample-plugin',
    values: {
      'pool.pool_a.remaining': 86,
      'pool.pool_b.remaining': 42
    },
    labels: {
      pool_order: 'pool_a,pool_b',
      'pool.pool_a.name': 'Pool A (5h)',
      'pool.pool_b.name': 'Pool B (Weekly)'
    }
  })

  render(
    <LocaleProvider loadSettings={false}>
      <ProviderUsageMeter provider={{
        id: 'plugin-provider', displayName: 'Plugin Provider', protocol: 'custom', backendId: 'sample-backend', pluginId: 'sample-plugin', hasApiKey: false, endPoint: ''
      }} />
    </LocaleProvider>
  )

  const trigger = screen.getByRole('button', { expanded: false })
  fireEvent.click(trigger)
  const dialog = screen.getByRole('dialog')
  expect(dialog.textContent).toContain('Pool A (5h)')
  expect(dialog.textContent).toContain('Pool B (Weekly)')
})
