import type { OratorioSettingsConfig, SecretConfigurationField, SourceProvider } from './oratorio-settings-model'

type JsonObject = Record<string, any>

export interface LoadedOratorioSettings {
  settings: OratorioSettingsConfig
  serverConfiguration: JsonObject
  restartRequired: boolean
}

export async function loadOratorioSettings(): Promise<LoadedOratorioSettings> {
  const [configurationResponse, schedulesResponse] = await Promise.all([
    window.api.oratorio.request<JsonObject>({ method: 'GET', path: '/api/v1/settings/server-configuration' }),
    window.api.oratorio.request<JsonObject>({ method: 'GET', path: '/api/v1/sources/sync-schedules' })
  ])
  const envelope = configurationResponse.data
  return {
    settings: fromServer(envelope, schedulesResponse.data.schedules ?? []),
    serverConfiguration: structuredClone(envelope.configuration),
    restartRequired: Boolean(envelope.restartRequired)
  }
}

export async function saveOratorioSettings(
  settings: OratorioSettingsConfig,
  serverConfiguration: JsonObject
): Promise<LoadedOratorioSettings> {
  const configuration = toServer(settings, serverConfiguration)
  const response = await window.api.oratorio.request<JsonObject>({
    method: 'PUT',
    path: '/api/v1/settings/server-configuration',
    body: { baseRevision: settings.revision, confirmImpact: true, configuration }
  })
  const envelope = response.data.configuration
  return {
    settings: fromServer(envelope, [
      { provider: 'github', enabled: settings.github.syncIntervalSeconds !== null, intervalSeconds: settings.github.syncIntervalSeconds },
      { provider: 'gitlab', enabled: settings.gitlab.syncIntervalSeconds !== null, intervalSeconds: settings.gitlab.syncIntervalSeconds }
    ]),
    serverConfiguration: structuredClone(envelope.configuration),
    restartRequired: Boolean(envelope.restartRequired)
  }
}

export async function saveOratorioSyncSchedule(provider: 'github' | 'gitlab', intervalSeconds: number | null): Promise<void> {
  await window.api.oratorio.request({
    method: 'PUT', path: `/api/v1/sources/${provider}/sync-schedule`,
    body: { enabled: intervalSeconds !== null, intervalSeconds: intervalSeconds ?? 900 }
  })
}

