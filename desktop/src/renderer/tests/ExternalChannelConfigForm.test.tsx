import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import {
  ExternalChannelConfigForm,
  type ExternalChannelConfigWire
} from '../components/channels/ExternalChannelConfigForm'

const baseValue: ExternalChannelConfigWire = {
  name: 'Custom',
  enabled: true,
  transport: 'subprocess',
  command: 'node',
  args: ['adapter.js'],
  workingDirectory: 'C:\\adapter',
  env: { TOKEN: 'secret' }
}

describe('ExternalChannelConfigForm', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: vi.fn().mockResolvedValue({ locale: 'en' })
        }
      }
    })
  })

  it('uses the shared select and clears process launcher fields for websocket transport', async () => {
    const onChange = vi.fn()
    render(
      <LocaleProvider>
        <ExternalChannelConfigForm
          value={baseValue}
          saving={false}
          deleting={false}
          isNew={false}
          hideHeader
          status="notConfigured"
          statusLabel="Not configured"
          onChange={onChange}
          onSave={vi.fn()}
        />
      </LocaleProvider>
    )

    const transport = await screen.findByRole('combobox', { name: 'Transport' })
    expect(transport.tagName).toBe('BUTTON')
    fireEvent.click(transport)
    fireEvent.click(screen.getByRole('option', { name: 'WebSocket' }))

    expect(onChange).toHaveBeenCalledWith({
      ...baseValue,
      transport: 'websocket',
      command: null,
      args: null,
      workingDirectory: null,
      env: null
    })
  })
})
