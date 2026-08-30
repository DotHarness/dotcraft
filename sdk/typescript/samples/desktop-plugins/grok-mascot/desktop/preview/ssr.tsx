import { renderToStaticMarkup } from 'react-dom/server'
import { writeFileSync } from 'node:fs'
import type { JSX } from 'react'
import { MascotCharacter, type MascotCharacterProps } from '../src/MascotCharacter'
import { COLORS, SHIPPED_SHAPES } from '../src/characterArt'
import { characterStateFor } from '../src/mascotState'
import { PREVIEW_STATES } from '../src/previewContext'

/** Renders the same matrix as `preview.tsx` to static markup for the shareable review page. */
function Stage(props: MascotCharacterProps): JSX.Element {
  return (
    <div className="grok" style={{ width: props.sizePx, height: props.sizePx }}>
      <MascotCharacter className="grok-character" {...props} />
    </div>
  )
}

function cell(element: JSX.Element, label: string): string {
  return `<figure class="cell">${renderToStaticMarkup(element)}<figcaption>${label}</figcaption></figure>`
}

const states = PREVIEW_STATES.map((state) => ({ ...state, character: characterStateFor(state) }))

const hero = states
  .filter((state) => ['idle', 'working', 'success'].includes(state.activity))
  .map((state) =>
    cell(<Stage sizePx={200} state={state.character} color="violet" shape="blob" />, state.activity)
  )
  .join('')

const composer = states
  .map((state) => cell(<Stage sizePx={44} state={state.character} />, state.activity))
  .join('')

const detail = states
  .map((state) => cell(<Stage sizePx={110} state={state.character} />, `${state.activity} → ${state.character}`))
  .join('')

const shapes = SHIPPED_SHAPES.map((shape) =>
  cell(<Stage sizePx={96} state="idle" color="cyan" shape={shape} />, shape)
).join('')

const colors = Object.keys(COLORS)
  .map((color) => cell(<Stage sizePx={64} state="happy" color={color} shape="blob" />, color))
  .join('')

writeFileSync(
  process.argv[2] ?? 'grok-fragments.json',
  JSON.stringify({ hero, composer, detail, shapes, colors }, null, 2)
)
