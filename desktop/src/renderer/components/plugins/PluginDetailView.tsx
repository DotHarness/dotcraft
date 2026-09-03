import { useState } from 'react'
import type { CSSProperties, MouseEvent } from 'react'
import { Anchor, Box, Code2, Ellipsis, ExternalLink, Link, MessageCircle, Plus, Server, Settings, Trash2, Wrench } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import type { PluginEntry } from '../../stores/pluginStore'
import { getPluginContentSummaries, type PluginContentType } from '../../utils/pluginContentSummaries'
import {
  CatalogBreadcrumb,
  CatalogHoverButton,
  CatalogTopBar,
  styles as catalogStyles
} from '../catalog/CatalogSurface'
import { ActionTooltip } from '../ui/ActionTooltip'
import { Button } from '../ui/Button'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { IconButton } from '../ui/IconButton'
import { AppBindingPanel } from './AppBindingPanel'
import { MorphingActionPill } from './MorphingActionPill'
import { PluginIcon, pluginSubtitle, pluginTitle } from './PluginCatalogItem'
import { displayCategory } from './pluginCatalogModel'
import styles from './PluginDetailView.module.css'

const DOTCRAFT_PLUGIN_FALLBACK_URL = 'https://github.com/DotHarness/dotcraft'

export function PluginDetailView({
  plugin,
  loading,
  onBack,
  onManage,
  onInstall,
  installing,
  onRemove,
  enabling,
  onEnable,
  enablingLsp,
  onEnableLsp,
  onTryInChat,
  onOpenSkill
}: {
  plugin: PluginEntry
  loading: boolean
  onBack: () => void
  onManage: () => void
  onInstall: () => void
  installing: boolean
  onRemove: () => void
  enabling: boolean
  onEnable: () => void
  enablingLsp: boolean
  onEnableLsp: () => void
  onTryInChat: () => void
  onOpenSkill: (skillName: string) => void
}): JSX.Element {
  const t = useT()
  const [detailMenuPosition, setDetailMenuPosition] = useState<ContextMenuPosition | null>(null)
  const info = plugin.interface
  const shouldOfferLspEnable = plugin.installed
    && plugin.enabled
    && (plugin.lspServers ?? []).some((server) => server.enabled && !server.active && !server.shadowedBy)
  const contents = getPluginContentSummaries(plugin, t)
  return (
    <div style={page}>
      <CatalogTopBar
        navigation={(
          <CatalogBreadcrumb
            parentLabel={t('plugins.pageTitle')}
            currentLabel={pluginTitle(plugin)}
            onBack={onBack}
          />
        )}
      />
      <header style={detailHeader}>
        <div style={detailIconRow}>
          <PluginIcon plugin={plugin} role="hero" />
        </div>
        <div style={detailIdentityRow}>
          <div style={detailIdentity}>
            <h1 style={detailTitle}>{pluginTitle(plugin)}</h1>
            <p style={detailSubtitle}>{pluginSubtitle(plugin)}</p>
          </div>
          <div style={detailActions}>
            <ActionTooltip label={t('plugins.detail.website')}>
              <a
                href={resolvePluginExternalUrl(info?.websiteUrl) ?? DOTCRAFT_PLUGIN_FALLBACK_URL}
                style={detailIconButton}
                aria-label={t('plugins.detail.website')}
                onClick={(event) => handlePluginExternalLinkClick(event, info?.websiteUrl)}
              >
                <Link size={15} aria-hidden />
              </a>
            </ActionTooltip>
            {plugin.installed && (
              <IconButton
                icon={<Ellipsis size={16} aria-hidden />}
                label={t('plugins.moreActions')}
                onClick={(event) => {
                  event.stopPropagation()
                  const rect = event.currentTarget.getBoundingClientRect()
                  setDetailMenuPosition({ x: rect.right - 200, y: rect.bottom + 4 })
                }}
              />
            )}
            <PluginPrimaryAction
              installed={plugin.installed}
              enabled={plugin.enabled}
              installing={installing}
              enabling={enabling}
              onInstall={onInstall}
              onEnable={onEnable}
              onTryInChat={onTryInChat}
            />
          </div>
        </div>
      </header>
      <main className="dc-scrollbar-stable" style={detailMain}>
        <div style={detailContent}>
          {loading && <p style={emptyText}>{t('plugins.loading')}</p>}
          <div className={styles.promptPreview} style={promptPreview} data-plugin-prompt-preview>
            <span className={styles.promptBubble} style={promptBubble}>
              <span style={promptBubblePrefix}>
                <PluginIcon plugin={plugin} role="compact" size={18} />
                <strong style={promptBubbleTitle}>{pluginTitle(plugin)}</strong>
              </span>
              <span style={promptBubbleText}>{info?.defaultPrompt || t('plugins.defaultPromptFallback')}</span>
            </span>
          </div>
          <p style={longDescription}>{info?.longDescription || plugin.description}</p>
          <AppBindingPanel plugin={plugin} />
          <section style={detailSection}>
            <h2 style={detailSectionTitle}>{t('plugins.detail.contents')}</h2>
            {contents.length > 0 ? (
              <div style={contentList}>
                {contents.map((item) => {
                  const body = (
                    <>
                      <span style={contentIcon}>
                        <PluginContentIcon type={item.type} size={16} />
                      </span>
                      <span style={pluginText}>
                        <span style={contentTitleLine}>
                          <strong style={rowTitle}>{item.title}</strong>
                          <span style={contentKind}>{item.kind}</span>
                        </span>
                        <span style={rowDesc}>{item.description}</span>
                      </span>
                    </>
                  )
                  // Only a skill has a document to preview; the other kinds are
                  // descriptions of runtime wiring with nothing to open.
                  if (item.skillName == null) {
                    return <div key={item.key} style={contentItem}>{body}</div>
                  }
                  return (
                    <CatalogHoverButton
                      key={item.key}
                      type="button"
                      baseStyle={contentItemButton}
                      onClick={() => onOpenSkill(item.skillName!)}
                    >
                      {body}
                    </CatalogHoverButton>
                  )
                })}
              </div>
            ) : (
              <p style={emptyText}>{t('plugins.detail.noContents')}</p>
            )}
          </section>
          {shouldOfferLspEnable && (
            <div style={lspEnablePanel} role="status">
              <span style={rowDesc}>{t('plugins.lsp.enablePrompt')}</span>
              <Button disabled={enablingLsp} onClick={onEnableLsp} iconLeft={<Code2 size={14} />}>
                {enablingLsp ? t('plugins.lsp.enabling') : t('plugins.lsp.enable')}
              </Button>
            </div>
          )}
          <section style={detailSection}>
            <h2 style={detailSectionTitle}>{t('plugins.detail.info')}</h2>
            <div style={infoTable}>
              <InfoRow label={t('plugins.detail.category')} value={[displayCategory(info?.category, t), info?.developerName].filter(Boolean).join(', ')} />
              <InfoRow label={t('plugins.detail.capabilities')} value={(info?.capabilities ?? []).join(', ')} />
              <InfoRow label={t('plugins.detail.developer')} value={info?.developerName || 'DotHarness'} />
              <InfoLinkRow label={t('plugins.detail.website')} href={info?.websiteUrl} />
              <InfoLinkRow label={t('plugins.detail.privacy')} href={info?.privacyPolicyUrl} />
              <InfoLinkRow label={t('plugins.detail.terms')} href={info?.termsOfServiceUrl} />
            </div>
          </section>
        </div>
      </main>
      {detailMenuPosition && (
        <ContextMenu
          position={detailMenuPosition}
          onClose={() => setDetailMenuPosition(null)}
          items={[
            {
              label: t('plugins.manage'),
              icon: <Settings size={14} />,
              onClick: onManage
            },
            ...(plugin.removable
              ? [{
                  label: t('plugins.uninstall'),
                  icon: <Trash2 size={14} />,
                  danger: true,
                  onClick: onRemove
                }]
              : [])
          ]}
        />
      )}
    </div>
  )
}

