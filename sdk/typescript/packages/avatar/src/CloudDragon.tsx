import { useId, type CSSProperties, type ReactNode } from 'react'
import { Plane } from './DecorationShapes.js'
import { along, lock, mirror, oval, ridge, talon, tube, type Pt } from './spine.js'

const jade = '#2f9e7a', deep = '#1f7a5e', mint = '#a7e8cf', gold = '#f6b500', eyeLight = '#eafff6', spirit = '#6dffb8'
const both = (d: string) => `${d}${mirror(d)}`
const pivot = (x: number, y: number, delay = 0): CSSProperties => ({ transformOrigin: `${x}px ${y}px`, animationDelay: `${delay}s` })

// A gradient, not a blur filter: the halo moves with the head, and a filter would re-render every frame.
function Halo({ cx, cy, rx, ry }: { cx: number; cy: number; rx: number; ry: number }) {
  const id = `dca-glow-${useId().replace(/:/g, '')}`
  return <>
    <defs><radialGradient id={id}><stop offset="0" stopColor={spirit} stopOpacity=".9" /><stop offset=".55" stopColor={spirit} stopOpacity=".4" /><stop offset="1" stopColor={spirit} stopOpacity="0" /></radialGradient></defs>
    <ellipse className="dca-fx dca-fx-eye-flare" cx={cx} cy={cy} rx={rx} ry={ry} fill={`url(#${id})`} />
  </>
}

const taper = 'M12-86C-22-72-56-64-80-56L-98-44C-92-22-80-2-70 12C-60 30-50 48-46 66C-44 84-40 100-26 110C-16 116-6 118 12 118Z'
const antler = 'M-30-70C-40-110-58-140-86-170M-52-112C-58-132-54-150-44-162M-70-144C-88-150-104-150-120-144'
const frills = [lock(-86, -34, 206, 84, 36, .7), lock(-74, 8, 166, 92, 38, .5)]
const eye = 'M-20-14L-86-40C-78-18-56-2-26 0Z'
const brow = 'M-16-24L-92-52L-90-40L-20-14Z'
const whiskerSpine: Pt[] = [[-40, 86], [-78, 98], [-110, 84], [-122, 54], [-110, 30]]
const whiskerWidth = (t: number) => 12 - 8 * t

function Whisker({ side }: { side: 1 | -1 }) {
  const spine = side === 1 ? whiskerSpine : whiskerSpine.map(([x, y]) => [-x, y] as Pt)
  const joint = along(spine, .45)
  const root = tube(spine, whiskerWidth, 0, .5, 0, 1, 10), tip = tube(spine, whiskerWidth, .42, 1, 0, 1, 10)
  const delay = side === 1 ? 0 : -1.4
  const pass = (outline: boolean) => {
    const paint = (d: string) => outline ? <path d={d} stroke="#fff" strokeWidth="10" strokeLinejoin="round" /> : <path d={d} fill={mint} />
    return <g className="dca-fx-whisker" style={pivot(spine[0][0], spine[0][1], delay)}>
      {paint(root)}
      <g className="dca-fx-whisker-tip" style={pivot(joint.x, joint.y, delay - .5)}>{paint(tip)}</g>
    </g>
  }
  return <>{pass(true)}{pass(false)}</>
}

function Head() {
  return <g transform="translate(210 290) rotate(4)"><g className="dca-fx-rear">
    <g className="dca-fx-frill-flare" style={pivot(0, -20)}>
      <g className="dca-fx-frill-l" style={pivot(-80, -20)}><Plane d={frills} fill={mint} /></g>
      <g className="dca-fx-frill-r" style={pivot(80, -20)}><Plane d={frills.map(mirror)} fill={mint} /></g>
    </g>
    <g strokeLinecap="round" strokeLinejoin="round"><path d={both(antler)} stroke="#fff" strokeWidth="42" /><path d={both(antler)} stroke={gold} strokeWidth="22" /></g>
    <Plane d={[taper, mirror(taper)]} fill={jade} />
    <path d={both(brow)} fill={deep} />
    <Halo cx={-50} cy={-22} rx={58} ry={34} /><Halo cx={50} cy={-22} rx={58} ry={34} />
    <path d={both(eye)} fill={eyeLight} />
    <Whisker side={1} /><Whisker side={-1} />
  </g></g>
}

