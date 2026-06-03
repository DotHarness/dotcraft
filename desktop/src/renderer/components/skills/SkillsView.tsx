import { useEffect, useMemo, useState } from 'react'
import { Check, ChevronLeft, Download, Ellipsis, ExternalLink, Plus, Settings, Sparkles } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useSkillsStore, type SkillEntry } from '../../stores/skillsStore'
import { useSkillMarketStore, type SkillMarketProviderFilter } from '../../stores/skillMarketStore'
import { useConnectionStore, type ServerCapabilities } from '../../stores/connectionStore'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import type { MarketDotCraftInstallPreparation, MarketSkillDetail, MarketSkillSummary } from '../../../shared/skillMarket'
import { SkillAvatar } from './SkillAvatar'
import { SkillDetailDialog } from './SkillDetailDialog'
import { VariantBadge } from './VariantBadge'
import { PillSwitch } from '../ui/PillSwitch'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { RefreshIcon } from '../ui/AppIcons'
import { addToast } from '../../stores/toastStore'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { useUIStore } from '../../stores/uiStore'
import type { ThreadSummary } from '../../types/thread'
import { CatalogFilterMenu, CatalogSearchBox, styles as catalogStyles } from '../catalog/CatalogSurface'
import { SkeletonCatalogGrid, SkeletonList } from '../ui/Skeleton'

type ViewMode = 'browse' | 'manage'
export type SourceFilter = 'all' | 'system' | 'personal' | 'market'

interface SkillsViewProps {
  onManage?: () => void
}

