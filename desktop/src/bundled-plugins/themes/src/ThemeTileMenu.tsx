import { IconButton, Menu, type DesktopPluginHost, type MenuEntry } from '@dotcraft/plugin'
import type { JSX } from 'react'
import type { Theme, ThemeImageFit } from '../../../shared/themes'
import type { ThemeStrings } from './i18n'
import { startThemeAuthoring } from './themeLibrary'
import { exportTheme } from './themeTransfer'

interface ThemeTileMenuProps {
  host: DesktopPluginHost
  theme: Theme
  name: string
  strings: ThemeStrings
  busy: boolean
  run(work: () => void | Promise<void>): void
  onRemove(theme: Theme): Promise<void>
}

function MoreIcon(): JSX.Element {
  return (
    <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
      <circle cx="3.5" cy="8" r="1.3" />
      <circle cx="8" cy="8" r="1.3" />
      <circle cx="12.5" cy="8" r="1.3" />
    </svg>
  )
}

export function ThemeTileMenu({ host, theme, name, strings, busy, run, onRemove }: ThemeTileMenuProps): JSX.Element | null {
  const image = !theme.builtin && theme.backdrop?.kind === 'image' ? theme.backdrop : null
  const items: MenuEntry[] = []
  if (!theme.builtin || theme.backdrop?.kind !== 'image') {
    items.push(
      { label: strings.edit, onSelect: () => run(() => startThemeAuthoring(host, theme)) },
      { label: strings.exportTheme, onSelect: () => run(() => exportTheme(theme)) }
    )
  }
  if (image) {
    const saveFit = async (fit: ThemeImageFit): Promise<void> => {
      const { revision, updatedAt: _updatedAt, authorThreadId: _author, builtin: _builtin, ...input } = theme
      await window.api.themes.save({ ...input, backdrop: { ...image, fit }, expectedRevision: revision })
    }
    const fits: readonly (readonly [ThemeImageFit, string])[] = [
      ['cover', strings.fitCover],
      ['contain', strings.fitContain],
      ['tile', strings.fitTile]
    ]
    items.push({ type: 'separator' }, { type: 'label', label: strings.fitLabel })
    for (const [fit, label] of fits) {
      items.push({ label, checked: image.fit === fit, onSelect: () => run(() => saveFit(fit)) })
    }
  }
  if (!theme.builtin) {
    items.push({ type: 'separator' }, { label: strings.removeTheme, danger: true, onSelect: () => run(() => onRemove(theme)) })
  }
  if (items.length === 0) return null

  return (
    <Menu items={items}>
      <IconButton
        className="dcw-tile-menu"
        icon={<MoreIcon />}
        label={`${strings.themeActions}: ${name}`}
        size={22}
        disabled={busy}
      />
    </Menu>
  )
}
