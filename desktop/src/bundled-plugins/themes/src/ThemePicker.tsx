import { Button, SegmentedControl, SettingsGroup, Skeleton, type DesktopPluginHost } from '@dotcraft/plugin'
import { useEffect, useRef, useState, type ChangeEvent, type JSX } from 'react'
import {
  THEME_MAX_IMAGE_BYTES, THEME_MAX_POSTER_BYTES, THEME_MAX_SOURCE_BYTES,
  type Theme, type ThemeAppearance, type ThemeChoice
} from '../../../shared/themes'
import { setChoice } from './choices'
import { themeDisplayName, type ThemeStrings } from './i18n'
import { applyTheme, cancelThemePreview, startThemeAuthoring, useThemeLibrary } from './themeLibrary'
import { backdropStyle } from './ThemeSurfaces'
import { ThemeTileMenu } from './ThemeTileMenu'
import { useThemeThumbnail } from './thumbnails'
import { readImageDataUrl, themeImportInput } from './themeTransfer'

// JSON escaping can expand each source byte to six characters, and an image is stored as base64.
const MAX_THEME_FILE_BYTES = 6 * (THEME_MAX_SOURCE_BYTES + THEME_MAX_POSTER_BYTES) + 2 * THEME_MAX_IMAGE_BYTES

interface ThemePickerProps {
  host: DesktopPluginHost
  appearance: ThemeAppearance
  active: ThemeChoice
  strings: ThemeStrings
  onAppearance(appearance: ThemeAppearance): void
}

export function ThemeThumbnail({ theme }: { theme: Theme }): JSX.Element {
  const { backdrop, colors } = theme
  const bitmap = useThemeThumbnail(theme)
  if (backdrop?.kind === 'scene' || backdrop?.kind === 'image') {
    return bitmap
      ? <img className="dcw-theme-thumbnail" src={bitmap} alt="" decoding="async" draggable={false} />
      : <span className="dcw-theme-thumbnail" style={{ background: theme.tint }} />
  }
  if (backdrop) return <span className="dcw-theme-thumbnail" style={backdropStyle(backdrop)} />
  return (
    <span className="dcw-theme-thumbnail dcw-theme-swatch" style={{ background: colors?.surface, color: colors?.ink }}>
      Aa
      <span className="dcw-theme-swatch-accent" style={{ background: colors?.accent }} />
    </span>
  )
}

