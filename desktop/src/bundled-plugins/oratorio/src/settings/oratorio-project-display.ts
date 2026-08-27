import type { OratorioProjectConfig, SourceProvider } from './oratorio-settings-model'

export interface OratorioProjectDisplayOption {
  projectId: string
  value: string
  label: string
  tooltip: string
  aliases: string[]
}
export interface OratorioProjectDisplay {
  label: string
  tooltip: string
}
interface ProjectCandidate {
  project: OratorioProjectConfig
  provider: SourceProvider
  instance: string | null
  projectPath: string
  labelDepth: number
}

const canonicalProjectPattern = /^(github|gitlab):([^/]+)\/(.+)$/i

export function buildOratorioProjectDisplayOptions(projects: OratorioProjectConfig[]): OratorioProjectDisplayOption[] {
  const candidates = projects.map(toCandidate)
  let changed = true

  while (changed) {
    changed = false
    const labels = groupBy(candidates, (candidate) => comparable(compactPath(candidate.projectPath, candidate.labelDepth)))
    for (const duplicates of labels.values()) {
      if (duplicates.length < 2) continue
      for (const duplicate of duplicates) {
        const segmentCount = pathSegments(duplicate.projectPath).length
        if (duplicate.labelDepth < segmentCount) {
          duplicate.labelDepth += 1
          changed = true
        }
      }
    }
  }

  const options = candidates.map((candidate) => ({
    candidate,
    label: compactPath(candidate.projectPath, candidate.labelDepth),
  }))
  const remainingDuplicates = groupBy(options, (option) => comparable(option.label))

  return options.map(({ candidate, label }) => {
    const duplicates = remainingDuplicates.get(comparable(label)) ?? []
    const providerIsUnique = duplicates.filter((option) => option.candidate.provider === candidate.provider).length === 1
    const disambiguatedLabel = duplicates.length < 2
      ? label
      : providerIsUnique
        ? `${providerLabel(candidate.provider)} · ${label}`
        : `${providerLabel(candidate.provider)} · ${candidate.instance ? `${candidate.instance}/` : ''}${candidate.projectPath}`
    const value = candidate.project.routeProjectKey?.trim() || candidate.project.projectKey.trim()
    return {
      projectId: candidate.project.id,
      value,
      label: disambiguatedLabel,
      tooltip: projectTooltip(candidate.provider, candidate.instance, candidate.projectPath),
      aliases: uniqueStrings([value, candidate.project.projectKey, candidate.project.routeProjectKey ?? '']),
    }
  })
}

export function oratorioProjectDisplay(value: string, options: OratorioProjectDisplayOption[]): OratorioProjectDisplay {
  const matched = options.find((option) => option.aliases.some((alias) => sameText(alias, value)))
  if (matched) return { label: matched.label, tooltip: matched.tooltip }

  const parsed = parseCanonicalProject(value)
  if (parsed) {
    return {
      label: compactPath(parsed.projectPath, Math.min(2, pathSegments(parsed.projectPath).length)),
      tooltip: projectTooltip(parsed.provider, parsed.instance, parsed.projectPath),
    }
  }

  const projectPath = normalizeProjectPath(value)
  return {
    label: compactPath(projectPath, Math.min(2, pathSegments(projectPath).length)) || value,
    tooltip: value,
  }
}

export function selectedOratorioProjectValue(values: string[], option: OratorioProjectDisplayOption): string | undefined {
  return values.find((value) => option.aliases.some((alias) => sameText(alias, value)))
}

export function projectValueMatchesOption(value: string, option: OratorioProjectDisplayOption): boolean {
  return option.aliases.some((alias) => sameText(alias, value))
}

function toCandidate(project: OratorioProjectConfig): ProjectCandidate {
  const parsed = parseCanonicalProject(project.routeProjectKey)
  const projectPath = normalizeProjectPath(parsed?.projectPath ?? project.projectKey)
  return {
    project,
    provider: parsed?.provider ?? project.provider,
    instance: parsed?.instance ?? null,
    projectPath,
    labelDepth: Math.min(2, Math.max(1, pathSegments(projectPath).length)),
  }
}

function parseCanonicalProject(value: string | null | undefined): { provider: SourceProvider; instance: string; projectPath: string } | null {
  const match = value?.trim().match(canonicalProjectPattern)
  if (!match) return null
  const projectPath = normalizeProjectPath(match[3])
  if (!projectPath) return null
  return { provider: match[1].toLowerCase() as SourceProvider, instance: match[2].toLowerCase(), projectPath }
}

function projectTooltip(provider: SourceProvider, instance: string | null, projectPath: string): string {
  return `${providerLabel(provider)} · ${instance ? `${instance}/` : ''}${projectPath}`
}

function providerLabel(provider: SourceProvider): string {
  return provider === 'github' ? 'GitHub' : 'GitLab'
}

function compactPath(value: string, depth: number): string {
  const segments = pathSegments(value)
  return segments.slice(Math.max(0, segments.length - depth)).join('/')
}

function pathSegments(value: string): string[] {
  return normalizeProjectPath(value).split('/').filter(Boolean)
}

function normalizeProjectPath(value: string): string {
  return value.trim().replace(/\\/g, '/').replace(/^\/+|\/+$/g, '')
}

function uniqueStrings(values: string[]): string[] {
  const seen = new Set<string>()
  return values.filter((value) => {
    const normalized = comparable(value)
    if (!normalized || seen.has(normalized)) return false
    seen.add(normalized)
    return true
  })
}

function groupBy<T>(values: T[], key: (value: T) => string): Map<string, T[]> {
  const groups = new Map<string, T[]>()
  for (const value of values) {
    const groupKey = key(value)
    groups.set(groupKey, [...(groups.get(groupKey) ?? []), value])
  }
  return groups
}

function sameText(left: string, right: string): boolean {
  return comparable(left) === comparable(right)
}

function comparable(value: string): string {
  return value.trim().toLowerCase()
}
