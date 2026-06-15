/**
 * Parametric DotCraft robot avatars for Agent Profiles.
 *
 * Uses the canonical DotCraft robot geometry (white shell + role-colored body +
 * white terminal face screen + stubby arms + glowing yellow status ball), the same
 * vocabulary as the Teams role avatars, so a generated agent looks native to DotCraft.
 *
 * Saved profiles may carry an explicit packed avatar number in frontmatter. Profiles without
 * one derive a deterministic spec from the profile name (stable identicon).
 */

export interface AvatarSpec {
  /** Palette index (role body + face mark colors). */
  palette: number
  /** Terminal face index (prompt / happy / curious / operator / skeptical). */
  face: number
  /** Corner accessory index (0 = none, then board / wrench / shield / lens / panel). */
  accessory: number
}

export interface PaletteEntry {
  key: string
  bodyD: string
  bodyM: string
  bodyL: string
  markD: string
  markL: string
  shadow: string
  accent: string
}

/** 12 role palettes. The first five mirror the shipped Teams role avatars. */
export const PALETTE: PaletteEntry[] = [
  { key: 'blue', bodyD: '#2563eb', bodyM: '#4f7cf6', bodyL: '#8198f5', markD: '#2563eb', markL: '#6f8df5', shadow: '#07307c', accent: '#4f7cf6' },
  { key: 'indigo', bodyD: '#4f46e5', bodyM: '#6366f1', bodyL: '#8b8ff8', markD: '#3730a3', markL: '#818cf8', shadow: '#1e1b4b', accent: '#6366f1' },
  { key: 'violet', bodyD: '#6d28d9', bodyM: '#8b5cf6', bodyL: '#a78bfa', markD: '#5b21b6', markL: '#a78bfa', shadow: '#4c1d95', accent: '#8b5cf6' },
  { key: 'fuchsia', bodyD: '#a21caf', bodyM: '#d946ef', bodyL: '#e9a8f5', markD: '#86198f', markL: '#e879f9', shadow: '#581c87', accent: '#d946ef' },
  { key: 'pink', bodyD: '#be185d', bodyM: '#ec4899', bodyL: '#f9a8d4', markD: '#9d174d', markL: '#f472b6', shadow: '#831843', accent: '#ec4899' },
  { key: 'rose', bodyD: '#be123c', bodyM: '#f43f5e', bodyL: '#fda4af', markD: '#9f1239', markL: '#fb7185', shadow: '#881337', accent: '#f43f5e' },
  { key: 'orange', bodyD: '#c2410c', bodyM: '#f97316', bodyL: '#fdba74', markD: '#9a3412', markL: '#fb923c', shadow: '#7c2d12', accent: '#f97316' },
  { key: 'amber', bodyD: '#d97706', bodyM: '#eab308', bodyL: '#fbbf24', markD: '#92400e', markL: '#f59e0b', shadow: '#78350f', accent: '#f59e0b' },
  { key: 'lime', bodyD: '#4d7c0f', bodyM: '#84cc16', bodyL: '#bef264', markD: '#3f6212', markL: '#a3e635', shadow: '#365314', accent: '#84cc16' },
  { key: 'green', bodyD: '#15803d', bodyM: '#22c55e', bodyL: '#4ade80', markD: '#166534', markL: '#4ade80', shadow: '#14532d', accent: '#22c55e' },
  { key: 'teal', bodyD: '#0f766e', bodyM: '#14b8a6', bodyL: '#5eead4', markD: '#115e59', markL: '#2dd4bf', shadow: '#134e4a', accent: '#14b8a6' },
  { key: 'sky', bodyD: '#0284c7', bodyM: '#0ea5e9', bodyL: '#38bdf8', markD: '#0369a1', markL: '#22d3ee', shadow: '#075985', accent: '#0ea5e9' }
]

export const FACE_COUNT = 5
export const ACCESSORY_COUNT = 6
const PALETTE_MASK = 0x0f
const FACE_MASK = 0x07
const ACCESSORY_MASK = 0x07
const FACE_SHIFT = 4
const ACCESSORY_SHIFT = 7
const AVATAR_MASK = PALETTE_MASK | (FACE_MASK << FACE_SHIFT) | (ACCESSORY_MASK << ACCESSORY_SHIFT)

/**
 * The canonical Agent Builder character: indigo/periwinkle body, a friendly "happy" face, and the
 * "create" sparkle accessory (index 6, rendered by RobotAvatar and never produced by avatarFromSeed).
 * Used for BOTH the Builder Welcome mascot and the plugin logo so they stay the same character.
 */
export const AGENT_BUILDER_AVATAR: AvatarSpec = { palette: 1, face: 1, accessory: 6 }

