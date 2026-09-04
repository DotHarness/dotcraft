import {
  Button,
  IconButton,
  PillSwitch,
  SegmentedControl,
  Select,
  SettingsGroup,
  SettingsPanelShell,
  SettingsRow,
  Skeleton,
  Slider
} from '@dotcraft/plugin'
import type { DesktopPluginViewProps } from '@dotcraft/plugin'
import { useEffect, useRef, useState, type ChangeEvent, type JSX } from 'react'
import { useImagesRevision, useResolvedTheme, useSettings, useStoredImages } from './hooks'
import { deleteImage, putImage, urlForImage } from './imageStore'
import { stringsFor } from './i18n'
import { PRESETS } from './presets'
import {
  choiceFor,
  previewSettings,
  setSettings,
  type WallpaperChoice,
  type WallpaperFit,
  type WallpaperSettings
} from './settings'

const MAX_IMAGE_BYTES = 16 * 1024 * 1024

function RemoveIcon(): JSX.Element {
  return (
    <svg width="13" height="13" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true">
      <path d="M3 4h10M6 2.5h4M5 4l.5 9h5L11 4" />
    </svg>
  )
}

function sameChoice(a: WallpaperChoice, b: WallpaperChoice): boolean {
  if (a.kind !== b.kind) return false
  return a.kind === 'none' || b.kind === 'none' ? true : a.id === b.id
}