export function SkillsView({ onManage }: SkillsViewProps = {}): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const {
    skills,
    loading,
    error,
    fetchSkills,
    selectedSkillName,
    skillContent,
    contentLoading,
    selectSkill,
    clearSelection,
    toggleSkillEnabled,
    uninstallSkill
  } = useSkillsStore()
  const {
    query: marketQuery,
    results,
    loading: marketLoading,
    error: marketError,
    selectedSkill: selectedMarketSkill,
    detailLoading,
    installSlug,
    dotCraftInstallSlug,
    setQuery: setMarketQuery,
    search,
    selectSkill: selectMarketSkill,
    clearSelection: clearMarketSelection,
    installSelected,
    prepareDotCraftInstall
  } = useSkillMarketStore()
  const connectionStatus = useConnectionStore((s) => s.status)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const remoteWorkspaceActive = useConversationStore((s) => s.remoteWorkspaceActive)
  const { addThread, setActiveThreadId } = useThreadStore()

  const [mode, setMode] = useState<ViewMode>('browse')
  const [query, setQuery] = useState('')
  const [sourceFilter, setSourceFilter] = useState<SourceFilter>('all')
  const [menuPosition, setMenuPosition] = useState<ContextMenuPosition | null>(null)
  const [savedSkillName, setSavedSkillName] = useState<string | null>(null)
  const [selfLearningEnabled, setSelfLearningEnabled] = useState(true)

  useEffect(() => {
    void fetchSkills()
  }, [fetchSkills])

  useEffect(() => {
    setMarketQuery(query)
  }, [query, setMarketQuery])

  useEffect(() => {
    const trimmed = marketQuery.trim()
    if (!trimmed) {
      void search()
      return
    }
    const timer = window.setTimeout(() => void search(), 350)
    return () => window.clearTimeout(timer)
  }, [marketQuery, search])

  useEffect(() => {
    if (!savedSkillName) return
    const timer = window.setTimeout(() => setSavedSkillName(null), 1500)
    return () => window.clearTimeout(timer)
  }, [savedSkillName])

  useEffect(() => {
    let disposed = false
    window.api.workspaceConfig
      .getCore()
      .then((core) => {
        if (disposed) return
        setSelfLearningEnabled(
          core.workspace.skillsSelfLearningEnabled ??
          core.userDefaults.skillsSelfLearningEnabled ??
          true
        )
      })
      .catch(() => {
        if (!disposed) setSelfLearningEnabled(true)
      })
    return () => {
      disposed = true
    }
  }, [])

  const filteredSkills = useMemo(() => filterLocalSkills(skills, query, sourceFilter), [skills, query, sourceFilter])
  const manageSkills = useMemo(() => filterLocalSkills(skills, query, 'all'), [skills, query])
  const systemSkills = filteredSkills.filter((skill) => skill.source === 'builtin')
  const personalSkills = filteredSkills.filter((skill) => skill.source !== 'builtin')
  const marketResults = sourceFilter === 'system' || sourceFilter === 'personal'
    ? []
    : results

  const selected = selectedSkillName
    ? skills.find((s) => s.name === selectedSkillName) ?? null
    : null
  const bodyMd = selected && skillContent != null ? stripYamlFrontmatter(skillContent) : ''

  async function handleRefresh(): Promise<void> {
    await fetchSkills()
    if (query.trim()) await search()
  }

  function handleTrySkillInChat(skill: SkillEntry): void {
    const text = `$${skill.name}`
    const ui = useUIStore.getState()
    const existing = ui.welcomeDraft
    ui.setWelcomeDraft({
      text,
      segments: [{ type: 'skill', skillName: skill.name }],
      selectionStart: 1,
      selectionEnd: 1,
      images: [],
      files: [],
      mode: existing?.mode ?? 'agent',
      model: existing?.model || 'Default',
      approvalPolicy: existing?.approvalPolicy ?? 'default'
    })
    clearSelection()
    ui.goToNewChat()
  }

  async function handleInstallMarketSkill(skill: MarketSkillDetail, overwrite = false): Promise<void> {
    if (remoteWorkspaceActive) {
      addToast(t('skillMarket.installUnavailableRemote'), 'warning')
      return
    }
    if ((skill.installed || skill.updateAvailable) && !overwrite) {
      const ok = await confirm({
        title: t('skillMarket.overwriteTitle'),
        message: t('skillMarket.overwriteMessage', { name: skill.name }),
        confirmLabel: skill.updateAvailable ? t('skillMarket.update') : t('skillMarket.reinstall'),
        cancelLabel: t('common.cancel')
      })
      if (!ok) return
      await handleInstallMarketSkill(skill, true)
      return
    }
    try {
      await installSelected(overwrite)
      await fetchSkills()
      addToast(t('skillMarket.installSuccess', { name: skill.name }), 'success')
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      addToast(t('skillMarket.installFailed', { error: msg }), 'error')
    }
  }

  async function handleDotCraftInstallMarketSkill(skill: MarketSkillDetail): Promise<void> {
    const disabledReason = dotCraftInstallDisabledReason(
      connectionStatus,
      capabilities,
      selfLearningEnabled,
      remoteWorkspaceActive,
      t
    )
    if (disabledReason) {
      addToast(disabledReason, 'warning')
      return
    }

    try {
      const preparation = await prepareDotCraftInstall()
      await startDotCraftInstallThread(skill, preparation, t, addThread, setActiveThreadId)
      clearMarketSelection()
      addToast(t('skillMarket.dotCraftInstallStarted', { name: skill.name }), 'success')
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      addToast(t('skillMarket.dotCraftInstallFailed', { error: msg }), 'error')
    }
  }

  async function handleRestoreOriginalSkill(skill: SkillEntry): Promise<void> {
    if (connectionStatus !== 'connected' || capabilities?.skillVariants !== true) {
      addToast(t('skillDetail.restoreOriginalUnavailable'), 'warning')
      return
    }

    try {
      const result = await window.api.appServer.sendRequest('skills/restoreOriginal', {
        name: skill.name
      }) as { restored?: boolean }
      if (result.restored) {
        addToast(t('skillDetail.restoreOriginalSuccess'), 'success')
        await fetchSkills()
        await selectSkill(skill.name)
      } else {
        addToast(t('skillDetail.restoreOriginalNoop'), 'info')
      }
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      addToast(t('skillDetail.restoreOriginalFailed', { error: msg }), 'error')
    }
  }

  async function handleUninstallSkill(skill: SkillEntry): Promise<void> {
    if (!isUninstallableSkill(skill)) return

    const ok = await confirm({
      title: t('skillDetail.uninstallTitle', { name: skill.displayName ?? skill.name }),
      message: skill.hasVariant
        ? t('skillDetail.uninstallVariantMessage', { name: skill.displayName ?? skill.name })
        : t('skillDetail.uninstallMessage', { name: skill.displayName ?? skill.name }),
      confirmLabel: t('skillDetail.uninstall'),
      cancelLabel: t('common.cancel'),
      danger: true
    })
    if (!ok) return

    try {
      await uninstallSkill(skill.name)
      await fetchSkills()
      addToast(t('skillDetail.uninstallSuccess', { name: skill.displayName ?? skill.name }), 'success')
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      addToast(t('skillDetail.uninstallFailed', { error: msg }), 'error')
    }
  }

  const dotCraftDisabledReason = dotCraftInstallDisabledReason(
    connectionStatus,
    capabilities,
    selfLearningEnabled,
    remoteWorkspaceActive,
    t
  )
  const installDisabledReason = remoteWorkspaceActive ? t('skillMarket.installUnavailableRemote') : null

  if (mode === 'manage') {
    return (
      <SkillsManageView
        skills={manageSkills}
        allSkills={skills}
        query={query}
        loading={loading}
        error={error}
        savedSkillName={savedSkillName}
        onQueryChange={setQuery}
        onBack={() => setMode('browse')}
        onToggleEnabled={async (skill, enabled) => {
          try {
            await toggleSkillEnabled(skill.name, enabled)
            setSavedSkillName(skill.name)
          } catch {
            addToast(t('skills.updateFailed'), 'error')
          }
        }}
      />
    )
  }

  return (
    <div style={page}>
      <header style={browseHeader}>
        <div style={topActions}>
          <button type="button" onClick={() => onManage ? onManage() : setMode('manage')} style={manageButton}>
            <Settings size={14} aria-hidden />
            <span style={manageButtonLabel}>{t('skills.manage')}</span>
          </button>
          <ActionTooltip label={t('skills.moreActions')} placement="bottom">
            <button
              type="button"
              aria-label={t('skills.moreActions')}
              onClick={(event) => setMenuPosition({ x: event.clientX, y: event.clientY })}
              style={iconButton}
            >
              <Ellipsis size={16} aria-hidden />
            </button>
          </ActionTooltip>
        </div>

        <h1 style={heroTitle}>{t('skills.heroTitle')}</h1>
        <div style={searchRow}>
          <CatalogSearchBox
            value={query}
            placeholder={t('skills.searchPlaceholder')}
            onChange={setQuery}
          />
          <SkillFilterMenu value={sourceFilter} onChange={setSourceFilter} />
        </div>
      </header>

      <main style={browseMain}>
        {loading && <SkeletonCatalogGrid ariaLabel={t('skills.loading')} />}
        {error && <p style={{ ...emptyText, color: 'var(--error)' }} role="alert">{error}</p>}
        {marketError && <p style={{ ...emptyText, color: 'var(--error)' }} role="alert">{marketError}</p>}

        {query.trim() && sourceFilter !== 'system' && sourceFilter !== 'personal' && (
          <SkillSection title={t('skills.section.marketResults')}>
            {marketLoading ? (
              <SkeletonCatalogGrid count={4} ariaLabel={t('skillMarket.loading')} />
            ) : marketResults.length > 0 ? (
              <CompactGrid>
                {marketResults.map((skill) => (
                  <MarketSkillItem
                    key={`${skill.provider}:${skill.slug}`}
                    skill={skill}
                    onOpen={() => void selectMarketSkill(skill)}
                  />
                ))}
              </CompactGrid>
            ) : (
              <p style={emptyText}>{t('skillMarket.noResults')}</p>
            )}
          </SkillSection>
        )}

        {sourceFilter !== 'market' && (
          <>
            {systemSkills.length > 0 && (
              <SkillSection title={t('skills.section.system')}>
                <CompactGrid>
                  {systemSkills.map((skill) => (
                    <LocalSkillItem key={skill.name} skill={skill} onOpen={() => void selectSkill(skill.name)} />
                  ))}
                </CompactGrid>
              </SkillSection>
            )}
            {personalSkills.length > 0 && (
              <SkillSection title={t('skills.section.personal')}>
                <CompactGrid>
                  {personalSkills.map((skill) => (
                    <LocalSkillItem key={skill.name} skill={skill} onOpen={() => void selectSkill(skill.name)} />
                  ))}
                </CompactGrid>
              </SkillSection>
            )}
          </>
        )}

        {!loading && !error && !query.trim() && systemSkills.length === 0 && personalSkills.length === 0 && (
          <p style={emptyText}>{t('skills.empty')}</p>
        )}
      </main>

      {menuPosition && (
        <ContextMenu
          position={menuPosition}
          onClose={() => setMenuPosition(null)}
          items={[
            {
              label: t('skills.refresh'),
              icon: <RefreshIcon size={14} />,
              onClick: () => void handleRefresh()
            }
          ]}
        />
      )}

      {selected && (
        <SkillDetailDialog
          skill={selected}
          markdownBody={bodyMd}
          loading={contentLoading}
          showToggle
          onClose={() => clearSelection()}
          onToggleEnabled={async (enabled) => {
            try {
              await toggleSkillEnabled(selected.name, enabled)
              setSavedSkillName(selected.name)
            } catch {
              addToast(t('skills.updateFailed'), 'error')
            }
          }}
          onTryInChat={() => handleTrySkillInChat(selected)}
          onRestoreOriginal={() => void handleRestoreOriginalSkill(selected)}
          onUninstall={isUninstallableSkill(selected) ? () => void handleUninstallSkill(selected) : undefined}
        />
      )}

      {selectedMarketSkill && (
        <MarketSkillDetailDialog
          skill={selectedMarketSkill}
          loading={detailLoading}
          installing={installSlug === selectedMarketSkill.slug}
          dotCraftInstalling={dotCraftInstallSlug === selectedMarketSkill.slug}
          dotCraftDisabledReason={dotCraftDisabledReason}
          installDisabledReason={installDisabledReason}
          onClose={clearMarketSelection}
          onInstall={() => void handleInstallMarketSkill(selectedMarketSkill)}
          onDotCraftInstall={() => void handleDotCraftInstallMarketSkill(selectedMarketSkill)}
        />
      )}
    </div>
  )
}

