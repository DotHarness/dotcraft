import { useEffect, useMemo, useState } from 'react'
import { RefreshCw, Trash2 } from 'lucide-react'
import { Button, PillSwitch, Select, SettingsBreadcrumb, SettingsGroup, SettingsPanelShell, SettingsRow } from '../ui'
import { GithubGlyph, GitlabGlyph } from '../ProviderGlyphs'
import { oratorioHost } from '../runtime'
import { useGitLabTokenKindLabel } from './oratorio-connect-parts'
import { FieldControl } from './oratorio-settings-controls'
import type { SettingsController } from './oratorio-settings-controller'
import type { SettingsDialog, WorkspaceBindingOption } from './oratorio-settings-dialogs'
import { useOratorioSettingsT } from './oratorio-settings-i18n'
import {
  cloneSettings,
  githubInstallationForOwner,
  gitlabProfileForProject,
  projectOwner,
  providerInstance,
  type GitLabProjectSecretKey,
  type OratorioProjectConfig,
  type OratorioSettingsConfig
} from './oratorio-settings-model'
import { buildOratorioProjectDisplayOptions, projectValueMatchesOption } from './oratorio-project-display'

export function useWorkspaceBindings(): { options: WorkspaceBindingOption[]; loading: boolean } {
  const [projects, setProjects] = useState<readonly { path: string; active: boolean }[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let active = true
    void oratorioHost().workspaces.listLocalProjects()
      .then((payload) => { if (active) setProjects(payload) })
      .finally(() => {
        if (active) setLoading(false)
      })
    return () => { active = false }
  }, [])

  const options = useMemo(() => {
    const foregroundIndex = projects.findIndex((project) => project.active)
    const ordered = foregroundIndex > 0
      ? [projects[foregroundIndex], ...projects.slice(0, foregroundIndex), ...projects.slice(foregroundIndex + 1)]
      : projects
    return ordered.map((project) => ({ value: project.path, label: project.path }))
  }, [projects])

  return { options, loading }
}

function withoutProject(config: OratorioSettingsConfig, projectId: string): OratorioSettingsConfig {
  const transaction = cloneSettings(config)
  const option = buildOratorioProjectDisplayOptions(transaction.projects).find((item) => item.projectId === projectId)
  transaction.projects = transaction.projects.filter((item) => item.id !== projectId)
  if (!option) return transaction
  transaction.autoReview = transaction.autoReview.filter((value) => !projectValueMatchesOption(value, option))
  transaction.draftPublish = transaction.draftPublish.filter((value) => !projectValueMatchesOption(value, option))
  transaction.followUp = transaction.followUp.filter((value) => !projectValueMatchesOption(value, option))
  return transaction
}

