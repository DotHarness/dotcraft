import { useEffect } from 'react'
import { Download, ExternalLink, Search } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import {
  useSkillMarketStore,
  type SkillMarketProviderFilter
} from '../../stores/skillMarketStore'
import type { MarketSkillDetail, MarketSkillSummary, SkillMarketProviderId } from '../../../shared/skillMarket'
import { addToast } from '../../stores/toastStore'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import { useConfirmDialog } from '../ui/ConfirmDialog'

interface SkillMarketplaceViewProps {
  onInstalled: () => Promise<void>
}

export function SkillMarketplaceView({ onInstalled }: SkillMarketplaceViewProps): JSX.Element {
  const t = useT()
  const {
    query,
    provider,
    results,
    loading,
    error,
    selectedSkill,
    detailLoading,
    installSlug,
    setQuery,
    setProvider,
    search,
    selectSkill,
    clearSelection,
    installSelected
  } = useSkillMarketStore()
  const confirm = useConfirmDialog()

  useEffect(() => {
    if (!query.trim()) {
      void search()
      return
    }
    const timer = window.setTimeout(() => void search(), 350)
    return () => window.clearTimeout(timer)
  }, [query, provider, search])

  async function handleInstall(overwrite = false): Promise<void> {
    const skill = selectedSkill
    if (!skill) return
    if ((skill.installed || skill.updateAvailable) && !overwrite) {
      const ok = await confirm({
        title: t('skillMarket.overwriteTitle'),
        message: t('skillMarket.overwriteMessage', { name: skill.name }),
        confirmLabel: skill.updateAvailable ? t('skillMarket.update') : t('skillMarket.reinstall'),
        cancelLabel: t('common.cancel')
      })
      if (!ok) return
      await handleInstall(true)
      return
    }
    try {
      await installSelected(overwrite)
      await onInstalled()
      addToast(t('skillMarket.installSuccess', { name: skill.name }), 'success')
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      addToast(t('skillMarket.installFailed', { error: msg }), 'error')
    }
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
      <form
        onSubmit={(event) => {
          event.preventDefault()
          void search()
        }}
        style={marketToolbar}
      >
        <div style={searchWrap}>
          <Search size={15} aria-hidden="true" />
          <input
            type="search"
            placeholder={t('skillMarket.searchPlaceholder')}
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            style={searchInput}
          />
        </div>
        <div role="tablist" aria-label={t('skillMarket.provider')} style={providerTabs}>
          {(['all', 'skillhub', 'clawhub'] satisfies SkillMarketProviderFilter[]).map((id) => (
            <button
              key={id}
              type="button"
              onClick={() => setProvider(id)}
              aria-pressed={provider === id}
              style={provider === id ? providerTabActive : providerTab}
            >
              {id === 'all' ? t('skillMarket.provider.all') : providerLabel(id)}
            </button>
          ))}
        </div>
      </form>

      {!query.trim() && (
        <p style={emptyText}>{t('skillMarket.emptyPrompt')}</p>
      )}
      {loading && <p style={emptyText}>{t('skillMarket.loading')}</p>}
      {error && (
        <p style={{ ...emptyText, color: 'var(--error)' }} role="alert">
          {error}
        </p>
      )}
      {!loading && !error && query.trim() && results.length === 0 && (
        <p style={emptyText}>{t('skillMarket.noResults')}</p>
      )}

      {results.length > 0 && (
        <div style={resultGrid}>
          {results.map((skill) => (
            <MarketSkillCard
              key={`${skill.provider}:${skill.slug}`}
              skill={skill}
              onOpen={() => void selectSkill(skill)}
            />
          ))}
        </div>
      )}

      {selectedSkill && (
        <MarketSkillDetailDialog
          skill={selectedSkill}
          loading={detailLoading}
          installing={installSlug === selectedSkill.slug}
          onClose={clearSelection}
          onInstall={() => void handleInstall(false)}
        />
      )}
    </div>
  )
}

function MarketSkillCard({ skill, onOpen }: { skill: MarketSkillSummary; onOpen: () => void }): JSX.Element {
  const t = useT()
  return (
    <button type="button" onClick={onOpen} style={cardBtn}>
      <div style={cardHeader}>
        <div style={{ minWidth: 0 }}>
          <h3 style={cardTitle}>{skill.name}</h3>
          <p style={cardSlug}>{skill.slug}</p>
        </div>
        <span style={providerBadge(skill.provider)}>{providerLabel(skill.provider)}</span>
      </div>
      <p style={cardDescription}>{skill.description || t('skillCard.noDescription')}</p>
      <div style={cardFooter}>
        <span>{skill.version ? `v${skill.version}` : t('skillMarket.versionUnknown')}</span>
        {skill.downloads != null && <span>{t('skillMarket.downloads', { count: String(skill.downloads) })}</span>}
        {skill.installed && (
          <span style={skill.updateAvailable ? updateBadge : installedBadge}>
            {skill.updateAvailable ? t('skillMarket.updateAvailable') : t('skillMarket.installed')}
          </span>
        )}
      </div>
    </button>
  )
}

