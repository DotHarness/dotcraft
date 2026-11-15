import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { mkdtemp, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { ThemeChoiceStore } from './themeChoiceStore'
import { resolveThemeChoice, THEME_MAX_CHAT_CHOICES } from '../../shared/themes'

let directory: string
let path: string
let clock: number
let store: ThemeChoiceStore
beforeEach(async () => {
  directory = await mkdtemp(join(tmpdir(), 'dotcraft-themes-choices-'))
  path = join(directory, 'choices.json')
  clock = 0
  store = new ThemeChoiceStore(path, () => ++clock)
})
afterEach(async () => { await rm(directory, { recursive: true, force: true }) })

const scene = (id: string) => ({ kind: 'theme' as const, id })

describe('themes choices', () => {
  it('resolves a chat over everywhere for each appearance separately', async () => {
    await store.set({ kind: 'everywhere' }, { light: scene('everywhere-light'), dark: scene('everywhere-dark') })
    await store.set({ kind: 'chat', threadId: 'thread' }, { light: { kind: 'none' } })
    const choices = await new ThemeChoiceStore(path).get()
    const context = { threadId: 'thread' }

    expect(resolveThemeChoice(choices, 'light', context)).toEqual({ choice: { kind: 'none' }, scope: 'chat' })
    expect(resolveThemeChoice(choices, 'dark', context)).toEqual({ choice: scene('everywhere-dark'), scope: 'everywhere' })
    expect(resolveThemeChoice(choices, 'light', { threadId: null }).choice).toEqual(scene('everywhere-light'))
    expect(resolveThemeChoice(choices, 'dark', context, () => false).choice).toEqual({ kind: 'none' })
  })

  it('clears a slot back to the wider scope and drops an emptied scope', async () => {
    await store.set({ kind: 'everywhere' }, { dark: scene('wide') })
    await store.set({ kind: 'chat', threadId: 'thread' }, { dark: scene('narrow') })
    const cleared = await store.set({ kind: 'chat', threadId: 'thread' }, { dark: null })
    expect(cleared.chats).toEqual({})
    expect(resolveThemeChoice(cleared, 'dark', { threadId: 'thread' }).choice).toEqual(scene('wide'))
  })

  it('removes a deleted theme from every scope without touching other choices', async () => {
    await store.set({ kind: 'everywhere' }, { light: scene('gone'), dark: scene('kept') })
    await store.set({ kind: 'chat', threadId: 'thread' }, { dark: scene('gone') })
    const choices = await store.forget('gone')
    expect(choices).toEqual({ everywhere: { dark: scene('kept') }, chats: {} })
  })

  it('keeps only the chats that chose most recently', async () => {
    for (let index = 0; index <= THEME_MAX_CHAT_CHOICES; index += 1) {
      await store.set({ kind: 'chat', threadId: `thread-${index}` }, { dark: scene('sky') })
    }
    const { chats } = await store.get()
    expect(Object.keys(chats)).toHaveLength(THEME_MAX_CHAT_CHOICES)
    expect(chats['thread-0']).toBeUndefined()
    expect(chats[`thread-${THEME_MAX_CHAT_CHOICES}`]).toBeDefined()
  })

  it('rejects an invalid choice and leaves the stored choices intact', async () => {
    await store.set({ kind: 'everywhere' }, { dark: scene('kept') })
    await expect(store.set({ kind: 'everywhere' }, { dark: { kind: 'video', id: 'x' } })).rejects.toThrow()
    await expect(store.set({ kind: 'planet' }, { dark: scene('x') })).rejects.toThrow()
    expect((await store.get()).everywhere.dark).toEqual(scene('kept'))
  })
})
