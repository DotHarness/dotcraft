import { useEffect, useState, type CSSProperties, type JSX } from 'react'
import { Check, Minus, Plus } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { addToast } from '../../../stores/toastStore'
import { useUIStore } from '../../../stores/uiStore'
import { applyTheme, useDocumentThemeMode, type ThemeMode } from '../../../utils/theme'
import {
  applyThemeSeeds,
  applyCodeFontSize,
  applyPointerCursors,
  applyReduceMotion,
  applyTranslucentSidebar
} from '../../../utils/appearance'
import {
  CODE_FONT_SIZE_MAX,
  CODE_FONT_SIZE_MIN,
  DEFAULT_APPEARANCE,
  DEFAULT_CODE_FONT_SIZE,
  UI_FONT_SIZE_MAX,
  UI_FONT_SIZE_MIN,
  interfaceZoomToUiFontPx,
  normalizeAccentHex,
  resolveAppearanceSettings,
  uiFontPxToInterfaceZoom,
  type AppearanceSettings,
  type DiffMarkerMode,
  type ReduceMotionMode
} from '../../../../shared/appearance'
import {
  CONTRAST_MAX,
  CONTRAST_MIN,
  DEFAULT_SEEDS,
  type ThemeSeedOverrides,
  type ThemeVariant
} from '../../../../shared/themeSeed'
import { SettingsPanelShell } from '../SettingsPanelShell'
import { SettingsGroup, SettingsRow } from '../SettingsGroup'
import { SegmentedControl } from '../ui/SegmentedControl'
import { PillSwitch } from '../../ui/PillSwitch'
import { AppearancePreview } from './AppearancePreview'
import { requestColorPickerDialog } from '../../ui/ColorPickerDialog'

/** Distinct alternative accents. The brand default is offered separately as "Default" (no override). */
const ACCENT_PRESETS: string[] = ['#2f81f7', '#3e8c64', '#c9821f', '#8b5cf6', '#e0566f', '#5b6b86']

/** Backgrounds near each variant's default, so a pick reads as a tint rather than a new theme. */
const SURFACE_PRESETS: Record<ThemeVariant, string[]> = {
  dark: ['#000000', '#16191d', '#1a1613', '#101614'],
  light: ['#faf8f4', '#f6f8fb', '#f7f4ec', '#f4f7f4']
}

/** The contrast control moves in steps this size; the range itself is 0-100. */
const CONTRAST_STEP = 5