export function ProjectSettings({ project, workspaceOptions, workspaceLoading, syncing, controller, onSync, onBack, onRemoved, onDialog }: {
  project: OratorioProjectConfig
  workspaceOptions: WorkspaceBindingOption[]
  workspaceLoading: boolean
  syncing: boolean
  controller: SettingsController
  onSync: () => void
  onBack: () => void
  onRemoved: () => void
  onDialog: (dialog: SettingsDialog) => void
}) {
  const t = useOratorioSettingsT()
  const github = project.provider === 'github'
  const Icon = github ? GithubGlyph : GitlabGlyph
  const providerLabel = github ? 'GitHub' : 'GitLab'
  const settings = controller.draft
  function patch(change: Partial<OratorioProjectConfig>): void { controller.change('projects', controller.snapshot().projects.map((item) => item.id === project.id ? { ...item, ...change } : item)) }
  const resolvedWorkspaceOptions = project.workspacePath
    ? workspaceOptions.some((option) => option.value === project.workspacePath)
      ? workspaceOptions
      : [{ value: project.workspacePath, label: `${project.workspacePath} · ${t('workspaceUnavailable')}`, disabled: true }, ...workspaceOptions]
    : [{ value: '', label: t('workspaceNotBound'), disabled: true }, ...workspaceOptions]
  const providerReadable = github || settings.gitlab.enabled
  function remove(): void {
    void oratorioHost().ui.confirm({ title: t('removeProjectTitle'), message: t('removeProjectMessage'), confirmLabel: t('remove'), cancelLabel: t('cancel'), danger: true }).then((confirmed) => {
      if (!confirmed) return
      controller.change('configuration', withoutProject(controller.snapshot(), project.id))
      onRemoved()
    })
  }
  return <SettingsPanelShell title={project.projectKey} description={`${providerLabel} · ${providerInstance(project.provider, settings[project.provider].endpoint)}`} breadcrumb={<SettingsBreadcrumb parentLabel={t('oratorio')} currentLabel={project.projectKey} onBack={onBack} />}>
    <SettingsGroup title={t('project')}>
      <SettingsRow label={t('provider')} control={<span className="ora-provider-identity"><Icon size={18} />{providerLabel}</span>} />
      <SettingsRow label={t('enabled')} control={<FieldControl><PillSwitch checked={project.enabled} onChange={(value) => patch({ enabled: value })} aria-label={`${project.projectKey} ${t('enabled')}`} /></FieldControl>} />
    </SettingsGroup>
    {github ? <GitHubProjectAccess project={project} settings={settings} onDialog={onDialog} /> : <GitLabProjectAccess project={project} settings={settings} onDialog={onDialog} />}
    <SettingsGroup title={t('workspace')}><SettingsRow label={t('workspace')} control={<FieldControl>{resolvedWorkspaceOptions.length > 0 ? <Select ariaLabel={t('workspace')} value={project.workspacePath} disabled={workspaceLoading} onValueChange={(value) => patch({ workspacePath: value })} options={resolvedWorkspaceOptions} /> : <span className="ora-settings__value">{t('workspaceEmpty')}</span>}</FieldControl>} /></SettingsGroup>
    <SettingsGroup title={t('sourceSync')}><SettingsRow label={t('syncNow')} control={<span className="ora-sync-action"><Button variant="secondary" size="sm" disabled={!providerReadable} loading={syncing} iconLeft={<RefreshCw size={13} />} onClick={onSync}>{syncing ? t('syncing') : t('syncNow')}</Button></span>} /></SettingsGroup>
    <SettingsGroup><SettingsRow label={t('remove')} description={t('removeProjectMessage')} control={<Button variant="danger" size="sm" iconLeft={<Trash2 size={13} />} onClick={remove}>{t('remove')}</Button>} /></SettingsGroup>
  </SettingsPanelShell>
}

function GitHubProjectAccess({ project, settings, onDialog }: { project: OratorioProjectConfig; settings: OratorioSettingsConfig; onDialog: (dialog: SettingsDialog) => void }) {
  const t = useOratorioSettingsT()
  const owner = projectOwner(project.projectKey)
  const installation = githubInstallationForOwner(settings, owner)
  const action = installation ? t('manage') : t('set')
  return <SettingsGroup title={t('access')} description={t('accessDescription')}>
    <SettingsRow label={t('installation')} description={installation ? `${installation.installationId} · ${installation.source === 'detected' ? t('detected') : t('manual')}` : t('noInstallation')} control={<Button variant="secondary" size="sm" aria-label={`${t('installation')} ${action}`} onClick={() => onDialog({ kind: 'installation', owner })}>{action}</Button>} />
  </SettingsGroup>
}

function GitLabProjectAccess({ project, settings, onDialog }: { project: OratorioProjectConfig; settings: OratorioSettingsConfig; onDialog: (dialog: SettingsDialog) => void }) {
  const t = useOratorioSettingsT()
  const tokenKindLabel = useGitLabTokenKindLabel()
  const profile = gitlabProfileForProject(settings, project.projectKey)
  const token = profile?.secrets.token.configured ? profile : undefined
  const tokenAction = token ? t('replace') : t('set')
  const secretRow = (secretKey: GitLabProjectSecretKey, label: string) => {
    const configured = profile?.secrets[secretKey].configured ?? false
    const action = configured ? t('manage') : t('set')
    return <SettingsRow label={label} description={configured ? t('configured') : t('notSet')} control={<Button variant="secondary" size="sm" aria-label={`${label} ${action}`} onClick={() => onDialog({ kind: 'projectSecret', projectKey: project.projectKey, secretKey, secretName: label })}>{action}</Button>} />
  }
  return <SettingsGroup title={t('access')} description={t('accessDescription')}>
    <SettingsRow label={t('accessToken')} description={token ? `${tokenKindLabel(token.tokenKind)} · ${t('configured')}` : t('notSet')} control={<Button variant="secondary" size="sm" aria-label={`${t('accessToken')} ${tokenAction}`} onClick={() => onDialog({ kind: 'token', projectKey: project.projectKey })}>{tokenAction}</Button>} />
    {secretRow('webhookSecret', t('webhookSecret'))}
    {secretRow('webhookSigningToken', t('signingToken'))}
  </SettingsGroup>
}
