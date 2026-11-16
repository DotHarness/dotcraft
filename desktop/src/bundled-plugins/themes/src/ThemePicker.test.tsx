import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { DesktopPluginHost } from '@dotcraft/plugin'
import type { PropsWithChildren, ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Theme } from '../../../shared/themes'
import { ThemePicker } from './ThemePicker'
import { stringsFor } from './i18n'

const mocks = vi.hoisted(() => ({
  state: {
    themes: [] as Theme[],
    preview: null as Theme | null,
    loading: false,
    error: null as string | null
  },
  cancel: vi.fn(),
  apply: vi.fn(),
  author: vi.fn(),
  setChoice: vi.fn()
}))

vi.mock('@dotcraft/plugin', () => ({
  Button: ({ children, onClick, disabled }: PropsWithChildren<{ onClick(): void; disabled?: boolean }>) => (
    <button onClick={onClick} disabled={disabled}>
      {children}
    </button>
  ),
  IconButton: ({ label }: { label: string }) => <button aria-label={label} />,
  Menu: ({ items, children }: PropsWithChildren<{ items: { label?: string; onSelect?(): void }[] }>) => (
    <>
      {children}
      {items.map((item) => item.onSelect && (
        <button key={item.label} role="menuitem" onClick={item.onSelect}>
          {item.label}
        </button>
      ))}
    </>
  ),
  SegmentedControl: ({ options, onValueChange, ariaLabel }: { options: { value: string; label: string }[]; onValueChange(value: string): void; ariaLabel: string }) => (
    <div role="group" aria-label={ariaLabel}>
      {options.map((option) => (
        <button key={option.value} onClick={() => onValueChange(option.value)}>
          {option.label}
        </button>
      ))}
    </div>
  ),
  Select: () => null,
  SettingsGroup: ({ children, headerAction }: PropsWithChildren<{ headerAction: ReactNode }>) => (
    <section>
      {headerAction}
      {children}
    </section>
  ),
  Skeleton: () => <span />
}))
vi.mock('./themeLibrary', () => ({
  useThemeLibrary: () => mocks.state,
  themeById: (id: string) => mocks.state.themes.find((theme) => theme.id === id),
  cancelThemePreview: mocks.cancel,
  applyTheme: mocks.apply,
  startThemeAuthoring: mocks.author
}))
vi.mock('./choices', () => ({ setChoice: mocks.setChoice }))
vi.mock('./thumbnails', () => ({ useThemeThumbnail: () => null }))

const svg = '<svg></svg>'
const theme: Theme = {
  id: 'garden', name: 'Garden', colorScheme: 'light', revision: 2, updatedAt: '2026-11-15T12:00:00Z',
  backdrop: { kind: 'scene', format: 'svg', source: svg, poster: svg }
}
const strings = stringsFor('en')
const confirm = vi.fn()
const remove = vi.fn()
const save = vi.fn()
const host = { ui: { confirm }, environment: { locale: 'en' } } as unknown as DesktopPluginHost
const everywhere = { kind: 'everywhere' as const }
const props = { host, appearance: 'light' as const, active: { kind: 'none' as const }, strings, onAppearance: vi.fn() }

beforeEach(() => {
  vi.clearAllMocks()
  mocks.state = { themes: [theme], preview: null, loading: false, error: null }
  Object.defineProperty(window, 'api', { configurable: true, value: { themes: { remove, save } } })
})

describe('theme selection', () => {
  it('applies a theme or No theme everywhere with one click', () => {
    render(<ThemePicker {...props} />)
    fireEvent.click(screen.getByRole('button', { name: theme.name }))
    expect(mocks.apply).toHaveBeenCalledWith(theme, everywhere)
    fireEvent.click(screen.getByRole('button', { name: strings.noneTile }))
    expect(mocks.setChoice).toHaveBeenLastCalledWith(everywhere, { light: { kind: 'none' } })
  })

  it('reports a failed deletion and lets the user retry', async () => {
    confirm.mockResolvedValue(true)
    remove.mockRejectedValueOnce(new Error('disk unavailable')).mockResolvedValueOnce(undefined)
    render(<ThemePicker {...props} />)
    const removeButton = screen.getByRole('menuitem', { name: strings.removeTheme })
    fireEvent.click(removeButton)
    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
    fireEvent.click(removeButton)
    await waitFor(() => expect(remove).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
  })

  it('edits a theme from its tile menu without applying it', async () => {
    render(<ThemePicker {...props} />)
    fireEvent.click(screen.getByRole('menuitem', { name: strings.edit }))
    await waitFor(() => expect(mocks.author).toHaveBeenCalledWith(host, theme))
    expect(mocks.apply).not.toHaveBeenCalled()
  })

  it('imports a copy without the exported revision and applies it after saving', async () => {
    save.mockResolvedValue({ ...theme, id: 'imported' })
    const exported = { ...theme, builtin: true, expectedRevision: 4, authorThreadId: 'old-thread', pair: 'garden-night' }
    render(<ThemePicker {...props} />)
    fireEvent.change(screen.getByLabelText(strings.importTheme, { selector: 'input' }), {
      target: { files: [{ size: 100, text: async () => JSON.stringify(exported) }] }
    })
    await waitFor(() => expect(save).toHaveBeenCalledOnce())
    const imported = save.mock.calls[0][0]
    expect(imported.id).not.toBe(theme.id)
    expect(imported.expectedRevision).toBeUndefined()
    expect(imported.authorThreadId).toBeUndefined()
    expect(imported.pair).toBeUndefined()
    expect(imported.backdrop).toEqual(theme.backdrop)
    await waitFor(() => expect(mocks.apply).toHaveBeenCalledWith({ ...theme, id: 'imported' }, everywhere))
  })

  it('does not apply an import that finishes after leaving settings', async () => {
    let finishSave!: (value: Theme) => void
    save.mockImplementationOnce(() => new Promise<Theme>((resolve) => { finishSave = resolve }))
    const view = render(<ThemePicker {...props} />)
    fireEvent.change(screen.getByLabelText(strings.importTheme, { selector: 'input' }), {
      target: { files: [{ size: 100, text: async () => JSON.stringify(theme) }] }
    })
    await waitFor(() => expect(save).toHaveBeenCalledOnce())
    view.unmount()
    await act(async () => finishSave(theme))
    expect(mocks.apply).not.toHaveBeenCalled()
  })
})
