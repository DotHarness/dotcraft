import { useState, useEffect, useRef, type CSSProperties } from 'react'
import { isWorkspaceLockedSwitchError } from '../../shared/workspaceSwitchErrors'
import type { AppLocale } from '../../shared/locales'
import { useLocale, useSetUiLocale, useT } from '../contexts/LocaleContext'
import { ActionTooltip } from './ui/ActionTooltip'
import { DotCraftFullLogo } from './ui/DotCraftLogo'
import { elementToLaunchLogoRect, type LaunchLogoRect } from './WorkspaceLaunchTransition'
import { ChevronRight, FolderOpen, MessageCircle } from 'lucide-react'

interface RecentWorkspace {
  path: string
  name: string
  lastOpenedAt: string
}

/**
 * Full-screen welcome view shown on first launch (no workspace configured).
 * Spec §16.1.
 */
function isLockError(err: unknown): boolean {
  return isWorkspaceLockedSwitchError(err)
}

interface WelcomeScreenProps {
  onOpenWorkspace: (request: { path: string; logoRect: LaunchLogoRect }) => Promise<void>
}

export function WelcomeScreen({ onOpenWorkspace }: WelcomeScreenProps): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const setUiLocale = useSetUiLocale()
  const isMac = window.api.platform === 'darwin'
  const languageSwitcherTop = isMac ? 20 : window.api.titleBarOverlayHeight + 16
  const [recents, setRecents] = useState<RecentWorkspace[]>([])
  const [loading, setLoading] = useState(false)
  const [switchingLocale, setSwitchingLocale] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [lockedPath, setLockedPath] = useState<string | null>(null)
  const [openingWorkspacePath, setOpeningWorkspacePath] = useState<string | null>(null)
  const [chatWorkspacePath, setChatWorkspacePath] = useState<string | null>(null)
  const logoRef = useRef<HTMLDivElement>(null)
  // shakingPath drives the animation; cleared on animationEnd to allow re-triggering
  const [shakingPath, setShakingPath] = useState<string | null>(null)
  const isOpeningWorkspace = openingWorkspacePath != null

  useEffect(() => {
    let disposed = false
    window.api.workspace.getRecent()
      .then((next) => {
        if (!disposed) setRecents(next)
      })
      .catch(() => {})
    window.api.workspace.getProjects?.()
      .then((payload) => {
        if (!disposed) setChatWorkspacePath(payload?.chat?.path?.trim() || null)
      })
      .catch(() => {})
    return () => {
      disposed = true
    }
  }, [])

  async function openWorkspaceWithBrandTransition(path: string): Promise<void> {
    if (loading) return
    const logoRect = elementToLaunchLogoRect(logoRef.current)
    if (!logoRect) return
    setLoading(true)
    setError(null)
    setLockedPath(null)
    setOpeningWorkspacePath(path)
    try {
      await onOpenWorkspace({ path, logoRect })
    } catch (err) {
      if (isLockError(err)) {
        setLockedPath(path)
        setShakingPath(path)
      } else {
        setError(err instanceof Error ? err.message : String(err))
      }
      setOpeningWorkspacePath(null)
      setLoading(false)
    }
  }

  async function handleOpenWorkspace(): Promise<void> {
    if (loading) return
    const picked = await window.api.workspace.pickFolder()
    if (!picked) return
    await openWorkspaceWithBrandTransition(picked)
  }

  async function handleOpenRecent(path: string): Promise<void> {
    await openWorkspaceWithBrandTransition(path)
  }

  async function handleOpenChats(): Promise<void> {
    if (!chatWorkspacePath) return
    await openWorkspaceWithBrandTransition(chatWorkspacePath)
  }

  async function handleLocaleSwitch(nextLocale: AppLocale): Promise<void> {
    if (nextLocale === locale || switchingLocale) return
    setSwitchingLocale(true)
    setUiLocale(nextLocale)
    try {
      await window.api.settings.set({ locale: nextLocale })
    } catch {
      // Ignore locale persistence failures on welcome screen.
    } finally {
      setSwitchingLocale(false)
    }
  }

  const rootClassName = isOpeningWorkspace
    ? 'welcome-screen-root welcome-screen--opening'
    : 'welcome-screen-root'
  const rootStyle: CSSProperties = {
    position: 'relative',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100vh',
    background: 'var(--welcome-surface)',
    color: 'var(--text-primary)',
    padding: '48px',
    boxSizing: 'border-box',
    overflow: 'hidden',
    isolation: 'isolate'
  }

  return (
    <div
      style={rootStyle}
      className={rootClassName}
    >
      <div
        className="welcome-language-switcher"
        style={{
          position: 'absolute',
          top: `${languageSwitcherTop}px`,
          right: '20px',
          display: 'flex',
          alignItems: 'center',
          gap: '8px'
        }}
      >
        <span style={{
          fontSize: 'var(--type-secondary-size)',
          lineHeight: 'var(--type-secondary-line-height)',
          color: 'var(--text-dimmed)'
        }}>{t('welcome.language')}</span>
        <div
          style={{
            display: 'inline-flex',
            border: '1px solid var(--border-default)',
            borderRadius: '999px',
            background: 'var(--bg-secondary)',
            overflow: 'hidden'
          }}
        >
          {(
            [
              ['en', 'EN'],
              ['zh-Hans', '中文']
            ] as const
          ).map(([value, label]) => {
            const active = locale === value
            return (
              <button
                key={value}
                type="button"
                onClick={() => {
                  void handleLocaleSwitch(value)
                }}
                disabled={switchingLocale || loading}
                style={{
                  border: 'none',
                  background: active ? 'var(--accent)' : 'transparent',
                  color: active ? 'var(--on-accent)' : 'var(--text-secondary)',
                  padding: '6px 10px',
                  fontSize: 'var(--type-secondary-size)',
                  fontWeight: 'var(--type-ui-emphasis-weight)',
                  lineHeight: 'var(--type-secondary-line-height)',
                  cursor: switchingLocale || loading ? 'default' : 'pointer',
                  opacity: switchingLocale || loading ? 0.7 : 1
                }}
                aria-label={label}
              >
                {label}
              </button>
            )
          })}
        </div>
      </div>

      <div className="welcome-logo-focus" aria-hidden="true" ref={logoRef}>
        <DotCraftFullLogo size={96} className="welcome-logo-image" />
      </div>
      <div
        className="welcome-screen-background"
        style={{
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          width: '100%'
        }}
      >
        <div style={{
          marginBottom: '10px',
          fontSize: 'var(--type-title-size)',
          lineHeight: 'var(--type-title-line-height)',
          fontWeight: 'var(--type-title-weight)',
          letterSpacing: 0
        }}>
          {t('app.brandSubtitle')}
        </div>
        <div style={{
          fontSize: 'var(--type-body-size)',
          lineHeight: 'var(--type-body-line-height)',
          color: 'var(--text-secondary)',
          marginBottom: '28px'
        }}>
          {t('welcome.tagline')}
        </div>

        <div className="welcome-workspace-panel">
          <button
            type="button"
            className="welcome-workspace-row welcome-open-workspace-row"
            onClick={() => { void handleOpenWorkspace() }}
            disabled={loading}
            aria-label={t('welcome.openWorkspace')}
          >
            <span className="welcome-workspace-row-icon" aria-hidden="true">
              <FolderOpen size={22} strokeWidth={1.8} />
            </span>
            <span className="welcome-workspace-row-body">
              <span className="welcome-workspace-row-title">
                {loading ? t('welcome.opening') : t('welcome.openWorkspace')}
              </span>
              <span className="welcome-workspace-row-path">
                {t('welcome.openWorkspaceHint')}
              </span>
            </span>
            <ChevronRight className="welcome-workspace-row-action" size={18} strokeWidth={2.1} aria-hidden="true" />
          </button>

          {chatWorkspacePath && (
            <button
              type="button"
              className="welcome-workspace-row welcome-chats-row"
              onClick={() => { void handleOpenChats() }}
              disabled={loading}
              aria-label={t('welcome.openChats')}
            >
              <span className="welcome-workspace-row-icon" aria-hidden="true">
                <MessageCircle size={22} strokeWidth={1.8} />
              </span>
              <span className="welcome-workspace-row-body">
                <span className="welcome-workspace-row-title">
                  {loading && openingWorkspacePath === chatWorkspacePath ? t('welcome.opening') : t('welcome.openChats')}
                </span>
                <span className="welcome-workspace-row-path">
                  {t('welcome.openChatsHint')}
                </span>
              </span>
              <ChevronRight className="welcome-workspace-row-action" size={18} strokeWidth={2.1} aria-hidden="true" />
            </button>
          )}

          {recents.map((r) => {
            const isLocked = lockedPath === r.path
            const isShaking = shakingPath === r.path
            return (
              <button
                key={r.path}
                type="button"
                className="welcome-workspace-row welcome-recent-workspace-row"
                onClick={() => { void handleOpenRecent(r.path) }}
                disabled={loading}
                style={{
                  animation: isShaking ? 'shake 0.4s ease' : undefined
                }}
                onAnimationEnd={() => {
                  if (isShaking) setShakingPath(null)
                }}
                aria-label={`Open workspace ${r.name}`}
              >
                <span className="welcome-workspace-row-body">
                  <span className="welcome-workspace-row-title">{r.name}</span>
                  <ActionTooltip
                    label={r.path}
                    wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}
                  >
                    <span className="welcome-workspace-row-path" style={{ display: 'block' }}>
                      {r.path}
                    </span>
                  </ActionTooltip>
                  {isLocked && (
                    <span className="welcome-workspace-row-warning">
                      {t('welcome.alreadyOpen')}
                    </span>
                  )}
                </span>
              </button>
            )
          })}
        </div>

        {/* Error */}
        {error && (
          <div
            style={{
              color: 'var(--error)',
              fontSize: 'var(--type-ui-size)',
              lineHeight: 'var(--type-ui-line-height)',
              marginBottom: '16px',
              maxWidth: '400px',
              textAlign: 'center'
            }}
          >
            {error}
          </div>
        )}
      </div>
    </div>
  )
}