function Cloud({ x, y, s, flip = false, delay = 0 }: { x: number; y: number; s: number; flip?: boolean; delay?: number }) {
  return <g transform={`translate(${x} ${y}) scale(${flip ? -s : s} ${s})`}><g className="dca-fx-drift" style={{ animationDelay: `${delay}s` }}>
    <path d="M-100 24C-118 24-122-2-104-10C-108-38-78-50-60-34C-54-64-10-70 6-42C16-62 50-60 58-34C82-44 106-24 96 0C114 4 112 26 94 26Z" fill="#dff4ec" stroke="#fff" strokeWidth="16" strokeLinejoin="round" paintOrder="stroke fill" />
    <path d="M-100 22C-60 32 60 32 94 22C80 10-80 10-100 22Z" fill="#b9e6d4" />
    <path d="M-66-6C-66-22-44-24-42-10M-8-28C-8-46 20-48 22-30M36-10C38-24 58-24 60-12" stroke="#5fbf9b" strokeWidth="9" strokeLinecap="round" />
  </g></g>
}

// Past `still`, each joint nests in the one before and swings on its own delay, so the tail whips.
const spine: Pt[] = [[240, 300], [300, 214], [392, 150], [512, 118], [632, 126], [742, 178], [828, 268], [888, 386], [918, 524], [922, 664], [900, 784], [934, 870], [996, 874]]
const width = (t: number) => 96 - 58 * t
const still = .6, joints = [still, .69, .78, .86, .93, 1], overlap = .012
const foreleg = [tube([[318, 206], [352, 262], [396, 330], [404, 356]], t => 48 - 14 * t), oval(404, 352, 34, 26)]
const tailTip = [lock(996, 874, -94, 84, 40, -.5), lock(996, 874, -60, 108, 48, .3), lock(996, 874, -24, 80, 36, .6)]

interface Piece { body: string; fins: string; belly: string }
function piece(from: number, to: number, finFrom = from, finTo = to): Piece {
  const steps = Math.max(6, Math.round((to - from) * 120))
  return {
    body: tube(spine, width, from, to, 0, 1, steps),
    fins: ridge(spine, width, finFrom, finTo, Math.max(1, Math.round((finTo - finFrom) * 40)), 46, -1, 1.1),
    belly: tube(spine, width, Math.max(from, .03), to, .26, .36, steps),
  }
}
const bare = piece(0, still, .03)
const rest = { ...bare, body: bare.body + foreleg.join('') }
const swinging = joints.slice(0, -1).map((a, k) => ({ a, piece: piece(Math.max(0, a - overlap), Math.min(1, joints[k + 1] + overlap), a, joints[k + 1]), amp: 1.2 + k * 1.1, delay: -k * .32 }))

function Joints({ paint, tip }: { paint: (p: Piece) => ReactNode; tip?: ReactNode }) {
  return swinging.reduceRight<ReactNode>((inner, joint) => {
    const at = along(spine, joint.a)
    return <g className="dca-fx-coil" style={{ ...pivot(at.x, at.y, joint.delay), '--dca-coil': joint.amp } as CSSProperties}>{paint(joint.piece)}{inner}</g>
  }, tip)
}

// Outlines, bodies, then belly stripes: each pass repeats the same joints so no piece paints over a neighbour's stripe.
export function CloudDragon() {
  const outline = (p: Piece) => <><path d={p.fins} stroke="#fff" strokeWidth="16" strokeLinejoin="round" /><path d={p.body} stroke="#fff" strokeWidth="22" strokeLinejoin="round" /></>
  const body = (p: Piece) => <><path d={p.fins} fill={jade} /><path d={p.body} fill={jade} /></>
  const belly = (p: Piece) => <path d={p.belly} fill={mint} />
  return <>
    <Cloud x={804} y={150} s={.95} flip delay={-2} /><Cloud x={962} y={600} s={.9} delay={-4} />
    {outline(rest)}<Joints paint={outline} />
    {body(rest)}<Joints paint={body} />
    {belly(rest)}<Joints paint={belly} tip={<Plane d={tailTip} fill={mint} />} />
    <Head />
  </>
}

export function CloudDragonFront() {
  return <>
    <Plane d={[talon(378, 371, 112, 36, 20), talon(404, 372, 92, 40, 20), talon(430, 373, 72, 36, 20)]} fill={gold} contour={12} />
    <Cloud x={200} y={812} s={1.05} />
  </>
}
