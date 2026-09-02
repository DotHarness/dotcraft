import { Button, Input, PillSwitch, SegmentedControl, Select, SettingsGroup, SettingsRow, Skeleton, Textarea } from '../ui'
import { useOratorioConnectT } from './oratorio-connect-i18n'
import {
  githubAppConfigured,
  providerInstance,
  type ConnectDraft,
  type ConnectIssueField,
  type GitLabTokenKind,
  type KeyMode,
  type SchedulePreset,
  type WorkspaceListState
} from './oratorio-connect-model'
import { ChoiceCard, ConnectField, LearnMore, ProviderGlyph, QuietRow, StepHeading, providerName } from './oratorio-connect-parts'
import type { OratorioSettingsConfig, SourceProvider } from './oratorio-settings-model'

export interface StepProps {
  draft: ConnectDraft
  settings: OratorioSettingsConfig
  issues: ConnectIssueField[]
  readOnly: boolean
  update: (patch: Partial<ConnectDraft>) => void
}

export function SourceStep({ draft, settings, issues, readOnly, update }: StepProps): JSX.Element {
  const t = useOratorioConnectT()
  const github = draft.provider === 'github'
  const choose = (provider: SourceProvider): void => {
    if (provider !== draft.provider) update({ provider, endpoint: settings[provider].endpoint })
  }
  return (
    <div className="ora-connect__step">
      <StepHeading title={t('sourceTitle')} description={t('sourceDescription')} />
      <div className="ora-connect__cards" role="radiogroup" aria-label={t('stepSource')}>
        {(['github', 'gitlab'] as SourceProvider[]).map((provider) => (
          <ChoiceCard key={provider} name="ora-connect-provider" value={provider} active={draft.provider === provider} disabled={readOnly} title={providerName(provider)} description={provider === 'github' ? t('githubNeed') : t('gitlabNeed')} icon={<ProviderGlyph provider={provider} size={18} />} onSelect={() => choose(provider)} />
        ))}
      </div>
      <div className="ora-connect__fields">
        <ConnectField label={t('endpoint')} error={issues.includes('endpoint') ? t('endpointInvalid') : undefined} hint={github ? t('endpointHintGitHub') : t('endpointHintGitLab')}>
          <Input mono value={draft.endpoint} invalid={issues.includes('endpoint')} disabled={readOnly} aria-label={t('endpoint')} onChange={(event) => update({ endpoint: event.target.value })} />
        </ConnectField>
        {github ? <GitHubAccess draft={draft} settings={settings} readOnly={readOnly} update={update} /> : <GitLabAccess draft={draft} readOnly={readOnly} update={update} />}
      </div>
    </div>
  )
}

function GitHubAccess({ draft, settings, readOnly, update }: Omit<StepProps, 'issues'>): JSX.Element {
  const t = useOratorioConnectT()
  const github = draft.github
  const patch = (value: Partial<ConnectDraft['github']>): void => update({ github: { ...github, ...value } })
  if (githubAppConfigured(settings) && github.appId === settings.github.appId) {
    return (
      <QuietRow action={<Button variant="secondary" size="sm" disabled={readOnly} onClick={() => patch({ appId: '', privateKey: '', privateKeyPath: '' })}>{t('replace')}</Button>}>
        <strong>{t('appConfigured')}</strong>
        <small>{t('appConfiguredHint', { id: github.appId })}</small>
      </QuietRow>
    )
  }
  return (
    <>
      <ConnectField label={t('appId')} hint={<>{t('appIdHint')} <LearnMore provider="github" /></>}>
        <Input mono inputMode="numeric" value={github.appId} placeholder="184203" disabled={readOnly} aria-label={t('appId')} onChange={(event) => patch({ appId: event.target.value })} />
      </ConnectField>
      <ConnectField label={t('privateKey')} hint={t('privateKeyHint')}>
        <div className="ora-connect__stack">
          <SegmentedControl<KeyMode> ariaLabel={t('privateKey')} value={github.keyMode} disabled={readOnly} options={[{ value: 'paste', label: t('pasteKey') }, { value: 'path', label: t('useFile') }]} onValueChange={(keyMode) => patch({ keyMode })} />
          {github.keyMode === 'paste'
            ? <Textarea mono rows={4} value={github.privateKey} placeholder="-----BEGIN RSA PRIVATE KEY-----" disabled={readOnly} aria-label={t('privateKey')} onChange={(event) => patch({ privateKey: event.target.value })} />
            : <Input mono value={github.privateKeyPath} placeholder={t('privateKeyPathPlaceholder')} disabled={readOnly} aria-label={`${t('privateKey')} ${t('useFile')}`} onChange={(event) => patch({ privateKeyPath: event.target.value })} />}
        </div>
      </ConnectField>
    </>
  )
}

function GitLabAccess({ draft, readOnly, update }: Omit<StepProps, 'issues' | 'settings'>): JSX.Element {
  const t = useOratorioConnectT()
  const gitlab = draft.gitlab
  const patch = (value: Partial<ConnectDraft['gitlab']>): void => update({ gitlab: { ...gitlab, ...value } })
  return (
    <div className="ora-connect__field-row">
      <ConnectField label={t('tokenKind')}>
        <Select<GitLabTokenKind> ariaLabel={t('tokenKind')} value={gitlab.tokenKind} disabled={readOnly} options={[{ value: 'accessToken', label: t('projectAccessToken') }, { value: 'personalAccessToken', label: t('personalAccessToken') }, { value: 'groupAccessToken', label: t('groupAccessToken') }]} onValueChange={(tokenKind) => patch({ tokenKind })} />
      </ConnectField>
      <ConnectField label={t('token')} hint={<>{t('tokenHint')} <LearnMore provider="gitlab" /></>}>
        <Input type="password" value={gitlab.token} placeholder={t('tokenPlaceholder')} disabled={readOnly} autoComplete="off" aria-label={t('token')} onChange={(event) => patch({ token: event.target.value })} />
      </ConnectField>
    </div>
  )
}

