import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { McpElicitationDialog } from '../components/mcp/McpElicitationDialog'
import { installDesktopApiMock } from './desktopApiMock'

describe('McpElicitationDialog', () => {
  beforeEach(() => {
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      shell: { openExternal: vi.fn() }
    })
  })

  it('uses shared selection controls and returns the accepted schema values', async () => {
    const onRespond = vi.fn()
    render(
      <LocaleProvider>
        <McpElicitationDialog
          request={{
            bridgeId: 'bridge-1',
            serverName: 'Example',
            mode: 'form',
            requestedSchema: {
              type: 'object',
              required: ['environment', 'scopes'],
              properties: {
                environment: {
                  type: 'string',
                  title: 'Environment',
                  enum: ['staging', 'production'],
                  enumNames: ['Staging', 'Production'],
                  default: 'staging'
                },
                remember: {
                  type: 'boolean',
                  title: 'Remember this choice'
                },
                scopes: {
                  type: 'array',
                  title: 'Permissions',
                  minItems: 1,
                  items: {
                    anyOf: [
                      { const: 'read', title: 'Read' },
                      { const: 'write', title: 'Write' }
                    ]
                  }
                }
              }
            }
          }}
          onRespond={onRespond}
        />
      </LocaleProvider>
    )

    const environment = await screen.findByRole('combobox', { name: 'Environment' })
    expect(environment.tagName).toBe('BUTTON')
    expect(screen.getByRole('checkbox', { name: 'Remember this choice' })).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'Read' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled()

    fireEvent.click(environment)
    fireEvent.click(screen.getByRole('option', { name: 'Production' }))
    fireEvent.click(screen.getByRole('checkbox', { name: 'Remember this choice' }))
    fireEvent.click(screen.getByRole('checkbox', { name: 'Read' }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled())
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }))

    expect(onRespond).toHaveBeenCalledWith({
      action: 'accept',
      content: {
        environment: 'production',
        remember: true,
        scopes: ['read']
      }
    })
  })
})
