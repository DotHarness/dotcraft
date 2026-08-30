import { createRoot } from 'react-dom/client'
import { StrictMode, type JSX } from 'react'
import { MascotCharacter, type MascotCharacterProps } from '../src/MascotCharacter'
import { COLORS, SHIPPED_SHAPES } from '../src/characterArt'
import { characterStateFor, type MascotStateInput } from '../src/mascotState'
import { PREVIEW_STATES } from '../src/previewContext'

/**
 * Local design harness. It is not part of the shipped bundle: `dotcraft-plugin build`
 * only reads `src/index.tsx`.
 */
const COLOR_NAMES = Object.keys(COLORS)

function Cell({ label, ...props }: { label: string } & MascotCharacterProps): JSX.Element {
  return (
    <figure className="cell">
      <div className="grok" style={{ width: props.sizePx, height: props.sizePx }}>
        <MascotCharacter className="grok-character" {...props} />
      </div>
      <figcaption>{label}</figcaption>
    </figure>
  )
}

function StateRow({
  title,
  sizePx,
  states = PREVIEW_STATES,
  ...props
}: {
  title: string
  sizePx: number
  states?: readonly MascotStateInput[]
} & Partial<MascotCharacterProps>): JSX.Element {
  return (
    <section>
      <h2>{title}</h2>
      <div className="row">
        {states.map((state) => (
          <Cell
            {...props}
            key={state.activity}
            label={`${state.activity} → ${characterStateFor(state)}`}
            sizePx={sizePx}
            state={characterStateFor(state)}
          />
        ))}
      </div>
    </section>
  )
}

function App(): JSX.Element {
  const hero = PREVIEW_STATES.filter((state) =>
    ['idle', 'working', 'success'].includes(state.activity)
  )
  return (
    <>
      <StateRow title="Hero (200 px)" sizePx={200} states={hero} color="violet" shape="blob" followPointer />
      <StateRow title="Composer size (44 px) — automatic" sizePx={44} />
      <StateRow title="Detail (110 px) — automatic" sizePx={110} followPointer />
      <StateRow
        title="Reduced motion (110 px)"
        sizePx={110}
        states={hero}
        color="gray"
        shape="squircle"
        reducedMotion
      />
      <section>
        <h2>Shapes (96 px) — cyan, idle</h2>
        <div className="row">
          {SHIPPED_SHAPES.map((shape) => (
            <Cell key={shape} label={shape} sizePx={96} state="idle" color="cyan" shape={shape} />
          ))}
        </div>
      </section>
      <section>
        <h2>Colors (64 px) — blob, happy</h2>
        <div className="row">
          {COLOR_NAMES.map((color) => (
            <Cell key={color} label={color} sizePx={64} state="happy" color={color} shape="blob" />
          ))}
        </div>
      </section>
      <section>
        <h2>Automatic per workspace (64 px)</h2>
        <div className="row">
          {['~/acme-api', '~/acme-web', '~/acme-docs', '~/notes', '~/sandbox'].map((sourceId) => (
            <Cell key={sourceId} label={sourceId} sizePx={64} state="idle" sourceId={sourceId} />
          ))}
        </div>
      </section>
    </>
  )
}

const root = document.getElementById('root')
if (root !== null) createRoot(root).render(<StrictMode><App /></StrictMode>)
