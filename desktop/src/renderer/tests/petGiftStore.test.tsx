import { beforeEach, describe, expect, it } from 'vitest'
import { items } from '@dotcraft/avatar'
import { presentFind, registerGiftStage, useGiftStore } from '../pet/giftStore'
import { useToastStore } from '../stores/toastStore'

const ITEM = items[0].id

beforeEach(() => {
  useGiftStore.setState({ gifts: [], opened: null, openRevision: 0, stages: 0 })
  useToastStore.setState({ toasts: [] })
})

describe('find gifts', () => {
  it('announces a find with the toast when no composer mascot can hold the gift', () => {
    presentFind(ITEM)
    expect(useGiftStore.getState().gifts).toHaveLength(0)
    expect(useToastStore.getState().toasts.map((toast) => toast.key)).toEqual(['pet-find'])
  })

  it('keeps finds as unopened gifts while a mascot is mounted, and opening one reveals it', () => {
    const unregister = registerGiftStage()
    presentFind(ITEM)
    presentFind(ITEM)
    const [first, second] = useGiftStore.getState().gifts
    expect(useToastStore.getState().toasts).toHaveLength(0)

    useGiftStore.getState().open(first.key)
    expect(useGiftStore.getState()).toMatchObject({ gifts: [second], opened: first, openRevision: 1 })

    unregister()
    presentFind(ITEM)
    expect(useToastStore.getState().toasts).toHaveLength(1)
  })
})