export function ThemePicker({ host, appearance, active, strings, onAppearance }: ThemePickerProps): JSX.Element {
  const library = useThemeLibrary()
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const themeInput = useRef<HTMLInputElement>(null)
  const imageInput = useRef<HTMLInputElement>(null)
  const mounted = useRef(true)
  const previewId = library.preview?.id
  const previewAppearance = library.preview?.colorScheme

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
      cancelThemePreview()
    }
  }, [])
  useEffect(() => {
    if (previewId && previewAppearance) onAppearance(previewAppearance)
  }, [previewId, previewAppearance, onAppearance])

  const run = (work: () => void | Promise<void>): void => {
    if (busy) return
    setBusy(true)
    setActionError(null)
    void Promise.resolve()
      .then(work)
      .catch(() => {
        if (mounted.current) setActionError(strings.themeActionError)
      })
      .finally(() => {
        if (mounted.current) setBusy(false)
      })
  }

  const choose = (choice: ThemeChoice): void => {
    cancelThemePreview()
    setChoice({ kind: 'everywhere' }, { [appearance]: choice })
  }
  const apply = (theme: Theme): void => {
    cancelThemePreview()
    applyTheme(theme, { kind: 'everywhere' })
  }

  const remove = async (theme: Theme): Promise<void> => {
    if (theme.builtin) return
    const confirmed = await host.ui.confirm({
      title: strings.removeThemeTitle,
      message: strings.removeThemeMessage,
      confirmLabel: strings.removeTheme,
      danger: true
    })
    if (!confirmed) return
    await window.api.themes.remove(theme.id)
  }

  const importFile = (event: ChangeEvent<HTMLInputElement>): void => {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file) return
    if (file.size > MAX_THEME_FILE_BYTES) {
      setActionError(strings.themeImportTooLarge)
      return
    }
    run(async () => {
      const theme = await window.api.themes.save(themeImportInput(JSON.parse(await file.text())))
      if (mounted.current) applyTheme(theme, { kind: 'everywhere' })
    })
  }

  const addImage = (event: ChangeEvent<HTMLInputElement>): void => {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file) return
    if (file.size > THEME_MAX_IMAGE_BYTES) {
      setActionError(strings.imageTooLarge)
      return
    }
    run(async () => {
      const theme = await window.api.themes.save({
        id: `image-${crypto.randomUUID()}`,
        name: file.name.replace(/\.[^.]+$/, '').slice(0, 100) || strings.addImage,
        colorScheme: appearance,
        backdrop: { kind: 'image', image: await readImageDataUrl(file), fit: 'cover' }
      })
      if (mounted.current) applyTheme(theme, { kind: 'everywhere' })
    })
  }

  const themes = library.themes.filter((theme) => theme.colorScheme === appearance)
  return (
    <SettingsGroup
      flush
      title={strings.libraryGroup}
      headerAction={
        <div className="dcw-theme-actions">
          <Button size="sm" variant="secondary" disabled={busy} onClick={() => imageInput.current?.click()}>
            {strings.addImage}
          </Button>
          <Button size="sm" variant="secondary" disabled={busy} onClick={() => themeInput.current?.click()}>
            {strings.importTheme}
          </Button>
          <Button size="sm" variant="secondary" disabled={busy} onClick={() => run(() => startThemeAuthoring(host))}>
            {strings.create}
          </Button>
          <input
            ref={imageInput}
            className="dcw-file-input"
            type="file"
            accept="image/jpeg,image/png,image/webp"
            aria-label={strings.addImage}
            onChange={addImage}
          />
          <input
            ref={themeInput}
            className="dcw-file-input"
            type="file"
            accept=".json,application/json"
            aria-label={strings.importTheme}
            onChange={importFile}
          />
        </div>
      }
    >
      <div className="dcw-theme">
        <div className="dcw-theme-filters">
          <SegmentedControl
            value={appearance}
            ariaLabel={strings.libraryGroup}
            options={[
              { value: 'light' as const, label: strings.lightTab },
              { value: 'dark' as const, label: strings.darkTab }
            ]}
            onValueChange={(next) => {
              cancelThemePreview()
              onAppearance(next)
            }}
          />
        </div>
        {(library.error || actionError) && (
          <p className="dcw-theme-error" role="alert">
            {actionError ?? strings.themeLoadError}
          </p>
        )}
        {library.loading && (
          <div role="status" aria-label={strings.themeLoading}>
            <Skeleton width="100%" height={18} />
          </div>
        )}
        <div className="dcw-picker">
          <button
            type="button"
            className="dcw-tile dcw-tile-none"
            aria-pressed={active.kind === 'none'}
            onClick={() => choose({ kind: 'none' })}
          >
            {strings.noneTile}
          </button>
          {themes.map((theme) => {
            const name = themeDisplayName(theme, host.environment.locale)
            return (
              <div key={theme.id} className="dcw-tile-slot">
                <button
                  type="button"
                  className="dcw-tile dcw-theme-tile"
                  aria-label={name}
                  aria-pressed={active.kind === 'theme' && active.id === theme.id}
                  onClick={() => {
                    setActionError(null)
                    apply(theme)
                  }}
                >
                  <ThemeThumbnail theme={theme} />
                  <span className="dcw-theme-name">{name}</span>
                </button>
                <ThemeTileMenu
                  host={host}
                  theme={theme}
                  name={name}
                  strings={strings}
                  busy={busy}
                  run={run}
                  onRemove={remove}
                />
              </div>
            )
          })}
        </div>
      </div>
    </SettingsGroup>
  )
}