function PluginPrimaryAction({
  installed,
  enabled,
  installing,
  enabling,
  onInstall,
  onEnable,
  onTryInChat
}: {
  installed: boolean
  enabled: boolean
  installing: boolean
  enabling: boolean
  onInstall: () => void
  onEnable: () => void
  onTryInChat: () => void
}): JSX.Element {
  const t = useT()
  const pending = installing || enabling
  const label = installing
    ? t('plugins.installing')
    : enabling
      ? t('plugins.enabling')
      : !installed
        ? t('plugins.install')
        : enabled
          ? t('plugins.tryInChat')
          : t('plugins.enable')
  const showInstallIcon = !installed && !pending
  const showTryIcon = installed && enabled && !pending

  return (
    <MorphingActionPill
      label={label}
      loading={pending}
      iconLeft={showInstallIcon ? <Plus size={14} /> : showTryIcon ? <MessageCircle size={14} /> : undefined}
      onClick={!installed ? onInstall : enabled ? onTryInChat : onEnable}
    />
  )
}

function InfoRow({ label, value }: { label: string; value?: string | null }): JSX.Element {
  return (
    <div style={infoRow}>
      <span style={infoLabel}>{label}</span>
      <span style={infoValue}>{value || '-'}</span>
    </div>
  )
}