function SkillFilterMenu({
  value,
  onChange
}: {
  value: SourceFilter
  onChange: (value: SourceFilter) => void
}): JSX.Element {
  const t = useT()
  return (
    <CatalogFilterMenu
      value={value}
      ariaLabel={t('skills.filter.label')}
      onChange={onChange}
      options={[
        { value: 'all', label: t('skills.filter.all') },
        { value: 'system', label: t('skills.filter.system') },
        { value: 'personal', label: t('skills.filter.personal') },
        { value: 'market', label: t('skills.filter.market') }
      ]}
    />
  )
}

function SkillsManageView({
  skills,
  allSkills,
  query,
  loading,
  error,
  savedSkillName,
  onQueryChange,
  onBack,
  onToggleEnabled
}: {
  skills: SkillEntry[]
  allSkills: SkillEntry[]
  query: string
  loading: boolean
  error: string | null
  savedSkillName: string | null
  onQueryChange: (query: string) => void
  onBack: () => void
  onToggleEnabled: (skill: SkillEntry, enabled: boolean) => void
}): JSX.Element {
  const t = useT()

  return (
    <div style={page}>
      <header style={manageHeader}>
        <div style={breadcrumb}>
          <button type="button" onClick={onBack} style={breadcrumbButton}>
            <ChevronLeft size={14} aria-hidden />
            {t('skills.pageTitle')}
          </button>
          <span style={breadcrumbSep}>›</span>
          <span style={breadcrumbCurrent}>{t('skills.manage')}</span>
        </div>
        <SkillsManageToolbar
          allSkills={allSkills}
          query={query}
          savedSkillName={savedSkillName}
          onQueryChange={onQueryChange}
        />
      </header>

      <SkillsManageList
        skills={skills}
        loading={loading}
        error={error}
        onToggleEnabled={onToggleEnabled}
      />
    </div>
  )
}

