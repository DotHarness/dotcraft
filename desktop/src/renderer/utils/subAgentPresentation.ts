interface SubAgentMetaInput {
  agentRole?: string | null
}

function normalizeText(value: string | null | undefined): string | null {
  const text = value?.trim()
  return text ? text : null
}

export function formatSubAgentMeta({ agentRole }: SubAgentMetaInput): string {
  const role = normalizeText(agentRole)
  if (role && role.toLowerCase() !== 'default') {
    return role
  }
  return ''
}
