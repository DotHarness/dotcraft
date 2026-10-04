import Svg, { Circle, Line, Path, Rect } from 'react-native-svg'

type Shape =
  | ['path', { d: string }]
  | ['rect', { width: number; height: number; x: number; y: number; rx: number }]
  | ['circle', { cx: number; cy: number; r: number }]
  | ['line', { x1: number; x2: number; y1: number; y2: number }]

const ICONS = {
  chevronLeft: [['path', { d: 'm15 18-6-6 6-6' }]],
  chevronRight: [['path', { d: 'm9 18 6-6-6-6' }]],
  chevronDown: [['path', { d: 'm6 9 6 6 6-6' }]],
  cloudOff: [
    ['path', { d: 'M10.94 5.274A7 7 0 0 1 15.71 10h1.79a4.5 4.5 0 0 1 4.222 6.057' }],
    ['path', { d: 'M18.796 18.81A4.5 4.5 0 0 1 17.5 19H9A7 7 0 0 1 5.79 5.78' }],
    ['path', { d: 'm2 2 20 20' }],
  ],
  folder: [
    [
      'path',
      { d: 'M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z' },
    ],
  ],
  messagesSquare: [
    ['path', { d: 'M16 10a2 2 0 0 1-2 2H6.828a2 2 0 0 0-1.414.586l-2.202 2.202A.71.71 0 0 1 2 14.286V4a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z' }],
    ['path', { d: 'M20 9a2 2 0 0 1 2 2v10.286a.71.71 0 0 1-1.212.502l-2.202-2.202A2 2 0 0 0 17.172 19H10a2 2 0 0 1-2-2v-1' }],
  ],
  unplug: [
    ['path', { d: 'm19 5 3-3' }],
    ['path', { d: 'm2 22 3-3' }],
    ['path', { d: 'M6.3 20.3a2.4 2.4 0 0 0 3.4 0L12 18l-6-6-2.3 2.3a2.4 2.4 0 0 0 0 3.4Z' }],
    ['path', { d: 'M7.5 13.5 10 11' }],
    ['path', { d: 'M10.5 16.5 13 14' }],
    ['path', { d: 'm12 6 6 6 2.3-2.3a2.4 2.4 0 0 0 0-3.4l-2.6-2.6a2.4 2.4 0 0 0-3.4 0Z' }],
  ],
  monitor: [
    ['rect', { width: 20, height: 14, x: 2, y: 3, rx: 2 }],
    ['line', { x1: 8, x2: 16, y1: 21, y2: 21 }],
    ['line', { x1: 12, x2: 12, y1: 17, y2: 21 }],
  ],
  search: [
    ['path', { d: 'm21 21-4.34-4.34' }],
    ['circle', { cx: 11, cy: 11, r: 8 }],
  ],
  settings: [
    [
      'path',
      {
        d: 'M9.671 4.136a2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033 2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1-2.33 4.033 2.34 2.34 0 0 0-3.319 1.915 2.34 2.34 0 0 1-4.659 0 2.34 2.34 0 0 0-3.32-1.915 2.34 2.34 0 0 1-2.33-4.033 2.34 2.34 0 0 0 0-3.831A2.34 2.34 0 0 1 6.35 6.051a2.34 2.34 0 0 0 3.319-1.915',
      },
    ],
    ['circle', { cx: 12, cy: 12, r: 3 }],
  ],
  squarePen: [
    ['path', { d: 'M12 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7' }],
    ['path', { d: 'M18.375 2.625a1 1 0 0 1 3 3l-9.013 9.014a2 2 0 0 1-.853.505l-2.873.84a.5.5 0 0 1-.62-.62l.84-2.873a2 2 0 0 1 .506-.852z' }],
  ],
  arrowLeftRight: [
    ['path', { d: 'M8 3 4 7l4 4' }],
    ['path', { d: 'M4 7h16' }],
    ['path', { d: 'm16 21 4-4-4-4' }],
    ['path', { d: 'M20 17H4' }],
  ],
  info: [
    ['circle', { cx: 12, cy: 12, r: 10 }],
    ['path', { d: 'M12 16v-4' }],
    ['path', { d: 'M12 8h.01' }],
  ],
  qrCode: [
    ['rect', { width: 5, height: 5, x: 3, y: 3, rx: 1 }],
    ['rect', { width: 5, height: 5, x: 16, y: 3, rx: 1 }],
    ['rect', { width: 5, height: 5, x: 3, y: 16, rx: 1 }],
    ['path', { d: 'M21 16h-3a2 2 0 0 0-2 2v3' }],
    ['path', { d: 'M21 21v.01' }],
    ['path', { d: 'M12 7v3a2 2 0 0 1-2 2H7' }],
    ['path', { d: 'M3 12h.01' }],
    ['path', { d: 'M12 3h.01' }],
    ['path', { d: 'M12 16v.01' }],
    ['path', { d: 'M16 12h1' }],
    ['path', { d: 'M21 12v.01' }],
    ['path', { d: 'M12 21v-1' }],
  ],
  arrowUp: [
    ['path', { d: 'm5 12 7-7 7 7' }],
    ['path', { d: 'M12 19V5' }],
  ],
  cornerDownRight: [
    ['path', { d: 'm15 10 5 5-5 5' }],
    ['path', { d: 'M4 4v7a4 4 0 0 0 4 4h12' }],
  ],
  triangleAlert: [
    ['path', { d: 'm21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3' }],
    ['path', { d: 'M12 9v4' }],
    ['path', { d: 'M12 17h.01' }],
  ],
  x: [
    ['path', { d: 'M18 6 6 18' }],
    ['path', { d: 'm6 6 12 12' }],
  ],
  wrench: [
    [
      'path',
      {
        d: 'M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.106-3.105c.32-.322.863-.22.983.218a6 6 0 0 1-8.259 7.057l-7.91 7.91a1 1 0 0 1-2.999-3l7.91-7.91a6 6 0 0 1 7.057-8.259c.438.12.54.662.219.984z',
      },
    ],
  ],
  bookOpen: [
    ['path', { d: 'M12 7v14' }],
    ['path', { d: 'M3 18a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h5a4 4 0 0 1 4 4 4 4 0 0 1 4-4h5a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1h-6a3 3 0 0 0-3 3 3 3 0 0 0-3-3z' }],
  ],
  pencil: [
    ['path', { d: 'M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z' }],
    ['path', { d: 'm15 5 4 4' }],
  ],
  globe: [
    ['circle', { cx: 12, cy: 12, r: 10 }],
    ['path', { d: 'M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20' }],
    ['path', { d: 'M2 12h20' }],
  ],
  copy: [
    ['rect', { width: 14, height: 14, x: 8, y: 8, rx: 2 }],
    ['path', { d: 'M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2' }],
  ],
  check: [['path', { d: 'M20 6 9 17l-5-5' }]],
  ellipsisVertical: [
    ['circle', { cx: 12, cy: 12, r: 1 }],
    ['circle', { cx: 12, cy: 5, r: 1 }],
    ['circle', { cx: 12, cy: 19, r: 1 }],
  ],
  file: [
    ['path', { d: 'M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z' }],
    ['path', { d: 'M14 2v5a1 1 0 0 0 1 1h5' }],
  ],
  image: [
    ['rect', { width: 18, height: 18, x: 3, y: 3, rx: 2 }],
    ['circle', { cx: 9, cy: 9, r: 2 }],
    ['path', { d: 'm21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21' }],
  ],
  zap: [
    [
      'path',
      { d: 'M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z' },
    ],
  ],
  hand: [
    ['path', { d: 'M18 11V6a2 2 0 0 0-2-2a2 2 0 0 0-2 2' }],
    ['path', { d: 'M14 10V4a2 2 0 0 0-2-2a2 2 0 0 0-2 2v2' }],
    ['path', { d: 'M10 10.5V6a2 2 0 0 0-2-2a2 2 0 0 0-2 2v8' }],
    ['path', { d: 'M18 8a2 2 0 1 1 4 0v6a8 8 0 0 1-8 8h-2c-2.8 0-4.5-.86-5.99-2.34l-3.6-3.6a2 2 0 0 1 2.83-2.82L7 15' }],
  ],
  link: [
    ['path', { d: 'M9 17H7A5 5 0 0 1 7 7h2' }],
    ['path', { d: 'M15 7h2a5 5 0 1 1 0 10h-2' }],
    ['line', { x1: 8, x2: 16, y1: 12, y2: 12 }],
  ],
  box: [
    ['path', { d: 'M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z' }],
    ['path', { d: 'm3.3 7 8.7 5 8.7-5' }],
    ['path', { d: 'M12 22V12' }],
  ],
  squareTerminal: [
    ['path', { d: 'm7 11 2-2-2-2' }],
    ['path', { d: 'M11 13h4' }],
    ['rect', { width: 18, height: 18, x: 3, y: 3, rx: 2 }],
  ],
  shieldAlert: [
    [
      'path',
      { d: 'M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z' },
    ],
    ['path', { d: 'M12 8v4' }],
    ['path', { d: 'M12 16h.01' }],
  ],
  circleStop: [
    ['circle', { cx: 12, cy: 12, r: 10 }],
    ['rect', { width: 6, height: 6, x: 9, y: 9, rx: 1 }],
  ],
  gitFork: [
    ['circle', { cx: 12, cy: 18, r: 3 }],
    ['circle', { cx: 6, cy: 6, r: 3 }],
    ['circle', { cx: 18, cy: 6, r: 3 }],
    ['path', { d: 'M18 9v2c0 .6-.4 1-1 1H7c-.6 0-1-.4-1-1V9' }],
    ['path', { d: 'M12 12v3' }],
  ],
  archive: [
    ['rect', { width: 20, height: 5, x: 2, y: 3, rx: 1 }],
    ['path', { d: 'M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8' }],
    ['path', { d: 'M10 12h4' }],
  ],
  plus: [
    ['path', { d: 'M5 12h14' }],
    ['path', { d: 'M12 5v14' }],
  ],
  lightbulb: [
    ['path', { d: 'M15 14c.2-1 .7-1.7 1.5-2.5 1-.9 1.5-2.2 1.5-3.5A6 6 0 0 0 6 8c0 1 .2 2.2 1.5 3.5.7.7 1.3 1.5 1.5 2.5' }],
    ['path', { d: 'M9 18h6' }],
    ['path', { d: 'M10 22h4' }],
  ],
  circle: [['circle', { cx: 12, cy: 12, r: 10 }]],
  circleDot: [
    ['circle', { cx: 12, cy: 12, r: 10 }],
    ['circle', { cx: 12, cy: 12, r: 1 }],
  ],
  circleCheck: [
    ['circle', { cx: 12, cy: 12, r: 10 }],
    ['path', { d: 'm9 12 2 2 4-4' }],
  ],
  circleX: [
    ['circle', { cx: 12, cy: 12, r: 10 }],
    ['path', { d: 'm15 9-6 6' }],
    ['path', { d: 'm9 9 6 6' }],
  ],
  circleAlert: [
    ['circle', { cx: 12, cy: 12, r: 10 }],
    ['path', { d: 'M12 8v4' }],
    ['path', { d: 'M12 16h.01' }],
  ],
  listChecks: [
    ['path', { d: 'M13 5h8' }],
    ['path', { d: 'M13 12h8' }],
    ['path', { d: 'M13 19h8' }],
    ['path', { d: 'm3 17 2 2 4-4' }],
    ['path', { d: 'm3 7 2 2 4-4' }],
  ],
} satisfies Record<string, Shape[]>

export type IconName = keyof typeof ICONS

export function Icon({
  name,
  size,
  color,
  strokeWidth = 1.8,
  fill = 'none',
}: {
  name: IconName
  size: number
  color: string
  strokeWidth?: number
  fill?: string
}) {
  const shapes: Shape[] = ICONS[name]
  return (
    <Svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      style={{ flexShrink: 0 }}
      fill={fill}
      stroke={color}
      strokeWidth={strokeWidth}
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      {shapes.map(([tag, attributes], index) => {
        switch (tag) {
          case 'path':
            return <Path key={index} {...attributes} />
          case 'rect':
            return <Rect key={index} {...attributes} />
          case 'circle':
            return <Circle key={index} {...attributes} />
          case 'line':
            return <Line key={index} {...attributes} />
        }
      })}
    </Svg>
  )
}

export function StopGlyph({ size, color }: { size: number; color: string }) {
  return (
    <Svg width={size} height={size} viewBox="0 0 24 24">
      <Rect width={18} height={18} x={3} y={3} rx={2} fill={color} />
    </Svg>
  )
}