export function SkillsManageToolbar({
  allSkills,
  query,
  savedSkillName,
  onQueryChange
}: {
  allSkills: SkillEntry[]
  query: string
  savedSkillName: string | null
  onQueryChange: (query: string) => void
}): JSX.Element {
  const t = useT()
  const systemCount = allSkills.filter((skill) => skill.source === 'builtin').length
  const personalCount = allSkills.length - systemCount

  return (
    <div style={manageToolbar}>
      <Chip label={t('skills.manage.count.all', { count: String(allSkills.length) })} active />
      <Chip label={t('skills.manage.count.system', { count: String(systemCount) })} />
      <Chip label={t('skills.manage.count.personal', { count: String(personalCount) })} />
      <div style={{ flex: 1 }} />
      {savedSkillName && <span style={savedHint}>{t('settings.savedToast')}</span>}
      <CatalogSearchBox
        value={query}
        placeholder={t('skills.manage.searchPlaceholder')}
        onChange={onQueryChange}
        style={{ maxWidth: '280px', flex: '0 1 280px' }}
      />
    </div>
  )
}

export function SkillsManageList({
  skills,
  loading,
  error,
  onToggleEnabled
}: {
  skills: SkillEntry[]
  loading: boolean
  error: string | null
  onToggleEnabled: (skill: SkillEntry, enabled: boolean) => void
}): JSX.Element {
  const t = useT()

  return (
    <main style={manageMain}>
      {loading && (
        <SkeletonList
          count={5}
          ariaLabel={t('skills.loading')}
          rowProps={{ media: 38, mediaRadius: 8, lines: ['46%', '30%'] }}
          rowStyle={{ maxWidth: '730px', margin: '0 auto', minHeight: '74px' }}
        />
      )}
      {error && <p style={{ ...emptyText, color: 'var(--error)' }} role="alert">{error}</p>}
      {!loading && !error && skills.map((skill) => (
        <div key={skill.name} style={manageRow}>
          <SkillAvatar
            name={skill.name}
            displayName={skillTitle(skill)}
            size={38}
            iconDataUrl={skill.iconSmallDataUrl}
          />
          <div style={{ minWidth: 0, flex: 1 }}>
            <div style={rowTitleLine}>
              <div style={rowTitle}>{skillTitle(skill)}</div>
              {skill.hasVariant ? <VariantBadge compact /> : null}
            </div>
            <div style={rowDesc}>{skillSubtitle(skill, t)}</div>
          </div>
          <span style={manageSource}>{sourceLabel(skill, t)}</span>
          <PillSwitch
            checked={skill.enabled}
            onChange={(enabled) => onToggleEnabled(skill, enabled)}
            size="sm"
            aria-label={skill.enabled ? t('skillCard.toggleDisable') : t('skillCard.toggleEnable')}
          />
        </div>
      ))}
    </main>
  )
}

