import { PillSwitch } from '@dotcraft/plugin'
import type { DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import { useEffect, useRef, useState, type JSX } from 'react'
import type { Theme, ThemeChoice, ThemeScope } from '../../../shared/themes'
import { choiceAt, scopeOf, setChoice, useChoices } from './choices'
import { closeComposerPicker, useComposerPickerOpen } from './composerPicker'
import { useAppearance, useSettings } from './hooks'
import { stringsFor, themeDisplayName } from './i18n'
import { applyTheme, cancelThemePreview, useThemeLibrary } from './themeLibrary'
import { ThemeThumbnail } from './ThemePicker'

export function ComposerThemePicker({ host }: DesktopPluginSurfaceProps<'composer.before'>): JSX.Element | null {
  const open = useComposerPickerOpen()
  const settings = useSettings()
  const appearance = useAppearance(host)
  const library = useThemeLibrary()
  const { context } = useChoices()
  const chat = scopeOf('chat')
  const [onlyThisChat, setOnlyThisChat] = useState(false)
  const root = useRef<HTMLDivElement>(null)
  const strings = stringsFor(host.environment.locale)

  useEffect(() => {
    if (open) setOnlyThisChat(chat !== null && choiceAt(chat, appearance) !== undefined)
  }, [open, context.threadId])
  useEffect(() => {
    if (!open) return
    const onKey = (event: KeyboardEvent): void => { if (event.key === 'Escape') closeComposerPicker() }
    const onPointer = (event: PointerEvent): void => {
      if (!root.current?.contains(event.target as Node)) closeComposerPicker()
    }
    window.addEventListener('keydown', onKey)
    window.addEventListener('pointerdown', onPointer, true)
    return () => {
      window.removeEventListener('keydown', onKey)
      window.removeEventListener('pointerdown', onPointer, true)
      cancelThemePreview()
    }
  }, [open])

  if (!open || !settings?.enabled) return null
  const scope: ThemeScope = onlyThisChat && chat ? chat : { kind: 'everywhere' }
  const active = choiceAt(scope, appearance) ?? (scope.kind === 'everywhere' ? { kind: 'none' as const } : undefined)
  const selectedId = library.preview?.id ?? (active?.kind === 'theme' ? active.id : null)
  const choose = (choice: ThemeChoice | null): void => {
    cancelThemePreview()
    setChoice(scope, { [appearance]: choice })
    closeComposerPicker()
  }
  const apply = (theme: Theme): void => {
    cancelThemePreview()
    applyTheme(theme, scope)
    closeComposerPicker()
  }

  return (
    <div ref={root} className="dcw-composer-picker" role="dialog" aria-label={strings.pickCommand}>
      <div className="dcw-composer-picker-tiles">
        {scope.kind === 'chat' && (
          <button type="button" className="dcw-tile dcw-tile-none" aria-pressed={!library.preview && active === undefined} onClick={() => choose(null)}>
            {strings.followTile}
          </button>
        )}
        <button type="button" className="dcw-tile dcw-tile-none" aria-pressed={!library.preview && active?.kind === 'none'} onClick={() => choose({ kind: 'none' })}>
          {strings.noneTile}
        </button>
        {library.themes.filter((theme) => theme.colorScheme === appearance).map((theme) => {
          const name = themeDisplayName(theme, host.environment.locale)
          return (
            <button
              key={theme.id}
              type="button"
              className="dcw-tile dcw-theme-tile"
              aria-label={name}
              aria-pressed={selectedId === theme.id}
              onClick={() => apply(theme)}
            >
              <ThemeThumbnail theme={theme} />
              <span className="dcw-theme-name">{name}</span>
            </button>
          )
        })}
      </div>
      {chat && (
        <label className="dcw-composer-picker-switch">
          <PillSwitch checked={onlyThisChat} aria-label={strings.onlyThisChat} onChange={setOnlyThisChat} />
          <span>{strings.onlyThisChat}</span>
        </label>
      )}
    </div>
  )
}
