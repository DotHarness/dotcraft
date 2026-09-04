import aurora from './assets/aurora.svg'
import blueprint from './assets/blueprint.svg'
import sakura from './assets/sakura.svg'
import terminal from './assets/terminal.svg'

export interface WallpaperPreset {
  readonly id: string
  readonly url: string
  readonly tone: 'dark' | 'light'
}

export const PRESETS: readonly WallpaperPreset[] = [
  { id: 'aurora', url: aurora, tone: 'dark' },
  { id: 'blueprint', url: blueprint, tone: 'dark' },
  { id: 'terminal', url: terminal, tone: 'dark' },
  { id: 'sakura', url: sakura, tone: 'light' }
]

export function presetById(id: string): WallpaperPreset | undefined {
  return PRESETS.find((preset) => preset.id === id)
}