export function AppearancePanel(): JSX.Element {
  const t = useT()
  const setDiffMarkers = useUIStore((s) => s.setDiffMarkers)
  // The editor writes to the variant currently on screen, so its swatches show what a pick does.
  const variant: ThemeVariant = useDocumentThemeMode() === 'dark' ? 'dark' : 'light'
  const [appearance, setAppearance] = useState<AppearanceSettings>(DEFAULT_APPEARANCE)

  useEffect(() => {
    let cancelled = false
    void window.api.settings
      .get()
      .then((settings) => {
        if (!cancelled) setAppearance(resolveAppearanceSettings(settings))
      })
      .catch(() => {
        // Non-fatal: keep defaults.
      })
    return () => {
      cancelled = true
    }
  }, [])

  async function persist(patch: Record<string, unknown>): Promise<void> {
    try {
      await window.api.settings.set(patch)
    } catch (err) {
      addToast(
        t('settings.saveFailed', { error: err instanceof Error ? err.message : String(err) }),
        'error'
      )
    }
  }

  function handleThemeMode(mode: ThemeMode): void {
    setAppearance((prev) => ({ ...prev, themeMode: mode }))
    applyTheme(mode)
    void persist({ theme: mode })
  }

  function handleAccent(hex: string | null): void {
    const normalized = hex ? normalizeAccentHex(hex) : null
    setAppearance((prev) => ({ ...prev, accent: normalized }))
    applyThemeSeeds(normalized, appearance.themeSeeds)
    // Empty string clears the override on the main side (undefined would be dropped by JSON).
    void persist({ accent: normalized ?? '' })
  }

  /** Background and contrast belong to the variant on screen; the accent is shared by both. */
  function handleSeed(next: ThemeSeedOverrides): void {
    const seeds = { ...appearance.themeSeeds, [variant]: next }
    setAppearance((prev) => ({ ...prev, themeSeeds: seeds }))
    applyThemeSeeds(appearance.accent, seeds)
    void persist({ themeSeeds: seeds })
  }

  function handleSurface(hex: string | null): void {
    const normalized = hex ? normalizeAccentHex(hex) : null
    const { surface: _dropped, ...rest } = seed
    handleSeed(normalized ? { ...rest, surface: normalized } : rest)
  }

  async function chooseAccent(): Promise<void> {
    const request = requestColorPickerDialog({
      title: t('settings.appearance.accent.pickerTitle'),
      description: t('settings.appearance.accent.hint'),
      initialColor: appearance.accent ?? DEFAULT_SEEDS[variant].accent,
      allowReset: true,
      defaultColor: DEFAULT_SEEDS[variant].accent
    })
    const result = await request.result
    if (result.kind === 'select') handleAccent(result.color)
    else if (result.kind === 'reset') handleAccent(null)
  }

  async function chooseSurface(): Promise<void> {
    const request = requestColorPickerDialog({
      title: t('settings.appearance.surface.pickerTitle'),
      description: t('settings.appearance.surface.hint'),
      initialColor: seed.surface ?? DEFAULT_SEEDS[variant].surface,
      allowReset: true,
      defaultColor: DEFAULT_SEEDS[variant].surface
    })
    const result = await request.result
    if (result.kind === 'select') handleSurface(result.color)
    else if (result.kind === 'reset') handleSurface(null)
  }

  function handleContrast(next: number): void {
    const clamped = Math.min(CONTRAST_MAX, Math.max(CONTRAST_MIN, next))
    handleSeed({ ...seed, contrast: clamped })
  }

  function handleCodeFontSize(next: number): void {
    const clamped = Math.min(CODE_FONT_SIZE_MAX, Math.max(CODE_FONT_SIZE_MIN, next))
    setAppearance((prev) => ({ ...prev, codeFontSize: clamped }))
    applyCodeFontSize(clamped)
    void persist({ codeFontSize: clamped })
  }

  function handleDiffMarkers(mode: DiffMarkerMode): void {
    setAppearance((prev) => ({ ...prev, diffMarkers: mode }))
    setDiffMarkers(mode)
    void persist({ diffMarkers: mode })
  }

  function handleReduceMotion(mode: ReduceMotionMode): void {
    setAppearance((prev) => ({ ...prev, reduceMotion: mode }))
    applyReduceMotion(mode)
    void persist({ reduceMotion: mode })
  }

  function handlePointerCursors(on: boolean): void {
    setAppearance((prev) => ({ ...prev, pointerCursors: on }))
    applyPointerCursors(on)
    void persist({ pointerCursors: on })
  }

  // The control reads as a UI font size in px but is applied as a whole-interface zoom anchored on
  // the 14px base, so each px step persists as the `px / 14` zoom factor (see shared/appearance.ts).
  function handleUiFontSize(nextPx: number): void {
    const clampedPx = Math.min(UI_FONT_SIZE_MAX, Math.max(UI_FONT_SIZE_MIN, nextPx))
    const zoom = uiFontPxToInterfaceZoom(clampedPx)
    setAppearance((prev) => ({ ...prev, interfaceZoom: zoom }))
    window.api.window.setZoomFactor(zoom)
    void persist({ interfaceZoom: zoom })
  }

  function handleTranslucentSidebar(on: boolean): void {
    setAppearance((prev) => ({ ...prev, translucentSidebar: on }))
    applyTranslucentSidebar(on)
    void persist({ translucentSidebar: on })
  }

  const accentLower = appearance.accent?.toLowerCase() ?? null
  const isCustomAccent = accentLower !== null && !ACCENT_PRESETS.includes(accentLower)
  const seed = appearance.themeSeeds[variant]
  const surfaceLower = seed.surface?.toLowerCase() ?? null
  const isCustomSurface = surfaceLower !== null && !SURFACE_PRESETS[variant].includes(surfaceLower)
  const contrast = seed.contrast ?? DEFAULT_SEEDS[variant].contrast
  const codeSize = appearance.codeFontSize ?? DEFAULT_CODE_FONT_SIZE
  const uiFontPx = interfaceZoomToUiFontPx(appearance.interfaceZoom)

  return (
    <SettingsPanelShell title={t('settings.tab.appearance')} description={t('settings.appearance.description')}>
      <div style={themeCardsRowStyle}>
        <ThemeModeCard
          kind="system"
          label={t('settings.appearance.mode.system')}
          selected={appearance.themeMode === 'system'}
          onSelect={() => handleThemeMode('system')}
        />
        <ThemeModeCard
          kind="light"
          label={t('settings.appearance.mode.light')}
          selected={appearance.themeMode === 'light'}
          onSelect={() => handleThemeMode('light')}
        />
        <ThemeModeCard
          kind="dark"
          label={t('settings.appearance.mode.dark')}
          selected={appearance.themeMode === 'dark'}
          onSelect={() => handleThemeMode('dark')}
        />
      </div>

      <AppearancePreview accent={appearance.accent ?? '#4566cc'} codeFontSize={codeSize} />

      <SettingsGroup title={t('settings.appearance.group.theme')}>
        <SettingsRow
          label={t('settings.appearance.accent.label')}
          description={t('settings.appearance.accent.hint')}
          control={
            <div style={swatchesRowStyle}>
            <AccentSwatch
              ariaLabel={t('settings.appearance.accent.defaultSwatch')}
              color="var(--accent)"
              selected={appearance.accent === null}
              onSelect={() => handleAccent(null)}
            />
            {ACCENT_PRESETS.map((hex) => (
              <AccentSwatch
                key={hex}
                ariaLabel={t('settings.appearance.accent.swatch', { color: hex })}
                color={hex}
                selected={accentLower === hex}
                onSelect={() => handleAccent(hex)}
              />
            ))}
            <button
              type="button"
              aria-label={t('settings.appearance.accent.custom')}
              title={t('settings.appearance.accent.custom')}
              onClick={() => void chooseAccent()}
              style={customSwatchStyle(isCustomAccent ? appearance.accent : null)}
            >
              {!isCustomAccent && <Plus size={13} strokeWidth={2} aria-hidden />}
              {isCustomAccent && <Check size={13} strokeWidth={3} color="#fff" aria-hidden />}
            </button>
            <span style={hexLabelStyle}>{(appearance.accent ?? '').toUpperCase() || t('settings.appearance.accent.default')}</span>
            </div>
          }
        />
        <SettingsRow
          label={t('settings.appearance.surface.label')}
          description={t('settings.appearance.surface.hint')}
          control={
            <div style={swatchesRowStyle}>
              <AccentSwatch
                ariaLabel={t('settings.appearance.surface.defaultSwatch')}
                color={DEFAULT_SEEDS[variant].surface}
                selected={seed.surface === undefined}
                onSelect={() => handleSurface(null)}
              />
              {SURFACE_PRESETS[variant].map((hex) => (
                <AccentSwatch
                  key={hex}
                  ariaLabel={t('settings.appearance.surface.swatch', { color: hex })}
                  color={hex}
                  selected={surfaceLower === hex}
                  onSelect={() => handleSurface(hex)}
                />
              ))}
              <button
                type="button"
                aria-label={t('settings.appearance.surface.custom')}
                title={t('settings.appearance.surface.custom')}
                onClick={() => void chooseSurface()}
                style={customSwatchStyle(isCustomSurface ? seed.surface ?? null : null)}
              >
                {!isCustomSurface && <Plus size={13} strokeWidth={2} aria-hidden />}
                {isCustomSurface && <Check size={13} strokeWidth={3} color="#fff" aria-hidden />}
              </button>
              <span style={hexLabelStyle}>
                {(seed.surface ?? '').toUpperCase() || t('settings.appearance.surface.default')}
              </span>
            </div>
          }
        />
        <SettingsRow
          label={t('settings.appearance.contrast.label')}
          description={t('settings.appearance.contrast.hint')}
          control={
            <div style={stepperStyle}>
              <button
                type="button"
                aria-label={t('settings.appearance.contrast.decrease')}
                disabled={contrast <= CONTRAST_MIN}
                onClick={() => handleContrast(contrast - CONTRAST_STEP)}
                style={stepperButtonStyle(contrast <= CONTRAST_MIN)}
              >
                <Minus size={15} strokeWidth={2} aria-hidden />
              </button>
              <span style={stepperValueStyle}>{contrast}</span>
              <button
                type="button"
                aria-label={t('settings.appearance.contrast.increase')}
                disabled={contrast >= CONTRAST_MAX}
                onClick={() => handleContrast(contrast + CONTRAST_STEP)}
                style={stepperButtonStyle(contrast >= CONTRAST_MAX)}
              >
                <Plus size={15} strokeWidth={2} aria-hidden />
              </button>
            </div>
          }
        />
      </SettingsGroup>

      <SettingsGroup title={t('settings.appearance.group.interface')}>
        <SettingsRow
          label={t('settings.appearance.interfaceZoom.label')}
          description={t('settings.appearance.interfaceZoom.hint')}
          control={
            <div style={stepperStyle}>
              <button
                type="button"
                aria-label={t('settings.appearance.interfaceZoom.decrease')}
                disabled={uiFontPx <= UI_FONT_SIZE_MIN}
                onClick={() => handleUiFontSize(uiFontPx - 1)}
                style={stepperButtonStyle(uiFontPx <= UI_FONT_SIZE_MIN)}
              >
                <Minus size={15} strokeWidth={2} aria-hidden />
              </button>
              <span style={stepperValueStyle}>{uiFontPx}px</span>
              <button
                type="button"
                aria-label={t('settings.appearance.interfaceZoom.increase')}
                disabled={uiFontPx >= UI_FONT_SIZE_MAX}
                onClick={() => handleUiFontSize(uiFontPx + 1)}
                style={stepperButtonStyle(uiFontPx >= UI_FONT_SIZE_MAX)}
              >
                <Plus size={15} strokeWidth={2} aria-hidden />
              </button>
            </div>
          }
        />
        <SettingsRow
          label={t('settings.appearance.translucentSidebar.label')}
          description={t('settings.appearance.translucentSidebar.hint')}
          control={
            <PillSwitch
              checked={appearance.translucentSidebar}
              aria-label={t('settings.appearance.translucentSidebar.label')}
              onChange={handleTranslucentSidebar}
            />
          }
        />
      </SettingsGroup>

      <SettingsGroup title={t('settings.appearance.group.code')}>
        <SettingsRow
          label={t('settings.appearance.codeFontSize.label')}
          description={t('settings.appearance.codeFontSize.hint')}
          control={
            <div style={stepperStyle}>
            <button
              type="button"
              aria-label={t('settings.appearance.codeFontSize.decrease')}
              disabled={codeSize <= CODE_FONT_SIZE_MIN}
              onClick={() => handleCodeFontSize(codeSize - 1)}
              style={stepperButtonStyle(codeSize <= CODE_FONT_SIZE_MIN)}
            >
              <Minus size={15} strokeWidth={2} aria-hidden />
            </button>
            <span style={stepperValueStyle}>{codeSize}px</span>
            <button
              type="button"
              aria-label={t('settings.appearance.codeFontSize.increase')}
              disabled={codeSize >= CODE_FONT_SIZE_MAX}
              onClick={() => handleCodeFontSize(codeSize + 1)}
              style={stepperButtonStyle(codeSize >= CODE_FONT_SIZE_MAX)}
            >
              <Plus size={15} strokeWidth={2} aria-hidden />
            </button>
            </div>
          }
        />
        <SettingsRow
          label={t('settings.appearance.diffMarkers.label')}
          description={t('settings.appearance.diffMarkers.hint')}
          control={
            <SegmentedControl<DiffMarkerMode>
              ariaLabel={t('settings.appearance.diffMarkers.label')}
              value={appearance.diffMarkers}
              onChange={handleDiffMarkers}
              options={[
                { value: 'color', label: t('settings.appearance.diffMarkers.color') },
                { value: 'sign', label: t('settings.appearance.diffMarkers.sign') }
              ]}
            />
          }
        />
      </SettingsGroup>

      <SettingsGroup title={t('settings.appearance.group.motion')}>
        <SettingsRow
          label={t('settings.appearance.reduceMotion.label')}
          description={t('settings.appearance.reduceMotion.hint')}
          control={
            <SegmentedControl<ReduceMotionMode>
              ariaLabel={t('settings.appearance.reduceMotion.label')}
              value={appearance.reduceMotion}
              onChange={handleReduceMotion}
              options={[
                { value: 'system', label: t('settings.appearance.reduceMotion.system') },
                { value: 'on', label: t('settings.appearance.reduceMotion.on') },
                { value: 'off', label: t('settings.appearance.reduceMotion.off') }
              ]}
            />
          }
        />
        <SettingsRow
          label={t('settings.appearance.pointerCursors.label')}
          description={t('settings.appearance.pointerCursors.hint')}
          control={
            <PillSwitch
              checked={appearance.pointerCursors}
              aria-label={t('settings.appearance.pointerCursors.label')}
              onChange={handlePointerCursors}
            />
          }
        />
      </SettingsGroup>
    </SettingsPanelShell>
  )
}

