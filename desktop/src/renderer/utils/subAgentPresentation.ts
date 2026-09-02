import { avatarFromSeed, paletteOf } from '../components/agents/agentAvatar'
import type { SubAgentChild } from '../stores/subAgentStore'

interface SubAgentMetaInput {
  agentRole?: string | null
  profileName?: string | null
  runtimeType?: string | null
}

interface SubAgentIdentityInput {
  agentPath?: string | null
  childThreadId?: string | null
  nickname?: string | null
}

function normalizeText(value: string | null | undefined): string | null {
  const text = value?.trim()
  return text ? text : null
}

function isNativeProfile(profileName: string | null, runtimeType: string | null): boolean {
  const profile = profileName?.toLowerCase()
  const runtime = runtimeType?.toLowerCase()
  return (profile == null || profile === 'native') && (runtime == null || runtime === 'native')
}

export function isExternalSubAgentProfile(
  profileName?: string | null,
  runtimeType?: string | null
): boolean {
  const profile = normalizeText(profileName)?.toLowerCase()
  const runtime = normalizeText(runtimeType)?.toLowerCase()
  return profile === 'codex-cli'
    || profile === 'cursor-cli'
    || (runtime != null && runtime !== 'native')
}

export function formatSubAgentMeta({
  agentRole,
  profileName,
  runtimeType
}: SubAgentMetaInput): string {
  const role = normalizeText(agentRole)
  const profile = normalizeText(profileName)
  const runtime = normalizeText(runtimeType)
  const parts: string[] = []

  if (role && role.toLowerCase() !== 'default') {
    parts.push(role)
  }

  if (profile && !isNativeProfile(profile, runtime) && !parts.some((part) => part.toLowerCase() === profile.toLowerCase())) {
    parts.push(profile)
  } else if (!profile && runtime && runtime.toLowerCase() !== 'native') {
    parts.push(runtime)
  }

  return parts.join(' · ')
}

/** The tint comes from the seeded avatar's palette, so name and robot cannot drift. */
export function getSubAgentAccent(seed?: string | null): string {
  return paletteOf(avatarFromSeed(normalizeText(seed) ?? 'agent')).accent
}

export function getSubAgentIdentitySeed({
  agentPath,
  childThreadId,
  nickname
}: SubAgentIdentityInput): string | null {
  return normalizeText(agentPath)
    ?? normalizeText(childThreadId)
    ?? normalizeText(nickname)
}

/**
 * `SubAgentControlResult` omits the child thread id, so a spawn result identifies its
 * agent by `agentPath`. Match on either key.
 */
export function findSubAgentChild(
  childrenByParent: Map<string, SubAgentChild[]>,
  childThreadId: string | null | undefined,
  agentPath: string | null | undefined
): SubAgentChild | null {
  for (const children of childrenByParent.values()) {
    const child = children.find((entry) =>
      (childThreadId != null && entry.childThreadId === childThreadId)
      || (agentPath != null && entry.agentPath === agentPath)
    )
    if (child) return child
  }
  return null
}