function MarketSkillDetailDialog({
  skill,
  loading,
  installing,
  onClose,
  onInstall
}: {
  skill: MarketSkillDetail
  loading: boolean
  installing: boolean
  onClose: () => void
  onInstall: () => void
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

  return (
    <div role="presentation" style={modalScrim} onClick={onClose}>
      <div role="dialog" aria-modal aria-labelledby="skill-market-detail-title" style={modal} onClick={(e) => e.stopPropagation()}>
        <header style={modalHeader}>
          <div style={{ minWidth: 0 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
              <h2 id="skill-market-detail-title" style={modalTitle}>{skill.name}</h2>
              <span style={providerBadge(skill.provider)}>{providerLabel(skill.provider)}</span>
              {skill.installed && (
                <span style={skill.updateAvailable ? updateBadge : installedBadge}>
                  {skill.updateAvailable ? t('skillMarket.updateAvailable') : t('skillMarket.installed')}
                </span>
              )}
            </div>
            <p style={modalSubtitle}>{skill.description || skill.slug}</p>
          </div>
          <button type="button" onClick={onClose} style={iconCloseBtn} aria-label={t('skillDetail.close')}>
            ×
          </button>
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
            <ExternalLink size={14} aria-hidden="true" />
            {t('skillMarket.openSource')}
          </button>
          <button type="button" onClick={onInstall} disabled={installing || loading} style={installing || loading ? disabledPrimaryBtn : primaryBtn}>
            <Download size={14} aria-hidden="true" />
            {installing ? t('skillMarket.installing') : installLabel}
          </button>
        </footer>
      </div>
    </div>
  )
}

function Meta({ label, value }: { label: string; value: string }): JSX.Element {
  return (
    <div style={metaItem}>
      <span style={metaLabel}>{label}</span>
      <span style={metaValue}>{value}</span>
    </div>
  )
}

function providerLabel(provider: SkillMarketProviderFilter): string {
  if (provider === 'skillhub') return 'SkillHub'
  if (provider === 'clawhub') return 'ClawHub'
  return 'All'
}

function providerBadge(provider: SkillMarketProviderId): React.CSSProperties {
  return {
    ...badge,
    borderColor: provider === 'skillhub' ? 'rgba(14, 165, 233, 0.45)' : 'rgba(34, 197, 94, 0.45)',
    color: provider === 'skillhub' ? '#38bdf8' : '#4ade80'
  }
}

function stripYamlFrontmatter(s: string): string {
  if (!s.startsWith('---')) return s
  const m = s.match(/^---\r?\n[\s\S]*?\r?\n---\r?\n/)
  return m ? s.slice(m[0].length).trim() : s
}

const marketToolbar: React.CSSProperties = {
  display: 'flex',
  gap: '10px',
  alignItems: 'center',
  justifyContent: 'space-between',
  flexWrap: 'wrap'
}

const searchWrap: React.CSSProperties = {
  flex: '1 1 280px',
  minWidth: 0,
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  padding: '8px 12px',
  borderRadius: '6px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-secondary)',
  color: 'var(--text-secondary)'
}

const searchInput: React.CSSProperties = {
  width: '100%',
  minWidth: 0,
  border: 'none',
  outline: 'none',
  backgroundColor: 'transparent',
  color: 'var(--text-primary)',
  fontSize: '13px'
}

const providerTabs: React.CSSProperties = {
  display: 'inline-flex',
  padding: '3px',
  borderRadius: '7px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-secondary)'
}

const providerTab: React.CSSProperties = {
  padding: '6px 10px',
  borderRadius: '5px',
  border: 'none',
  backgroundColor: 'transparent',
  color: 'var(--text-secondary)',
  fontSize: '12px',
  cursor: 'pointer'
}

const providerTabActive: React.CSSProperties = {
  ...providerTab,
  backgroundColor: 'var(--bg-tertiary)',
  color: 'var(--text-primary)'
}

const resultGrid: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))',
  gap: '12px'
}

const cardBtn: React.CSSProperties = {
  width: '100%',
  minHeight: '154px',
  textAlign: 'left',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-secondary)',
  padding: '14px',
  cursor: 'pointer',
  display: 'flex',
  flexDirection: 'column',
  gap: '10px'
}

const cardHeader: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  gap: '10px',
  alignItems: 'flex-start'
}

const cardTitle: React.CSSProperties = {
  margin: 0,
  fontSize: '15px',
  lineHeight: 1.25,
  color: 'var(--text-primary)',
  fontWeight: 600,
  wordBreak: 'break-word'
}

const cardSlug: React.CSSProperties = {
  margin: '4px 0 0',
  fontSize: '12px',
  color: 'var(--text-dimmed)',
  wordBreak: 'break-word'
}

const cardDescription: React.CSSProperties = {
  margin: 0,
  fontSize: '13px',
  lineHeight: 1.45,
  color: 'var(--text-secondary)',
  display: '-webkit-box',
  WebkitLineClamp: 3,
  WebkitBoxOrient: 'vertical',
  overflow: 'hidden',
  flex: 1
}

const cardFooter: React.CSSProperties = {
  display: 'flex',
  gap: '8px',
  flexWrap: 'wrap',
  alignItems: 'center',
  fontSize: '12px',
  color: 'var(--text-dimmed)'
}

const badge: React.CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: '3px 7px',
  borderRadius: '999px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-primary)',
  fontSize: '11px',
  lineHeight: 1.2,
  whiteSpace: 'nowrap'
}

const installedBadge: React.CSSProperties = {
  ...badge,
  color: 'var(--success)',
  borderColor: 'rgba(34, 197, 94, 0.45)'
}

const updateBadge: React.CSSProperties = {
  ...badge,
  color: 'var(--warning)',
  borderColor: 'rgba(245, 158, 11, 0.55)'
}

const emptyText: React.CSSProperties = {
  margin: 0,
  fontSize: '13px',
  color: 'var(--text-secondary)'
}

const previewFallbackText: React.CSSProperties = {
  margin: 0,
  fontSize: '14px',
  lineHeight: 1.6,
  color: 'var(--text-secondary)',
  whiteSpace: 'pre-wrap'
}

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
  letterSpacing: '0.04em',
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
