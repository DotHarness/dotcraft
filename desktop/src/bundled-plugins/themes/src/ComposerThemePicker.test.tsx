import { fireEvent, render, screen } from '@testing-library/react'
import type { DesktopPluginHost, DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Theme, ThemeScope } from '../../../shared/themes'
import { ComposerThemePicker } from './ComposerThemePicker'
import { stringsFor } from './i18n'

const mocks = vi.hoisted(() => ({
  threadId: null as string | null,
  setChoice: vi.fn(),
  apply: vi.fn()
}))

const day: Theme = { id: 'day', name: 'Day', colorScheme: 'light', revision: 1, updatedAt: '2026-11-15', colors: { surface: '#ffffff', ink: '#111111', accent: '#3355aa', contrast: 50 } }

vi.mock('./choices', () => ({
  useChoices: () => ({}),
  scopeOf: (kind: ThemeScope['kind']) => (kind === 'everywhere' ? { kind } : mocks.threadId ? { kind, threadId: mocks.threadId } : null),
  choiceAt: () => undefined,
  setChoice: mocks.setChoice
}))
vi.mock('./composerPicker', () => ({ useComposerPickerOpen: () => true, closeComposerPicker: vi.fn() }))
vi.mock('./hooks', () => ({ useSettings: () => ({ enabled: true }), useAppearance: () => 'light' }))
vi.mock('./themeLibrary', () => ({
  useThemeLibrary: () => ({ themes: [day], preview: null }),
  applyTheme: mocks.apply,
  cancelThemePreview: vi.fn()
}))
vi.mock('./ThemePicker', () => ({ ThemeThumbnail: () => null }))

const strings = stringsFor('en')
const host = { environment: { locale: 'en' } } as unknown as DesktopPluginHost
const props = { host, context: {} } as unknown as DesktopPluginSurfaceProps<'composer.before'>
const chat = { kind: 'chat', threadId: 'thread' }

beforeEach(() => {
  vi.clearAllMocks()
  mocks.threadId = 'thread'
})

describe('composer theme picker', () => {
  it('applies a theme to the current chat only', () => {
    render(<ComposerThemePicker {...props} />)
    fireEvent.click(screen.getByRole('button', { name: day.name }))
    expect(mocks.apply).toHaveBeenCalledWith(day, chat)
  })

  it('clears both appearances of the chat choice with Default', () => {
    render(<ComposerThemePicker {...props} />)
    fireEvent.click(screen.getByRole('button', { name: strings.followTile }))
    expect(mocks.setChoice).toHaveBeenCalledWith(chat, { light: null, dark: null })
  })

  it('applies everywhere before the Composer has a chat', () => {
    mocks.threadId = null
    render(<ComposerThemePicker {...props} />)
    expect(screen.queryByRole('button', { name: strings.followTile })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: day.name }))
    expect(mocks.apply).toHaveBeenCalledWith(day, { kind: 'everywhere' })
  })
})