export function WallpaperSettingsPage({ host }: DesktopPluginViewProps): JSX.Element | null {
  const settings = useSettings()
  const strings = stringsFor(host.environment.locale)
  const resolvedTheme = useResolvedTheme(host)
  const [tab, setTab] = useState<'light' | 'dark'>(resolvedTheme)
  const revision = useImagesRevision()
  const images = useStoredImages(revision)
  const fileInput = useRef<HTMLInputElement>(null)
  const previewFrame = useRef<number | null>(null)
  const pendingPreview = useRef<Partial<WallpaperSettings>>({})

  useEffect(() => () => {
    if (previewFrame.current !== null) cancelAnimationFrame(previewFrame.current)
  }, [])

  const preview = (patch: Partial<WallpaperSettings>): void => {
    pendingPreview.current = { ...pendingPreview.current, ...patch }
    if (previewFrame.current !== null) return
    previewFrame.current = requestAnimationFrame(() => {
      previewFrame.current = null
      const next = pendingPreview.current
      pendingPreview.current = {}
      previewSettings(next)
    })
  }

  const commit = (patch: Partial<WallpaperSettings>): void => {
    if (previewFrame.current !== null) {
      cancelAnimationFrame(previewFrame.current)
      previewFrame.current = null
    }
    pendingPreview.current = {}
    previewSettings(patch)
    setSettings(patch)
  }

  if (!settings) {
    return (
      <SettingsPanelShell title={strings.settingsTitle} description={strings.settingsDescription}>
        <SettingsGroup>
          <div role="status" aria-busy="true" aria-label={strings.settingsTitle}>
            <SettingsRow><Skeleton width="100%" height={18} /></SettingsRow>
            <SettingsRow><Skeleton width="78%" height={18} /></SettingsRow>
          </div>
        </SettingsGroup>
      </SettingsPanelShell>
    )
  }
  const active = choiceFor(settings, tab)

  const choose = (choice: WallpaperChoice): void => {
    setSettings(tab === 'dark' ? { dark: choice } : { light: choice })
  }

  const remove = async (id: string): Promise<void> => {
    const confirmed = await host.ui.confirm({
      title: strings.removeTitle,
      message: strings.removeMessage,
      confirmLabel: strings.removeImage,
      danger: true
    })
    if (!confirmed) return
    if (sameChoice(active, { kind: 'image', id })) choose({ kind: 'none' })
    await deleteImage(id)
  }

  const onFile = (event: ChangeEvent<HTMLInputElement>): void => {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (file === undefined) return
    if (file.size > MAX_IMAGE_BYTES) {
      host.ui.showToast({ message: strings.imageTooLarge, tone: 'warning' })
      return
    }
    void putImage(file).then((stored) => choose({ kind: 'image', id: stored.id }))
  }

  return (
    <SettingsPanelShell title={strings.settingsTitle} description={strings.settingsDescription}>
      <SettingsGroup>
        <SettingsRow
          label={strings.enableLabel}
          description={strings.enableDescription}
          control={
            <PillSwitch
              checked={settings.enabled}
              aria-label={strings.enableLabel}
              onChange={(enabled) => setSettings({ enabled })}
            />
          }
        />
      </SettingsGroup>

      <SettingsGroup
        flush
        title={strings.sceneGroup}
        headerAction={
          <span className="dcw-upload">
            <Button size="sm" variant="secondary" onClick={() => fileInput.current?.click()}>
              {strings.addImage}
            </Button>
            <input ref={fileInput} type="file" accept="image/*" onChange={onFile} />
          </span>
        }
      >
        <div className="dcw-scene">
          <SegmentedControl
            value={tab}
            ariaLabel={strings.sceneGroup}
            options={[
              { value: 'light', label: strings.lightTab },
              { value: 'dark', label: strings.darkTab }
            ]}
            onValueChange={setTab}
          />
          <div className="dcw-picker">
          <button
            type="button"
            className="dcw-tile dcw-tile-none"
            aria-pressed={active.kind === 'none'}
            onClick={() => choose({ kind: 'none' })}
          >
            {strings.noneTile}
          </button>
          {PRESETS.map((preset) => (
            <button
              key={preset.id}
              type="button"
              className="dcw-tile"
              style={{ backgroundImage: `url("${preset.url}")` }}
              aria-label={preset.id}
              aria-pressed={sameChoice(active, { kind: 'preset', id: preset.id })}
              onClick={() => choose({ kind: 'preset', id: preset.id })}
            />
          ))}
          {images.map((image) => (
            <div key={image.id} className="dcw-tile-slot">
              <button
                type="button"
                className="dcw-tile"
                style={{ backgroundImage: `url("${urlForImage(image)}")` }}
                aria-label={image.name}
                aria-pressed={sameChoice(active, { kind: 'image', id: image.id })}
                onClick={() => choose({ kind: 'image', id: image.id })}
              />
              <IconButton
                className="dcw-tile-remove"
                icon={<RemoveIcon />}
                label={strings.removeImage}
                size={22}
                onClick={() => void remove(image.id)}
              />
            </div>
          ))}
          </div>
        </div>
      </SettingsGroup>

      <SettingsGroup title={strings.adjustGroup}>
        <SettingsRow
          label={strings.fitLabel}
          control={
            <Select<WallpaperFit>
              value={settings.fit}
              ariaLabel={strings.fitLabel}
              options={[
                { value: 'cover', label: strings.fitCover },
                { value: 'contain', label: strings.fitContain },
                { value: 'tile', label: strings.fitTile }
              ]}
              onValueChange={(fit) => {
                setSettings({ fit })
              }}
            />
          }
        />
        <SettingsRow
          label={strings.blurLabel}
          control={
            <Slider
              min={0}
              max={24}
              value={settings.blur}
              ariaLabel={strings.blurLabel}
              valueText={`${settings.blur}px`}
              onValueChange={(blur) => preview({ blur })}
              onValueCommit={(blur) => commit({ blur })}
            />
          }
        />
        <SettingsRow
          label={strings.dimLabel}
          control={
            <Slider
              min={0}
              max={80}
              value={settings.dim}
              ariaLabel={strings.dimLabel}
              valueText={`${settings.dim}%`}
              onValueChange={(dim) => preview({ dim })}
              onValueCommit={(dim) => commit({ dim })}
            />
          }
        />
        <SettingsRow
          label={strings.surfaceLabel}
          description={strings.surfaceDescription}
          control={
            <Slider
              min={30}
              max={100}
              value={settings.surfaceOpacity}
              ariaLabel={strings.surfaceLabel}
              valueText={`${settings.surfaceOpacity}%`}
              onValueChange={(surfaceOpacity) => preview({ surfaceOpacity })}
              onValueCommit={(surfaceOpacity) => commit({ surfaceOpacity })}
            />
          }
        />
      </SettingsGroup>
    </SettingsPanelShell>
  )
}
