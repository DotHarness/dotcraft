import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ContextCommentEditor } from '../components/conversation/ContextCommentEditor'
import { LineCommentCard } from '../components/code/LineCommentCard'
import { registerComposerSend, sendComposer } from '../components/conversation/composerSend'
import { useToastStore } from '../stores/toastStore'

function typeComment(): HTMLElement {
  const input = screen.getByRole('textbox')
  fireEvent.change(input, { target: { value: 'Tighten this spacing' } })
  return input
}

describe('comment send-now shortcut', () => {
  beforeEach(() => {
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      voice: {
        getSnapshot: vi.fn(() => new Promise(() => {})),
        onSnapshot: vi.fn(() => () => {}),
        onSessionEvent: vi.fn(() => () => {}),
        getMicrophonePermissionStatus: vi.fn(() => new Promise(() => {}))
      }
    })
  })

  it('adds on Enter and adds and sends on Ctrl+Enter in an annotation', () => {
    const onSave = vi.fn()
    const onSend = vi.fn()
    render(<LocaleProvider><ContextCommentEditor onSave={onSave} onSend={onSend} onCancel={() => {}} /></LocaleProvider>)
    const input = typeComment()

    fireEvent.keyDown(input, { key: 'Enter' })
    fireEvent.keyDown(input, { key: 'Enter', ctrlKey: true })

    expect(onSave).toHaveBeenCalledTimes(1)
    expect(onSend).toHaveBeenCalledWith('Tighten this spacing')
  })

  it('only saves when editing a saved annotation', () => {
    const onSave = vi.fn()
    const onSend = vi.fn()
    render(<LocaleProvider><ContextCommentEditor mode="edit" onSave={onSave} onSend={onSend} onCancel={() => {}} /></LocaleProvider>)

    fireEvent.keyDown(typeComment(), { key: 'Enter', metaKey: true })

    expect(onSave).toHaveBeenCalledTimes(1)
    expect(onSend).not.toHaveBeenCalled()
  })

  it.each([
    ['Ctrl+Enter', (input: HTMLElement) => fireEvent.keyDown(input, { key: 'Enter', ctrlKey: true }), 1],
    ['its button', () => fireEvent.click(screen.getByRole('button', { name: 'Comment' })), 0]
  ] as const)('sends a line comment draft from %s only when asked', (_, act, sends) => {
    const onSubmit = vi.fn()
    const onSend = vi.fn()
    render(<LocaleProvider>
      <LineCommentCard label="R 12" saved={false} initialComment="" onSubmit={onSubmit} onSend={onSend} />
    </LocaleProvider>)

    act(typeComment())

    expect(onSend).toHaveBeenCalledTimes(sends)
    expect(onSubmit).toHaveBeenCalledTimes(1 - sends)
  })

  it('sends through the mounted composer and reports when none is mounted', () => {
    const send = vi.fn()
    const t = (key: string) => key
    const unregister = registerComposerSend('thread-a', send)
    sendComposer('thread-a', t)
    expect(send).toHaveBeenCalledTimes(1)

    unregister()
    sendComposer('thread-a', t)
    expect(send).toHaveBeenCalledTimes(1)
    expect(useToastStore.getState().toasts.at(-1)?.message).toBe('composer.context.addedNotSent')
  })
})
