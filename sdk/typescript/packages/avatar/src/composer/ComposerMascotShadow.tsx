import { deriveAppearance, mascotPaletteOf, type Appearance } from '../index.js'
export function ComposerMascotShadow({ name = '', appearanceOf = deriveAppearance }: { name?: string; appearanceOf?: (name: string) => Appearance }) {
  const palette = mascotPaletteOf(appearanceOf(name))
  return <div aria-hidden className="composer-mascot-contact-shadow" style={{ background: `radial-gradient(50% 100% at 50% 0%, color-mix(in srgb, ${palette.shadow} 10%, transparent) 0%, transparent 72%)` }} />
}
