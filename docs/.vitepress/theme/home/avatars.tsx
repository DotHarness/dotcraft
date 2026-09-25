import { renderToStaticMarkup } from 'react-dom/server'
import { AppearanceAvatar, Avatar } from '../../../../sdk/typescript/packages/avatar/src/Avatar'
import type { AvatarPose } from '../../../../sdk/typescript/packages/avatar/src/characters'
import { hasConflicts, originalAppearance, type Appearance } from '../../../../sdk/typescript/packages/avatar/src/appearanceModel'

export type ProductLook = 'desktop' | 'harness' | 'satellite' | 'avatar'

const productLooks: Record<ProductLook, Partial<Appearance>> = {
  desktop: {},
  harness: { head: 'hard-hat', hand: 'wrench' },
  satellite: { head: 'satellite-dish', back: 'jetpack' },
  avatar: { back: 'twin-blades', skin: 'holographic', head: 'lightning' }
}

let serial = 0

function unique(markup: string): string {
  serial++
  const prefix = `dch${serial}-`
  const ids = new Set([...markup.matchAll(/\sid="([^"]+)"/g)].map((match) => match[1]))
  let out = markup
  for (const id of ids) {
    out = out
      .replaceAll(`"${id}"`, `"${prefix}${id}"`)
      .replaceAll(`#${id})`, `#${prefix}${id})`)
      .replaceAll(`#${id}"`, `#${prefix}${id}"`)
  }
  return out
}

export function renderMascot(pose: AvatarPose, size: number): string {
  return unique(renderToStaticMarkup(<AppearanceAvatar appearance={originalAppearance} state={pose} size={size} motion="system" />))
}

export function renderAgent(name: string, size: number): string {
  return unique(renderToStaticMarkup(<Avatar name={name} state="idle" size={size} motion="system" />))
}

export function renderLook(look: ProductLook, size: number): string {
  const appearance = { ...originalAppearance, ...productLooks[look] } as Appearance
  if (hasConflicts(appearance)) throw new Error(`Conflicting product look: ${look}`)
  return unique(renderToStaticMarkup(<AppearanceAvatar appearance={appearance} state="idle" size={size} motion="off" />))
}