export function paletteOf(spec: AvatarSpec): PaletteEntry {
  return PALETTE[spec.palette % PALETTE.length]
}

export function encodeAvatar(spec: AvatarSpec): number {
  return (spec.palette & PALETTE_MASK)
    | ((spec.face & FACE_MASK) << FACE_SHIFT)
    | ((spec.accessory & ACCESSORY_MASK) << ACCESSORY_SHIFT)
}

export function decodeAvatar(value: number): AvatarSpec | null {
  if (!Number.isInteger(value) || value < 0 || (value & ~AVATAR_MASK) !== 0) return null
  const spec = {
    palette: value & PALETTE_MASK,
    face: (value >> FACE_SHIFT) & FACE_MASK,
    accessory: (value >> ACCESSORY_SHIFT) & ACCESSORY_MASK
  }
  return isAvatarSpec(spec) ? spec : null
}

export function isAvatarSpec(value: unknown): value is AvatarSpec {
  if (!value || typeof value !== 'object') return false
  const spec = value as Partial<AvatarSpec>
  return Number.isInteger(spec.palette)
    && Number.isInteger(spec.face)
    && Number.isInteger(spec.accessory)
    && spec.palette! >= 0
    && spec.palette! < PALETTE.length
    && spec.face! >= 0
    && spec.face! < FACE_COUNT
    && spec.accessory! >= 0
    && spec.accessory! < ACCESSORY_COUNT
}

export function normalizeAvatar(value: unknown): AvatarSpec | undefined {
  if (typeof value === 'number') return decodeAvatar(value) ?? undefined
  return isAvatarSpec(value) ? value : undefined
}

/** FNV-1a-ish string hash → unsigned 32-bit. */
function hash(seed: string): number {
  let h = 0x811c9dc5
  for (let i = 0; i < seed.length; i++) {
    h ^= seed.charCodeAt(i)
    h = Math.imul(h, 0x01000193)
  }
  return h >>> 0
}

/** Deterministic spec from a profile name (stable identicon). */
export function avatarFromSeed(seed: string): AvatarSpec {
  const h = hash(seed || 'agent')
  return {
    palette: h % PALETTE.length,
    face: (h >>> 5) % FACE_COUNT,
    accessory: (h >>> 9) % ACCESSORY_COUNT
  }
}

/**
 * Built-in Teams role templates → the spec of their shipped role avatar
 * (desktop/resources/.../agent-teams/assets/team-*.svg), so the gallery and editor render the
 * same character as the Teams board. Palette/face/accessory mirror those assets (team-builder uses
 * the closest parametric pairing — violet + happy + wrench, matching the agent-teams plugin logo).
 */
const TEAM_ROLE_AVATARS: Record<string, AvatarSpec> = {
  'team-leader': { palette: 0, face: 0, accessory: 1 },
  'team-explorer': { palette: 11, face: 2, accessory: 4 },
  'team-builder': { palette: 2, face: 1, accessory: 2 },
  'team-reviewer': { palette: 9, face: 4, accessory: 3 },
  'team-operator': { palette: 7, face: 3, accessory: 5 }
}

/** Fallback avatar for a profile id: a known Teams role keeps its shipped avatar; everything else is name-seeded. */
export function avatarForProfile(id: string): AvatarSpec {
  return TEAM_ROLE_AVATARS[id] ?? avatarFromSeed(id)
}

/**
 * Single source of truth for an agent profile's avatar across every surface
 * (builder gallery, profile picker, composer + welcome mascots). An explicit
 * avatar the user configured in the builder (stored in the profile frontmatter,
 * passed here as a packed number or spec) always wins; otherwise it falls back
 * to the derived avatar. Using this everywhere prevents the surfaces from
 * diverging — the picker/composer previously ignored the stored avatar and only
 * hashed the id, so a red-configured profile showed up green.
 */
export function resolveProfileAvatar(seed: string, storedAvatar?: number | AvatarSpec | null): AvatarSpec {
  return normalizeAvatar(storedAvatar) ?? avatarForProfile(seed)
}

/** A fresh random spec (the re-roll dice). Differs from `avoid` when possible. */
export function randomAvatar(avoid?: AvatarSpec): AvatarSpec {
  const r = (n: number): number => Math.floor(Math.random() * n)
  let spec: AvatarSpec = { palette: r(PALETTE.length), face: r(FACE_COUNT), accessory: r(ACCESSORY_COUNT) }
  if (avoid && spec.palette === avoid.palette && spec.face === avoid.face) {
    spec = { ...spec, palette: (spec.palette + 1) % PALETTE.length }
  }
  return spec
}