function ThemeModeCard({
  kind,
  label,
  selected,
  onSelect
}: {
  kind: 'system' | 'light' | 'dark'
  label: string
  selected: boolean
  onSelect: () => void
}): JSX.Element {
  return (
    <button type="button" aria-pressed={selected} onClick={onSelect} style={themeCardStyle}>
      <span style={themeThumbStyle(selected)}>
        {kind === 'dark' ? <ThemeMock tone="dark" /> : <ThemeMock tone="light" />}
        {kind === 'system' && (
          <span style={systemDarkOverlayStyle}>
            <ThemeMock tone="dark" />
          </span>
        )}
      </span>
      <span style={themeCapStyle(selected)}>{label}</span>
    </button>
  )
}

function ThemeMock({ tone }: { tone: 'light' | 'dark' }): JSX.Element {
  const colors =
    tone === 'light'
      ? { bg: '#f9f9f9', side: '#ececec', bar: '#cfcfcf' }
      : { bg: '#141515', side: '#202020', bar: '#3a3a3a' }
  return (
    <span style={{ position: 'absolute', inset: 0, display: 'flex', background: colors.bg }}>
      <span style={{ width: '34%', background: colors.side }} />
      <span style={{ flex: 1, padding: '9px 8px' }}>
        <span style={{ display: 'block', height: 5, borderRadius: 3, marginBottom: 6, background: colors.bar }} />
        <span style={{ display: 'block', height: 5, width: '60%', borderRadius: 3, background: colors.bar }} />
      </span>
    </span>
  )
}