function fromServer(envelope: JsonObject, schedules: JsonObject[]): OratorioSettingsConfig {
  const configuration = envelope.configuration ?? {}
  const github = configuration.gitHub ?? {}
  const gitlab = configuration.gitLab ?? {}
  const dotCraft = configuration.dotCraft ?? {}
  const runtime = configuration.runtime ?? {}
  const automation = configuration.automation ?? {}
  const schedule = (provider: string): number | null => schedules.find((item) => item.provider === provider && item.enabled)?.intervalSeconds ?? null
  const githubProfiles = (github.installationProfiles ?? []).map((profile: JsonObject) => ({
    id: githubProfileId(profile.instance, profile.owner),
    instance: profile.instance,
    owner: profile.owner,
    installationId: profile.installationId,
    source: profile.source === 'detected' ? 'detected' : 'manual'
  }))
  const gitlabProfiles = (gitlab.projectProfiles ?? []).map((profile: JsonObject) => ({
    id: gitlabProfileId(profile.instance, profile.projectPath),
    instance: profile.instance,
    projectPath: profile.projectPath,
    tokenKind: profile.tokenKind,
    secrets: normalizeSecrets(profile.secrets, ['token', 'webhookSecret', 'webhookSigningToken'])
  }))
  const routes = (dotCraft.repositoryWorkspaceRoutes ?? []) as Array<{ project: string; workspacePath: string }>
  const routeByProject = new Map(routes.map((route) => {
    const parsed = parseRouteProject(route.project)
    return [`${parsed?.provider ?? ''}:${parsed?.projectPath.toLowerCase() ?? ''}`, route]
  }))
  const gitlabProjectKeys = projectKeyMap(gitlab.projects ?? [])
  for (const profile of gitlabProfiles) addProjectKey(gitlabProjectKeys, profile.projectPath)
  const githubProjectKeys = projectKeyMap(github.repositories ?? [])
  for (const route of routes) {
    const parsed = parseRouteProject(route.project)
    if (parsed?.provider === 'github') addProjectKey(githubProjectKeys, parsed.projectPath)
    if (parsed?.provider === 'gitlab') addProjectKey(gitlabProjectKeys, parsed.projectPath)
  }
  const projects = [
    ...Array.from(githubProjectKeys.values()).map((projectKey: string) => ({
      id: `github:${projectKey}`, provider: 'github' as const, projectKey,
      routeProjectKey: routeByProject.get(`github:${projectKey.toLowerCase()}`)?.project,
      workspacePath: routeByProject.get(`github:${projectKey.toLowerCase()}`)?.workspacePath ?? '',
      profileId: githubProfiles.find((profile: { owner: string }) => projectKey.toLowerCase().startsWith(`${profile.owner.toLowerCase()}/`))?.id ?? '',
      enabled: (github.repositories ?? []).some((project: string) => project.toLowerCase() === projectKey.toLowerCase())
    })),
    ...Array.from(gitlabProjectKeys.values()).map((projectKey: string) => ({
      id: `gitlab:${projectKey}`, provider: 'gitlab' as const, projectKey,
      routeProjectKey: routeByProject.get(`gitlab:${projectKey.toLowerCase()}`)?.project,
      workspacePath: routeByProject.get(`gitlab:${projectKey.toLowerCase()}`)?.workspacePath ?? '',
      profileId: gitlabProfiles.find((profile: { projectPath: string }) => profile.projectPath.toLowerCase() === projectKey.toLowerCase())?.id ?? '',
      enabled: (gitlab.projects ?? []).some((project: string) => project.toLowerCase() === projectKey.toLowerCase())
    }))
  ]
  return {
    revision: String(envelope.revision ?? ''),
    approvalPolicy: dotCraft.approvalPolicy ?? 'interrupt',
    runTimeoutSeconds: dotCraft.runTimeoutSeconds ?? 1800,
    managedWorktreesEnabled: runtime.managedWorktreesEnabled ?? true,
    worktreeRoot: runtime.worktreeRoot ?? '',
    worktreeBranchPrefix: runtime.worktreeBranchPrefix ?? 'oratorio/run',
    globalMaxActiveRuns: runtime.globalMaxActiveRuns ?? 2,
    maxActiveRunsPerRepository: runtime.maxActiveRunsPerRepository ?? 1,
    maxActiveRunsPerSource: runtime.maxActiveRunsPerSource ?? 2,
    maxRunAttempts: runtime.maxRunAttempts ?? 3,
    retryBackoffSeconds: runtime.retryBackoffSeconds ?? 10,
    maxRetryBackoffSeconds: runtime.maxRetryBackoffSeconds ?? 300,
    stallTimeoutSeconds: runtime.stallTimeoutSeconds ?? 300,
    succeededWorktreeRetentionHours: runtime.succeededWorktreeRetentionHours ?? 24,
    failedWorktreeRetentionHours: runtime.failedWorktreeRetentionHours ?? 168,
    worktreeCleanupEnabled: runtime.worktreeCleanupEnabled ?? true,
    worktreeCleanupIntervalSeconds: runtime.worktreeCleanupIntervalSeconds ?? 60,
    autoDispatchEnabled: automation.autoDispatchEnabled ?? false,
    allowedLabels: normalizeLabels(automation.autoDispatchAllowLabels ?? []),
    blockedLabels: normalizeLabels(automation.autoDispatchBlockLabels ?? []),
    maxImplementationTurns: automation.maxImplementationTurns ?? 3,
    deliveryPolicy: automation.deliveryPolicy ?? 'manualDelivery',
    autoReview: automation.autoReviewRepositories ?? [],
    draftPublish: automation.autoReviewPublishRepositories ?? [],
    followUp: automation.autoFollowUpRepositories ?? [],
    maxFollowUpRounds: automation.maxFollowUpRounds ?? 5,
    github: {
      endpoint: github.endpoint ?? 'https://api.github.com', appId: github.appId ?? '',
      writesEnabled: github.writesEnabled ?? false, syncIntervalSeconds: schedule('github'), profiles: githubProfiles,
      secrets: normalizeSecrets(github.secrets, ['privateKey', 'privateKeyPath', 'webhookSecret'])
    },
    gitlab: {
      enabled: gitlab.enabled ?? false, endpoint: gitlab.endpoint ?? 'https://gitlab.com',
      apiBaseUrl: gitlab.apiBaseUrl ?? 'https://gitlab.com/api/v4', writesEnabled: gitlab.writesEnabled ?? false,
      syncIntervalSeconds: schedule('gitlab'), profiles: gitlabProfiles
    },
    projects
  }
}

function projectKeyMap(projects: string[]): Map<string, string> {
  const result = new Map<string, string>()
  for (const project of projects) addProjectKey(result, project)
  return result
}

function addProjectKey(projects: Map<string, string>, project: string): void {
  const value = project.trim()
  if (!value) return
  const identity = value.toLowerCase()
  if (!projects.has(identity)) projects.set(identity, value)
}