export function ProjectStep({ draft, issues, readOnly, update }: StepProps): JSX.Element {
  const t = useOratorioConnectT()
  const github = draft.provider === 'github'
  const projectError = draft.projectKey ? (issues.includes('projectFormat') ? (github ? t('repositoryFormat') : t('projectFormat')) : issues.includes('duplicate') ? t('alreadyConnected') : undefined) : undefined
  const installationError = issues.includes('installationId') ? t('installationNumber') : undefined
  const key = draft.projectKey.trim().replace(/^\/+|\/+$/g, '')
  return (
    <div className="ora-connect__step">
      <StepHeading title={github ? t('whichRepository') : t('whichProject')} description={github ? t('projectDescriptionGitHub') : t('projectDescriptionGitLab')} />
      <div className="ora-connect__fields">
        <ConnectField label={github ? t('repository') : t('project')} error={projectError} hint={github ? t('repositoryHint') : t('projectHint')}>
          <Input mono autoFocus value={draft.projectKey} placeholder={github ? 'owner/repository' : 'group/project'} invalid={Boolean(projectError)} disabled={readOnly} aria-label={github ? t('repository') : t('project')} onChange={(event) => update({ projectKey: event.target.value })} />
        </ConnectField>
        {key && !projectError ? <span className="ora-connect__resolved" aria-live="polite"><ProviderGlyph provider={draft.provider} size={12} />{providerInstance(draft.provider, draft.endpoint)}/{key}</span> : null}
        {github ? (
          <ConnectField label={t('installationId')} error={installationError} hint={<>{t('installationHint')} <LearnMore provider="github" /></>}>
            <Input mono inputMode="numeric" value={draft.github.installationId} placeholder={t('installationPlaceholder')} invalid={Boolean(installationError)} disabled={readOnly} aria-label={t('installationId')} onChange={(event) => update({ github: { ...draft.github, installationId: event.target.value } })} />
          </ConnectField>
        ) : null}
      </div>
    </div>
  )
}

export function WorkspaceStep({ draft, readOnly, update, projects, workspaces }: StepProps & { projects: readonly { path: string; name: string }[]; workspaces: WorkspaceListState }): JSX.Element {
  const t = useOratorioConnectT()
  return (
    <div className="ora-connect__step">
      <StepHeading title={t('workspaceTitle')} description={t('workspaceDescription')} />
      {workspaces === 'loading' ? (
        <div className="ora-connect__workspaces" role="status" aria-label={t('workspaceLoading')} aria-busy="true">{[0, 1, 2].map((index) => <Skeleton key={index} height={58} radius={10} />)}</div>
      ) : workspaces === 'empty' ? (
        <div className="ora-connect__empty" role="status"><strong>{t('workspaceEmptyTitle')}</strong><span>{t('workspaceEmptyHint')}</span></div>
      ) : (
        <div className="ora-connect__workspaces" role="radiogroup" aria-label={t('stepWorkspace')}>
          {projects.map((project) => (
            <ChoiceCard key={project.path} name="ora-connect-workspace" value={project.path} active={draft.workspacePath === project.path} disabled={readOnly} title={project.name} description={project.path} onSelect={() => update({ workspacePath: project.path })} />
          ))}
        </div>
      )}
    </div>
  )
}

export function AutomationStep({ draft, issues, readOnly, update }: StepProps): JSX.Element {
  const t = useOratorioConnectT()
  const name = providerName(draft.provider)
  const minutesError = issues.includes('customMinutes') ? t('minutesRange') : undefined
  return (
    <div className="ora-connect__step">
      <StepHeading title={t('automationTitle')} description={t('automationDescription')} />
      <SettingsGroup>
        <SettingsRow label={t('schedule')} description={t('scheduleHint')} orientation="block" control={
          <div className="ora-connect__schedule">
            <SegmentedControl<SchedulePreset> ariaLabel={t('schedule')} value={draft.schedule} disabled={readOnly} options={[{ value: 'off', label: t('off') }, { value: '15m', label: t('every15') }, { value: '1h', label: t('everyHour') }, { value: 'custom', label: t('custom') }]} onValueChange={(schedule) => update({ schedule })} />
            {draft.schedule === 'custom' ? (
              <span className="ora-connect__schedule-custom">
                <span>{t('every')}</span>
                <Input type="number" inputMode="numeric" min={1} max={1440} value={draft.customMinutes} invalid={Boolean(minutesError)} disabled={readOnly} aria-label={`${t('custom')} ${t('minutes')}`} onChange={(event) => update({ customMinutes: Number(event.target.value) })} />
                <span>{t('minutes')}</span>
                {minutesError ? <small className="ora-connect__error" role="alert">{minutesError}</small> : null}
              </span>
            ) : null}
          </div>
        } />
        <SettingsRow label={t('automaticReview')} description={t('automaticReviewHint')} control={<PillSwitch checked={draft.autoReview} disabled={readOnly} aria-label={t('automaticReview')} onChange={(autoReview) => update({ autoReview })} />} />
        <SettingsRow label={t('allowWrites', { provider: name })} description={draft.provider === 'github' ? t('allowWritesHintGitHub') : t('allowWritesHintGitLab')} control={<PillSwitch checked={draft.allowWrites} disabled={readOnly} aria-label={t('allowWrites', { provider: name })} onChange={(allowWrites) => update({ allowWrites })} />} />
      </SettingsGroup>
      <p className="ora-connect__copy">{t('moreAutomation')}</p>
    </div>
  )
}