function AccentSwatch({
  ariaLabel,
  color,
  selected,
  onSelect
}: {
  ariaLabel: string
  color: string
  selected: boolean
  onSelect: () => void
}): JSX.Element {
  return (
    <button
      type="button"
      aria-label={ariaLabel}
      aria-pressed={selected}
      title={ariaLabel}
      onClick={onSelect}
      style={swatchStyle(color, selected)}
    />
  )
}

const themeCardsRowStyle: CSSProperties = { display: 'flex', gap: 12, width: '100%' }

const themeCardStyle: CSSProperties = {
  flex: 1,
  display: 'flex',
  flexDirection: 'column',
  gap: 8,
  padding: 0,
  border: 'none',
  background: 'transparent',
  cursor: 'pointer'
}

function themeThumbStyle(selected: boolean): CSSProperties {
  return {
    position: 'relative',
    height: 80,
    borderRadius: 10,
    overflow: 'hidden',
    border: `2px solid ${selected ? 'var(--accent)' : 'var(--border-default)'}`,
    transition: 'border-color 140ms ease'
  }
}

const systemDarkOverlayStyle: CSSProperties = {
  position: 'absolute',
  inset: 0,
  clipPath: 'polygon(100% 0, 100% 100%, 0 100%)'
}

function themeCapStyle(selected: boolean): CSSProperties {
  return {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 5,
    fontSize: 12.5,
    fontWeight: selected ? 600 : 400,
    color: selected ? 'var(--text-primary)' : 'var(--text-secondary)'
  }
}