function toServer(settings: OratorioSettingsConfig, current: JsonObject): JsonObject {
  const next = structuredClone(current)
  const githubProjects = settings.projects.filter((item) => item.provider === 'github' && item.enabled)
  const gitlabProjects = settings.projects.filter((item) => item.provider === 'gitlab' && item.enabled)
  next.gitHub = next.gitHub ?? {}
  Object.assign(next.gitHub, {
    endpoint: settings.github.endpoint,
    appId: settings.github.appId || null,
    installationProfiles: settings.github.profiles.map(({ id: _, ...profile }) => profile),
    repositories: githubProjects.map((item) => item.projectKey),
    writesEnabled: settings.github.writesEnabled,
    secrets: settings.github.secrets
  })
  next.gitLab = next.gitLab ?? {}
  Object.assign(next.gitLab, {
    enabled: settings.gitlab.enabled,
    endpoint: settings.gitlab.endpoint,
    apiBaseUrl: settings.gitlab.apiBaseUrl,
    writesEnabled: settings.gitlab.writesEnabled,
    projects: gitlabProjects.map((item) => item.projectKey),
    projectProfiles: settings.gitlab.profiles.map(({ id: _, ...profile }) => profile)
  })
  next.dotCraft = next.dotCraft ?? {}
  next.dotCraft.repositoryWorkspaceRoutes = settings.projects
    .filter((item) => item.workspacePath)
    .map((item) => ({
      project: item.routeProjectKey ?? canonicalProjectKey(
        item.provider,
        item.projectKey,
        item.provider === 'github' ? settings.github.endpoint : settings.gitlab.endpoint
      ),
      workspacePath: item.workspacePath
    }))
  next.dotCraft.approvalPolicy = settings.approvalPolicy
  next.dotCraft.runTimeoutSeconds = settings.runTimeoutSeconds
  next.runtime = next.runtime ?? {}
  Object.assign(next.runtime, {
    managedWorktreesEnabled: settings.managedWorktreesEnabled,
    worktreeRoot: settings.worktreeRoot,
    worktreeBranchPrefix: settings.worktreeBranchPrefix,
    globalMaxActiveRuns: settings.globalMaxActiveRuns,
    maxActiveRunsPerRepository: settings.maxActiveRunsPerRepository,
    maxActiveRunsPerSource: settings.maxActiveRunsPerSource,
    maxRunAttempts: settings.maxRunAttempts,
    retryBackoffSeconds: settings.retryBackoffSeconds,
    maxRetryBackoffSeconds: settings.maxRetryBackoffSeconds,
    stallTimeoutSeconds: settings.stallTimeoutSeconds,
    succeededWorktreeRetentionHours: settings.succeededWorktreeRetentionHours,
    failedWorktreeRetentionHours: settings.failedWorktreeRetentionHours,
    worktreeCleanupEnabled: settings.worktreeCleanupEnabled,
    worktreeCleanupIntervalSeconds: settings.worktreeCleanupIntervalSeconds
  })
  next.automation = next.automation ?? {}
  Object.assign(next.automation, {
    autoDispatchEnabled: settings.autoDispatchEnabled,
    autoDispatchAllowLabels: normalizeLabels(settings.allowedLabels),
    autoDispatchBlockLabels: normalizeLabels(settings.blockedLabels),
    maxImplementationTurns: settings.maxImplementationTurns,
    deliveryPolicy: settings.deliveryPolicy,
    autoReviewRepositories: settings.autoReview,
    autoReviewPublishEnabled: settings.draftPublish.length > 0,
    autoReviewPublishRepositories: settings.draftPublish,
    autoFollowUpEnabled: settings.followUp.length > 0,
    autoFollowUpRepositories: settings.followUp,
    maxFollowUpRounds: settings.maxFollowUpRounds
  })
  return next
}

function parseRouteProject(value: string): { provider: SourceProvider; projectPath: string } | null {
  const match = /^(github|gitlab):[^/]+\/(.+)$/i.exec(value)
  if (!match) return null
  return { provider: match[1].toLowerCase() as SourceProvider, projectPath: match[2] }
}

function canonicalProjectKey(provider: SourceProvider, projectPath: string, endpoint: string): string {
  const host = new URL(endpoint).hostname.toLowerCase()
  const instance = provider === 'github' && host === 'api.github.com' ? 'github.com' : host
  return `${provider}:${instance}/${projectPath.toLowerCase()}`
}

function normalizeSecrets(source: JsonObject | undefined, keys: string[]): any {
  return Object.fromEntries(keys.map((key) => [key, normalizeSecret(source?.[key])]))
}

function normalizeSecret(value: JsonObject | undefined): SecretConfigurationField {
  return { configured: Boolean(value?.configured), mode: 'unchanged', value: null }
}

function normalizeLabels(labels: string[]): string[] {
  const normalized: string[] = []
  for (const label of labels) {
    const trimmed = label.trim()
    if (!trimmed || normalized.some((candidate) => candidate.toLowerCase() === trimmed.toLowerCase())) continue
    normalized.push(trimmed)
  }
  return normalized
}

function githubProfileId(instance: string, owner: string): string { return `github:${instance}:${owner}` }
function gitlabProfileId(instance: string, project: string): string { return `gitlab:${instance}:${project}` }