function LocalSkillItem({ skill, onOpen }: { skill: SkillEntry; onOpen: () => void }): JSX.Element {
  const t = useT()
  return (
    <button type="button" onClick={onOpen} style={compactItem}>
      <SkillAvatar
        name={skill.name}
        displayName={skillTitle(skill)}
        size={40}
        iconDataUrl={skill.iconSmallDataUrl}
      />
      <div style={{ minWidth: 0, flex: 1 }}>
        <div style={rowTitleLine}>
          <div style={rowTitle}>{skillTitle(skill)}</div>
          {skill.hasVariant ? <VariantBadge compact /> : null}
        </div>
        <div style={rowDesc}>{skillSubtitle(skill, t)}</div>
      </div>
      <span title={skill.enabled ? t('skillCard.on') : t('skillCard.disabledBadge')} style={statusIcon}>
        {skill.enabled ? <Check size={16} aria-hidden /> : t('skillCard.disabledBadge')}
      </span>
    </button>
  )
}

function MarketSkillItem({ skill, onOpen }: { skill: MarketSkillSummary; onOpen: () => void }): JSX.Element {
  const t = useT()
  return (
    <button type="button" onClick={onOpen} style={compactItem}>
      <SkillAvatar name={skill.name} size={40} />
      <div style={{ minWidth: 0, flex: 1 }}>
        <div style={rowTitle}>{skill.name}</div>
        <div style={rowDesc}>{skill.description || skill.slug}</div>
      </div>
      <span style={marketAction(skill)}>
        {skill.updateAvailable
          ? t('skillMarket.updateAvailable')
          : skill.installed
            ? t('skillMarket.installed')
            : <Plus size={16} aria-hidden />}
      </span>
    </button>
  )
}

type TranslateFn = (key: string, vars?: Record<string, string | number>) => string

function dotCraftInstallDisabledReason(
  connectionStatus: string,
  capabilities: ServerCapabilities | null,
  selfLearningEnabled: boolean,
  remoteWorkspaceActive: boolean,
  t: TranslateFn
): string | null {
  if (remoteWorkspaceActive) return t('skillMarket.dotCraftInstallUnavailableRemote')
  if (connectionStatus !== 'connected') return t('skillMarket.dotCraftInstallUnavailableDisconnected')
  if (capabilities?.skillsManagement !== true) return t('skillMarket.dotCraftInstallUnavailableSkills')
  if (capabilities?.skillVariants !== true) return t('skillMarket.dotCraftInstallUnavailableVariants')
  if (selfLearningEnabled === false) return t('skillMarket.dotCraftInstallUnavailableSelfLearning')
  return null
}

function buildDotCraftInstallPrompt(
  skill: MarketSkillDetail,
  preparation: MarketDotCraftInstallPreparation,
  t: TranslateFn
): string {
  return t('skillMarket.dotCraftInstallPrompt', {
    name: skill.name,
    skillName: preparation.skillName,
    provider: providerLabel(preparation.provider),
    slug: preparation.slug,
    version: preparation.version || t('skillMarket.versionUnknown'),
    candidateDir: preparation.candidateDir,
    metadataPath: preparation.metadataPath,
    workspacePath: preparation.workspacePath,
    sourceUrl: preparation.sourceUrl || skill.sourceUrl || ''
  })
}

