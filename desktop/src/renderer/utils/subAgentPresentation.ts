const SUB_AGENT_ACCENTS = ['#ff6b7a', '#0ea5ff', '#f59e0b', '#22c55e', '#a78bfa']

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

export function getSubAgentAccent(seed?: string | null): string {
  const normalized = normalizeText(seed)
  if (!normalized) return SUB_AGENT_ACCENTS[0]

  let hash = 0
  for (let i = 0; i < normalized.length; i += 1) {
    hash = ((hash << 5) - hash + normalized.charCodeAt(i)) | 0
  }
  return SUB_AGENT_ACCENTS[Math.abs(hash) % SUB_AGENT_ACCENTS.length]
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
