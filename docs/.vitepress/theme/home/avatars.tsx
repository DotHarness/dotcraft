import { renderToStaticMarkup } from 'react-dom/server'
import { AppearanceAvatar, Avatar } from '../../../../sdk/typescript/packages/avatar/src/Avatar'
import { originalAppearance } from '../../../../sdk/typescript/packages/avatar/src/appearanceModel'

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

export function renderMascot(size: number): string {
  return unique(renderToStaticMarkup(<AppearanceAvatar appearance={originalAppearance} state="idle" size={size} motion="system" />))
}

export function renderAgent(name: string, size: number): string {
  return unique(renderToStaticMarkup(<Avatar name={name} state="idle" size={size} motion="system" />))
}
