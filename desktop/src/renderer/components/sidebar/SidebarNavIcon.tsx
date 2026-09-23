export type SidebarNavIconName =
  | 'new-chat'
  | 'search'
  | 'channels'
  | 'agents'
  | 'automations'
  | 'skills'
  | 'settings'

const STROKE_WIDTH: Record<SidebarNavIconName, number> = {
  'new-chat': 1.8,
  search: 2,
  channels: 2,
  agents: 2,
  automations: 2,
  skills: 2,
  settings: 1.8
}

const BOX_OUTLINE =
  'M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z'
const BOX_LID = 'M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8M3.3 7 12 12l8.7-5'
const BOX_BODY = 'M3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8'

function Glyph({ name }: { name: SidebarNavIconName }): JSX.Element {
  switch (name) {
    case 'new-chat':
      return (
        <>
          <path d="M12 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
          <g className="dotcraft-sidebar-nav-icon__pen-path">
            <g className="dotcraft-sidebar-nav-icon__pen-tilt">
              <path d="M18.375 2.625a1 1 0 0 1 3 3l-9.013 9.014a2 2 0 0 1-.853.505l-2.873.84a.5.5 0 0 1-.62-.62l.84-2.873a2 2 0 0 1 .506-.852z" />
            </g>
          </g>
        </>
      )
    case 'search':
      return (
        <>
          <path d="m21 21-4.34-4.34" />
          <g transform="rotate(-45 11 11)">
            <ellipse className="dotcraft-sidebar-nav-icon__lens" cx="11" cy="11" rx="8" ry="8" />
          </g>
        </>
      )
    case 'channels':
      return (
        <>
          <path d="M22 17a2 2 0 0 1-2 2H6.828a2 2 0 0 0-1.414.586l-2.202 2.202A.71.71 0 0 1 2 21.286V5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2z" />
          <g className="dotcraft-sidebar-nav-icon__typing">
            <circle className="dotcraft-sidebar-nav-icon__dot" cx="8" cy="11" r="1.25" />
            <circle className="dotcraft-sidebar-nav-icon__dot" cx="12" cy="11" r="1.25" />
            <circle className="dotcraft-sidebar-nav-icon__dot" cx="16" cy="11" r="1.25" />
          </g>
        </>
      )
    case 'agents':
      return (
        <>
          <path d="M12 8V4H8" />
          <rect width="16" height="12" x="4" y="8" rx="2" />
          <path d="M2 14h2" />
          <path d="M20 14h2" />
          <g className="dotcraft-sidebar-nav-icon__eyes">
            <path d="M15 13v2" />
            <path d="M9 13v2" />
          </g>
        </>
      )
    case 'automations':
      return (
        <>
          <circle cx="12" cy="12" r="9" />
          <line x1="12" y1="12" x2="16" y2="12" />
          <line className="dotcraft-sidebar-nav-icon__minute" x1="12" y1="12" x2="12" y2="8" />
        </>
      )
    case 'skills':
      return (
        <>
          <g className="dotcraft-sidebar-nav-icon__at-rest">
            <path d={BOX_OUTLINE} />
            <path d="m3.3 7 8.7 5 8.7-5" />
            <path d="M12 22V12" />
          </g>
          <g className="dotcraft-sidebar-nav-icon__in-motion">
            <path d={BOX_BODY} />
            <path d="M12 22V12" />
            <path className="dotcraft-sidebar-nav-icon__lid" d={BOX_LID} />
          </g>
        </>
      )
    case 'settings':
      return (
        <g className="dotcraft-sidebar-nav-icon__gear">
          <path d="M9.671 4.136a2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033 2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1-2.33 4.033 2.34 2.34 0 0 0-3.319 1.915 2.34 2.34 0 0 1-4.659 0 2.34 2.34 0 0 0-3.32-1.915 2.34 2.34 0 0 1-2.33-4.033 2.34 2.34 0 0 0 0-3.831A2.34 2.34 0 0 1 6.35 6.051a2.34 2.34 0 0 0 3.319-1.915" />
          <circle cx="12" cy="12" r="3" />
        </g>
      )
  }
}

export function SidebarNavIcon({ name, size = 16 }: { name: SidebarNavIconName; size?: number }): JSX.Element {
  return (
    <svg
      className={`dotcraft-sidebar-nav-icon dotcraft-sidebar-nav-icon--${name}`}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={STROKE_WIDTH[name]}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      <Glyph name={name} />
    </svg>
  )
}