function InfoLinkRow({ label, href }: { label: string; href?: string | null }): JSX.Element {
  const resolvedHref = resolvePluginExternalUrl(href) ?? DOTCRAFT_PLUGIN_FALLBACK_URL
  return (
    <div style={infoRow}>
      <span style={infoLabel}>{label}</span>
      <span style={infoValue}>
        <ActionTooltip label={label}>
          <a
            href={resolvedHref}
            style={plainLink}
            aria-label={label}
            onClick={(event) => handlePluginExternalLinkClick(event, href)}
          >
            <ExternalLink size={14} aria-hidden />
          </a>
        </ActionTooltip>
      </span>
    </div>
  )
}

function handlePluginExternalLinkClick(event: MouseEvent<HTMLAnchorElement>, href?: string | null): void {
  event.preventDefault()
  const resolvedHref = resolvePluginExternalUrl(href) ?? DOTCRAFT_PLUGIN_FALLBACK_URL
  void window.api.shell.openExternal(resolvedHref).catch(() => undefined)
}

function resolvePluginExternalUrl(href?: string | null): string | null {
  const value = href?.trim()
  if (!value) return null
  try {
    const parsed = new URL(value)
    if (parsed.protocol === 'http:' || parsed.protocol === 'https:' || parsed.protocol === 'mailto:' || parsed.protocol === 'tel:') {
      return parsed.href
    }
  } catch {
    return null
  }
  return null
}

function PluginContentIcon({ type, size }: { type: PluginContentType; size: number }): JSX.Element {
  if (type === 'app') return <Link size={size} aria-hidden />
  if (type === 'desktopPlugin') return <Settings size={size} aria-hidden />
  if (type === 'hooks') return <Anchor size={size} aria-hidden />
  if (type === 'skill') return <Box size={size} aria-hidden />
  if (type === 'mcp') return <Server size={size} aria-hidden />
  if (type === 'lsp') return <Code2 size={size} aria-hidden />
  return <Wrench size={size} aria-hidden />
}

