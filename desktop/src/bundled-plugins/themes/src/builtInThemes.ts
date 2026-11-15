import type { Theme, ThemeAppearance } from '../../../shared/themes'
import { blueprintSvg } from './scenes/blueprint'
import { brailleStarsSvg } from './scenes/braille-stars'
import { auroraSvg, deepWaterSvg, emberSvg } from './scenes/classic'
import { departureBoardSvg } from './scenes/departure-board'
import { ditherDunesNightSvg, ditherDunesSvg } from './scenes/dither-dunes'
import { halftoneDawnSvg, halftoneDuskSvg } from './scenes/halftone-dawn'
import { honuLagoonSvg } from './scenes/honu-lagoon'
import { koiMosaicSvg } from './scenes/koi-mosaic'
import { nightReefSvg } from './scenes/night-reef'
import { paperGardenKraftSvg, paperGardenSvg } from './scenes/paper-garden'
import { plotterTideNightSvg, plotterTideSvg } from './scenes/plotter-tide'
import { snowPinesSvg } from './scenes/snow-pines'
import { teletextCloudsSvg } from './scenes/teletext-clouds'
import { vectorRangeSvg } from './scenes/vector-range'

type Entry = readonly [id: string, name: string, colorScheme: ThemeAppearance, tint: string, scene: () => string, pair?: string]

const entries: readonly Entry[] = [
  ['honu-lagoon', 'Honu Lagoon', 'light', '#e6fbff', honuLagoonSvg],
  ['koi-mosaic', 'Koi Mosaic', 'light', '#eef8f1', koiMosaicSvg],
  ['halftone-dawn', 'Halftone Dawn', 'light', '#f6ead9', halftoneDawnSvg, 'halftone-dusk'],
  ['halftone-dusk', 'Halftone Dusk', 'dark', '#1f2336', halftoneDuskSvg, 'halftone-dawn'],
  ['plotter-tide', 'Plotter Tide', 'light', '#f4efe4', plotterTideSvg, 'plotter-tide-night'],
  ['plotter-tide-night', 'Plotter Tide, Night', 'dark', '#141a27', plotterTideNightSvg, 'plotter-tide'],
  ['dither-dunes', 'Dither Dunes', 'light', '#f1e8d6', ditherDunesSvg, 'dither-dunes-night'],
  ['dither-dunes-night', 'Dither Dunes Night', 'dark', '#1c1712', ditherDunesNightSvg, 'dither-dunes'],
  ['paper-garden', 'Paper Garden', 'light', '#fbf6ec', paperGardenSvg, 'paper-garden-kraft'],
  ['paper-garden-kraft', 'Paper Garden, Kraft', 'dark', '#35312a', paperGardenKraftSvg, 'paper-garden'],
  ['night-reef', 'Night Reef', 'dark', '#0a1430', nightReefSvg],
  ['snow-pines', 'Snow Pines', 'dark', '#14143a', snowPinesSvg],
  ['braille-stars', 'Braille Stars', 'dark', '#0a1430', brailleStarsSvg],
  ['teletext-clouds', 'Block Clouds', 'dark', '#0d173f', teletextCloudsSvg],
  ['vector-range', 'Vector Range', 'dark', '#0e2f27', vectorRangeSvg],
  ['departure-board', 'Departure Board', 'dark', '#1a120b', departureBoardSvg],
  ['blueprint', 'Blueprint', 'dark', '#0c2444', blueprintSvg],
  ['deep-water', 'Deep Water', 'dark', '#062c3a', deepWaterSvg],
  ['aurora', 'Aurora', 'dark', '#14123a', auroraSvg],
  ['ember', 'Ember', 'dark', '#2a0d12', emberSvg]
]

const still = (svg: string): string => svg
  .replace(/@(?:-\w+-)?keyframes\s+[\w-]+\s*\{(?:[^{}]*\{[^{}]*\})*[^{}]*\}/g, '')
  .replace(/\b(?:-\w+-)?(?:animation|transition)(?:-[\w-]+)?\s*:[^;}"]*;?/g, '')
  .replace(/<(animate\w*|set)\b[^>]*?(?:\/>|>[\s\S]*?<\/\1>)/g, '')

export const BUILTIN_THEMES: readonly Theme[] = entries.map(([id, name, colorScheme, tint, scene, pair]): Theme => {
  const source = scene()
  return {
    id: `builtin-${id}`,
    name,
    colorScheme,
    backdrop: { kind: 'scene', format: 'svg', source, poster: still(source) },
    tint,
    ...(pair ? { pair: `builtin-${pair}` } : {}),
    revision: 1,
    updatedAt: '2026-11-15T00:00:00.000Z',
    builtin: true
  }
})
