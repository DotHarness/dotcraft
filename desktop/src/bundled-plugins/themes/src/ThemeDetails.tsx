import { Button, Select, type DesktopPluginHost } from '@dotcraft/plugin'
import type { JSX } from 'react'
import type { Theme, ThemeImageFit } from '../../../shared/themes'
import { themeDisplayName, type ThemeStrings } from './i18n'
import { startThemeAuthoring } from './themeLibrary'
import { exportTheme } from './themeTransfer'

interface ThemeDetailsProps {
  host: DesktopPluginHost
  theme: Theme
  strings: ThemeStrings
  busy: boolean
  run(work: () => void | Promise<void>): void
}

export function ThemeDetails({ host, theme, strings, busy, run }: ThemeDetailsProps): JSX.Element {
  const image = !theme.builtin && theme.backdrop?.kind === 'image' ? theme.backdrop : null
  const saveFit = async (fit: ThemeImageFit): Promise<void> => {
    if (!image) return
    const { revision, updatedAt: _updatedAt, authorThreadId: _author, builtin: _builtin, ...input } = theme
    await window.api.themes.save({ ...input, backdrop: { ...image, fit }, expectedRevision: revision })
  }
  return (
    <div className="dcw-preview" role="region" aria-label={themeDisplayName(theme, host.environment.locale)}>
      <div className="dcw-preview-heading">
        <strong>{themeDisplayName(theme, host.environment.locale)}</strong>
      </div>
      <div className="dcw-theme-actions">
        {image && (
          <Select<ThemeImageFit>
            value={image.fit}
            ariaLabel={strings.fitLabel}
            options={[
              { value: 'cover', label: strings.fitCover },
              { value: 'contain', label: strings.fitContain },
              { value: 'tile', label: strings.fitTile }
            ]}
            onValueChange={(fit) => run(() => saveFit(fit))}
          />
        )}
        {(!theme.builtin || theme.backdrop?.kind !== 'image') && (
          <Button size="sm" variant="secondary" disabled={busy} onClick={() => run(() => startThemeAuthoring(host, theme))}>
            {strings.edit}
          </Button>
        )}
        {(!theme.builtin || theme.backdrop?.kind !== 'image') && (
          <Button size="sm" variant="secondary" disabled={busy} onClick={() => run(() => exportTheme(theme))}>
            {strings.exportTheme}
          </Button>
        )}
      </div>
    </div>
  )
}
