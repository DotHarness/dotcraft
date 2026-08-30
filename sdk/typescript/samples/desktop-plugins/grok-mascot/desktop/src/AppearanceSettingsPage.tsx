import { PillSwitch, SegmentedControl, SettingsGroup, SettingsPanelShell, SettingsRow, Skeleton } from '@dotcraft/plugin'
import type { DesktopPluginViewProps } from '@dotcraft/plugin'
import { useState, type JSX } from 'react'
import { ComposerMascot } from './ComposerMascot'
import { MascotStatusRing } from './MascotStatusRing'
import {
  GROK_COLORS,
  GROK_SHAPES,
  setAppearance,
  type GrokColorChoice,
  type GrokShapeChoice
} from './appearance'
import { COLORS, VIEWBOX, personaShapePath, resolvePersonaColor, resolvePersonaShape } from './characterArt'
import { stringsFor } from './i18n'
import { PREVIEW_STATES, previewContext } from './previewContext'
import { useAppearance } from './useAppearance'

const PREVIEW_SIZE = 116
const PREVIEW_SOURCE = 'dotcraft'

export function AppearanceSettingsPage({ host }: DesktopPluginViewProps): JSX.Element | null {
  const appearance = useAppearance()
  const strings = stringsFor(host.environment.locale)
  const [previewIndex, setPreviewIndex] = useState(0)
  const state = PREVIEW_STATES[previewIndex] ?? PREVIEW_STATES[0]
  const context = previewContext(state, PREVIEW_SIZE)

  if (!appearance) {
    return (
      <SettingsPanelShell title={strings.settingsTitle} description={strings.settingsDescription}>
        <SettingsGroup title={strings.previewLabel}>
          <div role="status" aria-busy="true" aria-label={strings.settingsTitle}>
            <SettingsRow><Skeleton width="100%" height={116} radius={8} /></SettingsRow>
          </div>
        </SettingsGroup>
      </SettingsPanelShell>
    )
  }

  const colorChoices: readonly GrokColorChoice[] = ['auto', ...GROK_COLORS]
  const shapeChoices: readonly GrokShapeChoice[] = ['auto', ...GROK_SHAPES]

  return (
    <SettingsPanelShell title={strings.settingsTitle} description={strings.settingsDescription}>
      <SettingsGroup title={strings.previewLabel} flush>
        <div className="grok-preview">
          <div className="grok-preview-stage">
            <ComposerMascot host={host} context={context} />
            <MascotStatusRing host={host} context={context} />
          </div>
          <div className="grok-preview-states">
            <SegmentedControl
              value={String(previewIndex)}
              ariaLabel={strings.previewLabel}
              options={PREVIEW_STATES.map((preview, index) => ({
                value: String(index),
                label: strings.previewStates[preview.activity]
              }))}
              onValueChange={(value) => setPreviewIndex(Number(value))}
            />
          </div>
        </div>
      </SettingsGroup>

      <SettingsGroup title={strings.characterGroup}>
        <SettingsRow
          orientation="block"
          label={strings.colorLabel}
          description={strings.colorDescription}
          control={
            <div className="grok-tiles grok-tiles-color" role="group" aria-label={strings.colorLabel}>
              {colorChoices.map((choice) => {
                const name = choice === 'auto' ? resolvePersonaColor(PREVIEW_SOURCE, null) : choice
                const swatch = COLORS[name] ?? COLORS.black
                return (
                  <button
                    key={choice}
                    type="button"
                    className="grok-tile"
                    data-auto={choice === 'auto' ? 'true' : undefined}
                    aria-pressed={appearance.color === choice}
                    aria-label={choice === 'auto' ? strings.automatic : strings.colors[choice]}
                    title={choice === 'auto' ? strings.automatic : strings.colors[choice]}
                    onClick={() => setAppearance({ color: choice })}
                  >
                    <span
                      className="grok-tile-swatch"
                      style={{ background: `linear-gradient(135deg, ${swatch.light}, ${swatch.dark})` }}
                    />
                  </button>
                )
              })}
            </div>
          }
        />
        <SettingsRow
          orientation="block"
          label={strings.shapeLabel}
          description={strings.shapeDescription}
          control={
            <div className="grok-tiles grok-tiles-shape" role="group" aria-label={strings.shapeLabel}>
              {shapeChoices.map((choice) => {
                const name = choice === 'auto' ? resolvePersonaShape(PREVIEW_SOURCE, null) : choice
                return (
                  <button
                    key={choice}
                    type="button"
                    className="grok-tile"
                    data-auto={choice === 'auto' ? 'true' : undefined}
                    aria-pressed={appearance.shape === choice}
                    aria-label={choice === 'auto' ? strings.automatic : strings.shapes[choice]}
                    title={choice === 'auto' ? strings.automatic : strings.shapes[choice]}
                    onClick={() => setAppearance({ shape: choice })}
                  >
                    <svg viewBox={VIEWBOX} aria-hidden="true">
                      <path d={personaShapePath(name)} fill="currentColor" />
                    </svg>
                  </button>
                )
              })}
            </div>
          }
        />
      </SettingsGroup>

      <SettingsGroup title={strings.effectsGroup}>
        <SettingsRow
          label={strings.statusRingLabel}
          description={strings.statusRingDescription}
          control={
            <PillSwitch
              checked={appearance.statusRing}
              aria-label={strings.statusRingLabel}
              onChange={(statusRing) => setAppearance({ statusRing })}
            />
          }
        />
      </SettingsGroup>
    </SettingsPanelShell>
  )
}
