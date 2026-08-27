export const TEAM_AVATAR_URLS: Record<string, string> = {
  leader: new URL('./assets/team-leader.svg', import.meta.url).toString(),
  explorer: new URL('./assets/team-explorer.svg', import.meta.url).toString(),
  builder: new URL('./assets/team-builder.svg', import.meta.url).toString(),
  reviewer: new URL('./assets/team-reviewer.svg', import.meta.url).toString(),
  operator: new URL('./assets/team-operator.svg', import.meta.url).toString()
}

export const TEAM_BOARD_WIDTH = 1360
export const TEAM_BOARD_HEIGHT = 1280
export const TEAM_BOARD_FIT_HEIGHT = 720
export const TEAM_BOARD_MIN_SCALE = 0.72
export const TEAM_BOARD_MAX_SCALE = 0.94
export const TEAM_BOARD_EDGE_PAD = 32
export const TEAM_CARD_WIDTH = 128
export const TEAM_CARD_MIN_HEIGHT = 168
export const HISTORY_PAGE_SIZE = 6

export const MISSION_LAYOUTS = [
  { x: 528, y: 190, rotation: -1.5 },
  { x: 195, y: 122, rotation: -3 },
  { x: 888, y: 255, rotation: 2 },
  { x: 424, y: 360, rotation: -3.5 },
  { x: 1040, y: 172, rotation: -2.5 }
]

export const TASK_LAYOUTS = [
  { x: 960, y: 185, rotation: -2.5 },
  { x: 360, y: 310, rotation: -3.5 },
  { x: 800, y: 340, rotation: 2 },
  { x: 648, y: 430, rotation: -1.8 },
  { x: 1048, y: 305, rotation: 3 },
  { x: 224, y: 420, rotation: 2.4 },
  { x: 752, y: 130, rotation: -2.8 },
  { x: 480, y: 220, rotation: 1.6 }
]

export const HAND_LAYOUTS: Record<string, { x: number; y: number; rotation: number }> = {
  leader: { x: 376, y: 538, rotation: -2.6 },
  explorer: { x: 1016, y: 538, rotation: 2.2 },
  builder: { x: 536, y: 562, rotation: -2.2 },
  reviewer: { x: 696, y: 557, rotation: 2.6 },
  operator: { x: 856, y: 552, rotation: -1.8 }
}

export const HAND_BASE_Y = 538
export const HAND_BOTTOM_PAD = 96
export const HAND_ZONE_HEIGHT = 142
export const HAND_ZONE_BOTTOM_PAD = 72

export const HISTORY_LAYOUTS = [
  { x: 266, y: 44, rotation: -4.2, dealX: -304, dealY: -170 },
  { x: 643, y: 38, rotation: 2.6, dealX: -128, dealY: -200 },
  { x: 1018, y: 44, rotation: -2.4, dealX: 192, dealY: -190 },
  { x: 269, y: 300, rotation: 3.4, dealX: -352, dealY: -80 },
  { x: 643, y: 296, rotation: -2.7, dealX: 64, dealY: -130 },
  { x: 1018, y: 300, rotation: 2.1, dealX: 336, dealY: -90 }
]

export const ROLE_ACCENTS: Record<string, string> = {
  leader: '#4f7cf6',
  explorer: '#0ea5e9',
  builder: '#7c3aed',
  reviewer: '#22a45a',
  operator: '#d88700'
}
