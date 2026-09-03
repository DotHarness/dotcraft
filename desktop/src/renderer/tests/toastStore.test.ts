import { beforeEach, describe, expect, it, vi } from 'vitest'
import { addToast, showToast, useToastStore } from '../stores/toastStore'

beforeEach(() => {
  useToastStore.setState({ toasts: [] })
})

describe('toast replacement', () => {
  it('replaces a toast that shares its key and expires the old one', () => {
    const onExpire = vi.fn()
    showToast({ message: 'Committing…', key: 'commit', durationMs: 0, onExpire })
    const doneId = showToast({ message: 'Committed', key: 'commit', type: 'success' })

    const toasts = useToastStore.getState().toasts
    expect(toasts.map((t) => t.id)).toEqual([doneId])
    expect(toasts[0].message).toBe('Committed')
    expect(onExpire).toHaveBeenCalledTimes(1)
  })

  it('coalesces an identical plain notice instead of stacking it', () => {
    addToast('Copied to clipboard', 'success', 2000)
    const second = addToast('Copied to clipboard', 'success', 2000)

    expect(useToastStore.getState().toasts.map((t) => t.id)).toEqual([second])
  })

  it('keeps identical messages apart when either carries an action', () => {
    showToast({ message: 'Chat archived', action: { label: 'Undo', onClick: () => {} } })
    showToast({ message: 'Chat archived', action: { label: 'Undo', onClick: () => {} } })
    addToast('Chat archived')

    expect(useToastStore.getState().toasts).toHaveLength(3)
  })
})

describe('toast settlement', () => {
  it('commits through onExpire when removed without the action', () => {
    const onClick = vi.fn()
    const onExpire = vi.fn()
    const id = showToast({ message: 'Approved task.', action: { label: 'Undo', onClick }, onExpire })

    useToastStore.getState().removeToast(id)
    useToastStore.getState().removeToast(id)

    expect(onExpire).toHaveBeenCalledTimes(1)
    expect(onClick).not.toHaveBeenCalled()
    expect(useToastStore.getState().toasts).toHaveLength(0)
  })

  it('does not expire a toast whose action already ran', () => {
    const onClick = vi.fn()
    const onExpire = vi.fn()
    const id = showToast({ message: 'Approved task.', action: { label: 'Undo', onClick }, onExpire })

    useToastStore.getState().settleToast(id, 'action')
    useToastStore.getState().settleToast(id, 'action')
    useToastStore.getState().removeToast(id)

    expect(onClick).toHaveBeenCalledTimes(1)
    expect(onExpire).not.toHaveBeenCalled()
  })
})

describe('toast durations', () => {
  it('holds an action toast longer than a plain notice', () => {
    const plain = showToast({ message: 'Saved' })
    const undo = showToast({ message: 'Chat archived', action: { label: 'Undo', onClick: () => {} } })
    const byId = new Map(useToastStore.getState().toasts.map((t) => [t.id, t.duration]))

    expect(byId.get(plain)).toBe(5000)
    expect(byId.get(undo)).toBe(8000)
  })
})
