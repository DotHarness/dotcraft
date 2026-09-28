import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ImportSyncDialog } from '../components/settings/panels/ImportSyncDialog'

describe('import sync selection', () => {
  it('saves the explicit future-category choice and retains the custom selection', async () => {
    const onSave = vi.fn().mockResolvedValue(undefined)
    const onClose = vi.fn()
    render(<LocaleProvider loadSettings={false}><ImportSyncDialog
      selection={{ all: false, user: ['skills'], workspace: ['commands'], sessions: true }} onSave={onSave} onClose={onClose} />
    </LocaleProvider>)
    fireEvent.click(screen.getByRole('checkbox', { name: 'Sync all categories' }))
    expect(screen.getByRole('checkbox', { name: 'Chat sessions' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(onSave).toHaveBeenCalledWith({ all: true, user: ['skills'], workspace: ['commands'], sessions: true }))
    await waitFor(() => expect(onClose).toHaveBeenCalledOnce())
  })

  it('requires at least one category before saving', () => {
    const onSave = vi.fn().mockResolvedValue(undefined)
    render(<LocaleProvider loadSettings={false}><ImportSyncDialog
      selection={{ all: false, user: [], workspace: [], sessions: false }} onSave={onSave} onClose={vi.fn()} />
    </LocaleProvider>)
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Tools & setup' }))
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Tools & setup' }))
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })

  it('keeps the dialog open on a failed settings write', async () => {
    const onClose = vi.fn()
    render(<LocaleProvider loadSettings={false}><ImportSyncDialog
      selection={{ all: false, user: [], workspace: [], sessions: true }}
      onSave={async () => { throw new Error('write failed') }} onClose={onClose} />
    </LocaleProvider>)
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('write failed')
    expect(onClose).not.toHaveBeenCalled()
  })
})
