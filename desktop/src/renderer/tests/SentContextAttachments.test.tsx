import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { UserMessageBlock } from '../components/conversation/UserMessageBlock'
import { buildComposerInputParts } from '../utils/composeInputParts'
import { createOptimisticUserMessage } from '../utils/inputPresentation'
import { wireItemToConversationItem } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'

const page = { id: 'page', kind: 'pageReference' as const, url: 'https://example.test', title: 'Example page', selectionKind: 'region' as const,
  text: 'Selected button', comment: 'Make this easier to find', image: { tempPath: '/attachments/page.png', fileName: 'page.png', mimeType: 'image/png' } }
beforeEach(() => { installDesktopApiMock({
  settings: { get: async () => ({ locale: 'en' }) },
  workspace: { readImageAsDataUrl: vi.fn(async () => ({ dataUrl: 'data:image/png;base64,AA==' })) }
}) })

it('keeps the same read-only annotation after acknowledgement and reload, without a duplicate ordinary screenshot', async () => {
  const { inputParts } = buildComposerInputParts({ text: 'Please update it', contexts: [page] })
  const local = createOptimisticUserMessage(inputParts, 'Please update it', 'submission')
  const persisted = wireItemToConversationItem(JSON.parse(JSON.stringify({ id: 'server', type: 'userMessage', payload: {
    clientUserMessageId: 'submission', nativeInputParts: inputParts, images: [{ path: page.image.tempPath }], text: 'Please update it'
  } })))
  const { rerender } = render(<LocaleProvider><UserMessageBlock {...local} text={local.text!} /></LocaleProvider>)
  fireEvent.click(screen.getByRole('button', { name: '1 annotation' }))
  expect(await screen.findByText(page.comment)).toBeVisible()
  expect(await screen.findByRole('img', { name: 'Screenshot attached' })).toBeVisible()
  expect(screen.queryByRole('button', { name: /Remove/ })).toBeNull()
  expect(screen.queryByRole('button', { name: /Edit/ })).toBeNull()
  rerender(<LocaleProvider><UserMessageBlock {...persisted} text={persisted.text!} /></LocaleProvider>)
  await waitFor(() => expect(screen.getAllByRole('img')).toHaveLength(1))
  expect(screen.getByText(page.comment)).toBeVisible()
  expect(screen.getByText('Please update it')).toBeVisible()
})

it('retains annotation content when its screenshot cannot be read', async () => {
  vi.mocked(window.api.workspace.readImageAsDataUrl).mockRejectedValue(new Error('missing'))
  const { inputParts } = buildComposerInputParts({ text: '', contexts: [page] })
  render(<LocaleProvider><UserMessageBlock text="" nativeInputParts={inputParts} /></LocaleProvider>)
  fireEvent.click(screen.getByRole('button', { name: '1 annotation' }))
  expect(await screen.findByText(page.comment)).toBeVisible()
  await waitFor(() => expect(window.api.workspace.readImageAsDataUrl).toHaveBeenCalled())
  expect(screen.queryByRole('img')).toBeNull()
})

it('shows both URL and local images in a native message', async () => {
  render(<LocaleProvider><UserMessageBlock text="" nativeInputParts={[
    { type: 'image', url: 'https://example.test/remote.png' },
    { type: 'localImage', path: '/attachments/mixed-local.png', fileName: 'local.png', mimeType: 'image/png' }
  ]} /></LocaleProvider>)
  await waitFor(() => expect(screen.getAllByRole('button', { name: /View attached image/ })).toHaveLength(2))
  expect(screen.getAllByRole('button', { name: /View attached image/ }).map(button => button.querySelector('img')?.getAttribute('src'))).toEqual([
    'https://example.test/remote.png', 'data:image/png;base64,AA=='
  ])
})
