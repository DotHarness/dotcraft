import { deriveAppearance, mascotPaletteOf } from '../index.js'
export function ComposerMascotShadow({ name = '' }: { name?: string }) {
  const palette = mascotPaletteOf(deriveAppearance(name))
  return <div aria-hidden className="composer-mascot-contact-shadow" style={{ background: `radial-gradient(50% 100% at 50% 0%, color-mix(in srgb, ${palette.shadow} 10%, transparent) 0%, transparent 72%)` }} />
}