async function startDotCraftInstallThread(
  skill: MarketSkillDetail,
  preparation: MarketDotCraftInstallPreparation,
  t: TranslateFn,
  addThread: (thread: ThreadSummary) => void,
  setActiveThreadId: (id: string | null) => void
): Promise<void> {
  const prompt = buildDotCraftInstallPrompt(skill, preparation, t)
  const visibleText = `$skill-installer\n\n${prompt}`
  const result = await window.api.appServer.sendRequest('thread/start', {
    identity: {
      channelName: 'dotcraft-desktop',
      userId: 'local',
      channelContext: `workspace:${preparation.workspacePath}`,
      workspacePath: preparation.workspacePath
    },
    historyMode: 'server'
  }) as { thread: ThreadSummary }

  await window.api.skillMarket.bindDotCraftInstall?.({
    threadId: result.thread.id,
    stagingDir: preparation.stagingDir
  })

  useUIStore.getState().setPendingWelcomeTurn({
    threadId: result.thread.id,
    text: visibleText,
    inputParts: [
      { type: 'skillRef', name: 'skill-installer' },
      { type: 'text', text: `\n\n${prompt}` }
    ],
    mode: 'agent',
    approvalPolicy: 'default',
    model: ''
  })
  addThread(result.thread)
  setActiveThreadId(result.thread.id)
  useUIStore.getState().setActiveMainView('conversation')
}