const page: CSSProperties = catalogStyles.page
const rowTitle: CSSProperties = catalogStyles.rowTitle
const rowDesc: CSSProperties = catalogStyles.rowDesc
const emptyText: CSSProperties = catalogStyles.emptyText
const pluginText: CSSProperties = { display: 'flex', flexDirection: 'column', minWidth: 0, flex: 1 }
const detailMain: CSSProperties = { flex: 1, minHeight: 0, overflow: 'auto', width: '100%' }
const detailContent: CSSProperties = { width: 'min(760px, calc(100% - 48px))', margin: '0 auto', padding: '0 0 48px' }
const detailHeader: CSSProperties = { width: 'min(760px, calc(100% - 48px))', margin: '22px auto 28px' }
const detailIconRow: CSSProperties = { display: 'flex', alignItems: 'flex-start' }
const detailIdentityRow: CSSProperties = { display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', flexWrap: 'wrap', gap: '12px 20px', marginTop: 16 }
const detailIdentity: CSSProperties = { display: 'flex', minWidth: 'min(100%, 280px)', flex: '1 1 360px', flexDirection: 'column', gap: 6 }
const detailActions: CSSProperties = { display: 'flex', alignItems: 'flex-start', justifyContent: 'flex-end', flexWrap: 'wrap', gap: 12 }
const detailTitle: CSSProperties = { margin: 0, fontSize: 'var(--type-detail-title-size)', fontWeight: 'var(--type-detail-title-weight)', lineHeight: 'var(--type-detail-title-line-height)' }
const detailSubtitle: CSSProperties = { margin: 0, color: 'var(--text-secondary)', fontSize: 'var(--type-body-size)', lineHeight: 'var(--type-body-line-height)' }
const detailIconButton: CSSProperties = { width: 32, height: 32, borderRadius: 8, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', color: 'var(--text-secondary)', textDecoration: 'none' }
const promptPreview: CSSProperties = { minHeight: 132, borderRadius: 8, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '18px 24px', boxSizing: 'border-box' }
const promptBubble: CSSProperties = { display: 'inline-flex', alignItems: 'center', flexWrap: 'wrap', columnGap: 7, rowGap: 4, maxWidth: '80%', borderRadius: 13, padding: '8px 12px', fontSize: 13, lineHeight: 1.35 }
const promptBubblePrefix: CSSProperties = { display: 'inline-flex', alignItems: 'center', gap: 7, flex: '0 1 auto', minWidth: 0, maxWidth: '100%', whiteSpace: 'nowrap' }
const promptBubbleTitle: CSSProperties = { minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }
const promptBubbleText: CSSProperties = { flex: '1 1 180px', minWidth: 0, whiteSpace: 'normal', overflowWrap: 'anywhere' }
const longDescription: CSSProperties = { margin: '28px 8px 0', lineHeight: 1.55, fontSize: 14, color: 'var(--text-primary)' }
// Detail sections are frameless: a section is marked by a rule under its heading,
// not by a box around its rows, so stacked groups read as one column instead of a
// stack of cards. See the Detail Sections part of DESIGN.md.
const detailSection: CSSProperties = { marginTop: 28 }
const detailSectionTitle: CSSProperties = {
  margin: '0 0 8px',
  paddingBottom: 8,
  borderBottom: '1px solid var(--border-subtle)',
  fontSize: 15,
  fontWeight: 600
}
const contentList: CSSProperties = { display: 'flex', flexDirection: 'column' }
const contentItem: CSSProperties = { display: 'flex', alignItems: 'center', gap: 12, padding: '8px 0' }
// An openable row keeps the same rhythm as a static one, so the list does not
// change shape; the hover fill is what marks it as reachable.
const contentItemButton: CSSProperties = {
  ...contentItem,
  width: 'calc(100% + 16px)',
  marginInline: -8,
  padding: '8px',
  border: 'none',
  borderRadius: 8,
  background: 'transparent',
  color: 'inherit',
  font: 'inherit',
  textAlign: 'left',
  cursor: 'pointer'
}
const contentIcon: CSSProperties = { width: 38, height: 38, borderRadius: 19, border: '1px solid var(--border-default)', display: 'inline-flex', alignItems: 'center', justifyContent: 'center', color: 'var(--text-secondary)' }
const contentTitleLine: CSSProperties = { display: 'inline-flex', alignItems: 'baseline', gap: 5, minWidth: 0 }
const contentKind: CSSProperties = { fontWeight: 400, color: 'var(--text-secondary)' }
const lspEnablePanel: CSSProperties = { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, border: '1px solid var(--border-default)', borderRadius: 8, background: 'var(--bg-secondary)', padding: '12px 14px', marginTop: 16 }
const infoTable: CSSProperties = { display: 'flex', flexDirection: 'column' }
const infoRow: CSSProperties = { display: 'grid', gridTemplateColumns: '180px 1fr', alignItems: 'center', minHeight: 32 }
const infoLabel: CSSProperties = { color: 'var(--text-secondary)', fontSize: 13, padding: '6px 0' }
const infoValue: CSSProperties = { fontSize: 13, padding: '6px 0' }
const plainLink: CSSProperties = { color: 'var(--accent)', display: 'inline-flex' }