const swatchesRowStyle: CSSProperties = {
  position: 'relative',
  display: 'flex',
  alignItems: 'center',
  gap: 9,
  flexWrap: 'wrap'
}

function swatchStyle(color: string, selected: boolean): CSSProperties {
  return {
    position: 'relative',
    width: 24,
    height: 24,
    borderRadius: 999,
    padding: 0,
    cursor: 'pointer',
    background: color,
    border: '2px solid transparent',
    boxShadow: selected
      ? '0 0 0 2px var(--bg-secondary), 0 0 0 3.5px var(--accent)'
      : 'inset 0 0 0 1px color-mix(in srgb, #000 14%, transparent)'
  }
}

function customSwatchStyle(customColor: string | null): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: 24,
    height: 24,
    borderRadius: 999,
    padding: 0,
    cursor: 'pointer',
    background: customColor ?? 'var(--bg-tertiary)',
    color: 'var(--text-secondary)',
    border: '2px solid transparent',
    boxShadow: customColor
      ? '0 0 0 2px var(--bg-secondary), 0 0 0 3.5px var(--accent)'
      : 'inset 0 0 0 1px var(--border-default)'
  }
}

const hexLabelStyle: CSSProperties = {
  fontFamily: 'var(--font-mono)',
  fontSize: 11.5,
  color: 'var(--text-secondary)',
  padding: '4px 8px',
  borderRadius: 6,
  background: 'var(--bg-tertiary)',
  border: '1px solid var(--border-default)'
}

const stepperStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  border: '1px solid var(--border-default)',
  borderRadius: 8,
  overflow: 'hidden'
}

function stepperButtonStyle(disabled: boolean): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: 30,
    height: 30,
    border: 'none',
    background: 'var(--bg-tertiary)',
    color: 'var(--text-primary)',
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.5 : 1
  }
}

const stepperValueStyle: CSSProperties = {
  minWidth: 52,
  textAlign: 'center',
  fontSize: 13,
  fontFamily: 'var(--font-mono)',
  color: 'var(--text-primary)'
}