function MarketSkillDetailDialog({
  skill,
  loading,
  installing,
  dotCraftInstalling,
  dotCraftDisabledReason,
  installDisabledReason,
  onClose,
  onInstall,
  onDotCraftInstall
}: {
  skill: MarketSkillDetail
  loading: boolean
  installing: boolean
  dotCraftInstalling: boolean
  dotCraftDisabledReason: string | null
  installDisabledReason: string | null
  onClose: () => void
  onInstall: () => void
  onDotCraftInstall: () => void
}): JSX.Element {
  const t = useT()

  useEffect(() => {
    function onKey(event: KeyboardEvent): void {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const installLabel = skill.updateAvailable
    ? t('skillMarket.update')
    : skill.installed
      ? t('skillMarket.reinstall')
      : t('skillMarket.install')
  const dotCraftInstallLabel = skill.updateAvailable
    ? t('skillMarket.updateWithDotCraft')
    : skill.installed
      ? t('skillMarket.checkWithDotCraft')
      : t('skillMarket.installWithDotCraft')
  const dotCraftButtonDisabled = Boolean(dotCraftDisabledReason) || dotCraftInstalling || loading || installing
  const installButtonDisabled = Boolean(installDisabledReason) || installing || loading

  return (
    <div role="presentation" style={modalScrim} onClick={onClose}>
      <div role="dialog" aria-modal aria-labelledby="skill-market-detail-title" style={modal} onClick={(e) => e.stopPropagation()}>
        <header style={modalHeader}>
          <div style={{ minWidth: 0 }}>
            <h2 id="skill-market-detail-title" style={modalTitle}>{skill.name}</h2>
            <p style={modalSubtitle}>{skill.description || skill.slug}</p>
          </div>
          <button type="button" onClick={onClose} style={iconCloseBtn} aria-label={t('skillDetail.close')}>×</button>
        </header>
        <div style={metaRow}>
          <Meta label={t('skillMarket.provider')} value={providerLabel(skill.provider)} />
          <Meta label={t('skillMarket.version')} value={skill.version || t('skillMarket.versionUnknown')} />
          {skill.author && <Meta label={t('skillMarket.author')} value={skill.author} />}
          {skill.downloads != null && <Meta label={t('skillMarket.downloadsLabel')} value={String(skill.downloads)} />}
        </div>
        <div style={modalBody}>
          {loading ? (
            <p style={emptyText}>{t('skillDetail.loading')}</p>
          ) : skill.readme ? (
            <MarkdownRenderer content={stripYamlFrontmatter(skill.readme)} linkMode="external" />
          ) : skill.description ? (
            <p style={previewFallbackText}>{skill.description}</p>
          ) : (
            <p style={emptyText}>{t('skillMarket.noReadme')}</p>
          )}
        </div>
        <footer style={modalFooter}>
          <button
            type="button"
            onClick={() => {
              if (skill.sourceUrl) void window.api.shell.openExternal(skill.sourceUrl)
            }}
            disabled={!skill.sourceUrl}
            style={!skill.sourceUrl ? disabledSecondaryBtn : secondaryBtn}
          >
            <ExternalLink size={14} aria-hidden />
            {t('skillMarket.openSource')}
          </button>
          <ActionTooltip
            label={installLabel}
            disabledReason={installDisabledReason ?? undefined}
            placement="top"
          >
            <button type="button" onClick={onInstall} disabled={installButtonDisabled} style={installButtonDisabled ? disabledSecondaryBtn : secondaryBtn}>
              <Download size={14} aria-hidden />
              {installing ? t('skillMarket.installing') : installLabel}
            </button>
          </ActionTooltip>
          <ActionTooltip
            label={dotCraftInstallLabel}
            disabledReason={dotCraftDisabledReason ?? undefined}
            placement="top"
          >
            <button
              type="button"
              onClick={onDotCraftInstall}
              disabled={dotCraftButtonDisabled}
              style={dotCraftButtonDisabled ? disabledPrimaryBtn : primaryBtn}
            >
              <Sparkles size={14} aria-hidden />
              {dotCraftInstalling ? t('skillMarket.dotCraftInstallPreparing') : dotCraftInstallLabel}
            </button>
          </ActionTooltip>
        </footer>
      </div>
    </div>
  )
}

function SkillSection({ title, children }: { title: string; children: React.ReactNode }): JSX.Element {
  return (
    <section style={{ marginBottom: '34px' }}>
      <h2 style={sectionTitle}>{title}</h2>
      {children}
    </section>
  )
}

function CompactGrid({ children }: { children: React.ReactNode }): JSX.Element {
  return <div style={compactGrid}>{children}</div>
}

function Chip({ label, active = false }: { label: string; active?: boolean }): JSX.Element {
  return <span style={active ? chipActive : chip}>{label}</span>
}

function Meta({ label, value }: { label: string; value: string }): JSX.Element {
  return (
    <div style={metaItem}>
      <span style={metaLabel}>{label}</span>
      <span style={metaValue}>{value}</span>
    </div>
  )
}

export function filterLocalSkills(skills: SkillEntry[], query: string, filter: SourceFilter): SkillEntry[] {
  const q = query.trim().toLowerCase()
  return skills.filter((skill) => {
    if (filter === 'system' && skill.source !== 'builtin') return false
    if (filter === 'personal' && skill.source === 'builtin') return false
    if (filter === 'market') return false
    if (!q) return true
    return (
      skill.name.toLowerCase().includes(q) ||
      (skill.displayName ?? '').toLowerCase().includes(q) ||
      (skill.description ?? '').toLowerCase().includes(q) ||
      (skill.shortDescription ?? '').toLowerCase().includes(q)
    )
  })
}

function skillTitle(skill: SkillEntry): string {
  return skill.displayName || skill.name
}

function skillSubtitle(skill: SkillEntry, t: ReturnType<typeof useT>): string {
  return skill.shortDescription || skill.description || t('skillCard.noDescription')
}

function sourceLabel(skill: SkillEntry, t: ReturnType<typeof useT>): string {
  if (skill.source === 'builtin') return t('skills.source.system')
  if (skill.source === 'workspace') return t('skills.source.workspace')
  if (skill.source === 'plugin') return skill.pluginDisplayName || t('plugins.source.plugin')
  return t('skills.source.user')
}

function isUninstallableSkill(skill: SkillEntry): boolean {
  return skill.source === 'workspace' || skill.source === 'user'
}

function providerLabel(provider: SkillMarketProviderFilter): string {
  if (provider === 'skillhub') return 'SkillHub'
  if (provider === 'clawhub') return 'ClawHub'
  return 'All'
}

function stripYamlFrontmatter(s: string): string {
  if (!s.startsWith('---')) return s
  const m = s.match(/^---\r?\n[\s\S]*?\r?\n---\r?\n/)
  return m ? s.slice(m[0].length).trim() : s
}

const page: React.CSSProperties = catalogStyles.page
const browseHeader: React.CSSProperties = catalogStyles.browseHeader
const topActions: React.CSSProperties = catalogStyles.topActions
const heroTitle: React.CSSProperties = catalogStyles.heroTitle
const searchRow: React.CSSProperties = catalogStyles.searchRow
const browseMain: React.CSSProperties = catalogStyles.browseMain
const sectionTitle: React.CSSProperties = catalogStyles.sectionTitle
const compactGrid: React.CSSProperties = catalogStyles.compactGrid
const compactItem: React.CSSProperties = catalogStyles.compactItem
const rowTitle: React.CSSProperties = catalogStyles.rowTitle
const rowTitleLine: React.CSSProperties = catalogStyles.rowTitleLine
const rowDesc: React.CSSProperties = catalogStyles.rowDesc
const statusIcon: React.CSSProperties = catalogStyles.statusIcon

function marketAction(skill: MarketSkillSummary): React.CSSProperties {
  return {
    ...statusIcon,
    color: skill.updateAvailable ? 'var(--warning)' : skill.installed ? 'var(--success)' : 'var(--text-primary)'
  }
}

const manageButton: React.CSSProperties = catalogStyles.manageButton
const manageButtonLabel: React.CSSProperties = {
  lineHeight: 1,
  transform: 'translateY(-1px)'
}
const iconButton: React.CSSProperties = catalogStyles.iconButton
const manageHeader: React.CSSProperties = catalogStyles.manageHeader
const breadcrumb: React.CSSProperties = catalogStyles.breadcrumb
const breadcrumbButton: React.CSSProperties = catalogStyles.breadcrumbButton
const breadcrumbSep: React.CSSProperties = catalogStyles.breadcrumbSep
const breadcrumbCurrent: React.CSSProperties = catalogStyles.breadcrumbCurrent
const manageToolbar: React.CSSProperties = catalogStyles.manageToolbar
const chip: React.CSSProperties = catalogStyles.chip
const chipActive: React.CSSProperties = catalogStyles.chipActive
const savedHint: React.CSSProperties = catalogStyles.savedHint
const manageMain: React.CSSProperties = catalogStyles.manageMain
const manageRow: React.CSSProperties = catalogStyles.manageRow

const manageSource: React.CSSProperties = {
  width: '72px',
  color: 'var(--text-secondary)',
  fontSize: '13px',
  textAlign: 'left'
}

const emptyText: React.CSSProperties = catalogStyles.emptyText

const modalScrim: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 1000,
  backgroundColor: 'rgba(0,0,0,0.55)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: '24px'
}

const modal: React.CSSProperties = {
  width: 'min(760px, 100%)',
  maxHeight: 'min(86vh, 920px)',
  backgroundColor: 'var(--bg-primary)',
  borderRadius: '12px',
  border: '1px solid var(--border-default)',
  boxShadow: '0 16px 48px rgba(0,0,0,0.45)',
  display: 'flex',
  flexDirection: 'column',
  overflow: 'hidden'
}

const modalHeader: React.CSSProperties = {
  padding: '16px 20px',
  borderBottom: '1px solid var(--border-default)',
  display: 'flex',
  alignItems: 'flex-start',
  justifyContent: 'space-between',
  gap: '12px',
  flexShrink: 0
}

const modalTitle: React.CSSProperties = {
  margin: 0,
  fontSize: '18px',
  fontWeight: 600,
  color: 'var(--text-primary)',
  wordBreak: 'break-word'
}

const modalSubtitle: React.CSSProperties = {
  margin: '8px 0 0',
  color: 'var(--text-secondary)',
  fontSize: '13px',
  lineHeight: 1.45
}

const metaRow: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(auto-fit, minmax(120px, 1fr))',
  gap: '10px',
  padding: '12px 20px',
  borderBottom: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-secondary)'
}

