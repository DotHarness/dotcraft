import Svg, { Circle, Line, Path, Rect } from 'react-native-svg'

type Shape =
  | ['path', { d: string }]
  | ['rect', { width: number; height: number; x: number; y: number; rx: number }]
  | ['circle', { cx: number; cy: number; r: number }]
  | ['line', { x1: number; x2: number; y1: number; y2: number }]

const ICONS = {
  chevronLeft: [['path', { d: 'm15 18-6-6 6-6' }]],
  chevronRight: [['path', { d: 'm9 18 6-6-6-6' }]],
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
  filePen: [
    ['path', { d: 'M12.659 22H18a2 2 0 0 0 2-2V8a2.4 2.4 0 0 0-.706-1.706l-3.588-3.588A2.4 2.4 0 0 0 14 2H6a2 2 0 0 0-2 2v9.34' }],
    ['path', { d: 'M14 2v5a1 1 0 0 0 1 1h5' }],
    ['path', { d: 'M10.378 12.622a1 1 0 0 1 3 3.003L8.36 20.637a2 2 0 0 1-.854.506l-2.867.837a.5.5 0 0 1-.62-.62l.836-2.869a2 2 0 0 1 .506-.853z' }],
  ],
  fileText: [
    ['path', { d: 'M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z' }],
    ['path', { d: 'M14 2v5a1 1 0 0 0 1 1h5' }],
    ['path', { d: 'M10 9H8' }],
    ['path', { d: 'M16 13H8' }],
    ['path', { d: 'M16 17H8' }],
  ],
  terminal: [
    ['path', { d: 'M12 19h8' }],
    ['path', { d: 'm4 17 6-6-6-6' }],
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
} satisfies Record<string, Shape[]>

export type IconName = keyof typeof ICONS

export function Icon({ name, size, color, strokeWidth = 1.8 }: { name: IconName; size: number; color: string; strokeWidth?: number }) {
  const shapes: Shape[] = ICONS[name]
  return (
    <Svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
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
