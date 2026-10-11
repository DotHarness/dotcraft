// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ChatsFolderRow } from '../components/settings/panels/ChatsFolderRow'
import { installDesktopApiMock } from './desktopApiMock'

function renderRow(customized: boolean): { set: ReturnType<typeof vi.fn> } {
  const set = vi.fn().mockResolvedValue(undefined)
  installDesktopApiMock({
    initialLocale: 'en',
    settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }), set },
    workspace: { getChatsFolder: vi.fn().mockResolvedValue({ path: '/data/chats', customized }) }
  })
  render(
    <LocaleProvider>
      <ChatsFolderRow />
    </LocaleProvider>
  )
  return { set }
}

it('offers Reset only for a customized folder', async () => {
  renderRow(false)

  await screen.findByTitle('/data/chats')
  expect(screen.queryByRole('button', { name: 'Reset' })).not.toBeInTheDocument()
})

it('resets a customized folder to the default', async () => {
  const { set } = renderRow(true)

  fireEvent.click(await screen.findByRole('button', { name: 'Reset' }))

  await waitFor(() => expect(set).toHaveBeenCalledWith({ chatsFolder: '' }))
})