const metaItem: React.CSSProperties = {
  minWidth: 0
}

const metaLabel: React.CSSProperties = {
  display: 'block',
  fontSize: '11px',
  color: 'var(--text-dimmed)',
  textTransform: 'uppercase',
  marginBottom: '3px'
}

const metaValue: React.CSSProperties = {
  display: 'block',
  fontSize: '13px',
  color: 'var(--text-primary)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const modalBody: React.CSSProperties = {
  flex: 1,
  overflow: 'auto',
  padding: '16px 20px',
  minHeight: '220px'
}

const previewFallbackText: React.CSSProperties = {
  margin: 0,
  fontSize: '14px',
  lineHeight: 1.6,
  color: 'var(--text-secondary)',
  whiteSpace: 'pre-wrap'
}

const modalFooter: React.CSSProperties = {
  padding: '12px 20px',
  borderTop: '1px solid var(--border-default)',
  display: 'flex',
  justifyContent: 'flex-end',
  alignItems: 'center',
  gap: '8px',
  flexWrap: 'wrap'
}

const secondaryBtn: React.CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '6px',
  padding: '7px 12px',
  fontSize: '13px',
  borderRadius: '6px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-tertiary)',
  color: 'var(--text-primary)',
  cursor: 'pointer'
}

const primaryBtn: React.CSSProperties = {
  ...secondaryBtn,
  backgroundColor: 'var(--accent)',
  borderColor: 'var(--accent)',
  color: 'var(--on-accent)'
}

const disabledSecondaryBtn: React.CSSProperties = {
  ...secondaryBtn,
  opacity: 0.55,
  cursor: 'not-allowed'
}

const disabledPrimaryBtn: React.CSSProperties = {
  ...primaryBtn,
  opacity: 0.65,
  cursor: 'not-allowed'
}

const iconCloseBtn: React.CSSProperties = {
  width: '32px',
  height: '32px',
  fontSize: '22px',
  lineHeight: 1,
  borderRadius: '6px',
  border: 'none',
  backgroundColor: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  flexShrink: 0
}
