import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type Dispatch,
  type JSX,
  type ReactNode,
  type SetStateAction
} from 'react'
import { createPortal } from 'react-dom'
import { ArrowLeft, Eye, FileSearch, FileText, MessageSquare, MoreHorizontal, Pencil, Plus, RefreshCw, Search, Shuffle, Sparkles, Tag, Trash2, X, type LucideIcon } from 'lucide-react'
import { showToast } from '../../stores/toastStore'
import { useModelCatalogStore } from '../../stores/modelCatalogStore'
import { useConversationStore } from '../../stores/conversationStore'
import { useLocale } from '../../contexts/LocaleContext'
import { ConversationPanel } from '../layout/ConversationPanel'
import { ComposerSendButton, ComposerShell, SendIcon } from '../conversation/ComposerShell'
import { formatRelativeTime } from '../../utils/relativeTime'
import { CatalogCompactGrid, CatalogHoverButton, CatalogSearchBox, CatalogSection, styles as catalogStyles } from '../catalog/CatalogSurface'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { SettingsGroup, SettingsRow } from '../settings/SettingsGroup'
import { SettingsSelect } from '../settings/ui/SettingsSelect'
import { PillSwitch } from '../ui/PillSwitch'
import { RobotAvatar } from './RobotAvatar'
import { AGENT_BUILDER_AVATAR, avatarForProfile, randomAvatar, type AvatarSpec } from './agentAvatar'
import {
  AGENT_CONTROL_OPTIONS,
  APPROVAL_OPTIONS,
  REASONING_OPTIONS,
  createEmptyDraft,
  parseProfile,
  toMarkdown,
  type AgentControl,
  type ApprovalPolicy,
  type ProfileDraft,
  type ReasoningEffort,
  type SaveTarget
} from './agentProfileDraft'
import { applyBuilderChange, type BuilderField, type BuilderToolResult } from './agentBuilderDraftSync'
import { useAgentBuilderConversation } from './useAgentBuilderConversation'
import { AgentSaveTargetDialog } from './AgentSaveTargetDialog'
import './AgentBuilderView.css'

// ── Wire shapes (subset; see specs/protocols/appserver-protocol.md) ──

interface ProfileEntry {
  id: string
  name?: string
  description?: string
  source: string
  valid?: boolean
  readOnly?: boolean
  isBuiltIn?: boolean
  shadowed?: boolean
  rawContent?: string
  updatedAt?: string
}

interface ToolInfo {
  name: string
  description?: string
  icon?: string
  planMode?: boolean
}

interface SkillInfo {
  name: string
  displayName?: string
  description?: string
  source?: string
  enabled?: boolean
}

type Filter = 'all' | 'builtIn' | 'user' | 'workspace'

type Route =
  | { name: 'gallery' }
  | { name: 'intro' }
  | {
      name: 'builder'
      draft: ProfileDraft
      id: string | null
      source: string | null
      readOnly: boolean
      isNew: boolean
      saveTarget: SaveTarget
      saving: boolean
      avatar: AvatarSpec
      /** Whether the profile is persisted (existing, or created via the Create button). */
      created: boolean
      /** ISO last-updated time of the persisted profile; drives "Updated X ago". */
      updatedAt: string | null
    }

const SUGGESTIONS: { icon: LucideIcon; title: string; desc: string; prompt: string }[] = [
  { icon: FileSearch, title: 'Code review', desc: 'Read-only: find correctness gaps and missing tests', prompt: 'A read-only code reviewer focused on correctness, risk, and missing tests.' },
  { icon: FileText, title: 'Doc writer', desc: 'Draft and maintain docs in the established style', prompt: 'A documentation writer that drafts and keeps docs consistent in the established style.' },
  { icon: Tag, title: 'Bug triage', desc: 'Prioritize incoming bugs and log them', prompt: 'A bug triage agent that reviews incoming bugs, prioritizes them, and logs them.' }
]

const FAN = [
  { rot: -14, y: 16 },
  { rot: -7, y: 5 },
  { rot: 0, y: 0 },
  { rot: 7, y: 5 },
  { rot: 14, y: 16 }
]

// Neutral-inverted primary "+ Add" button, matching the Channels/Plugins catalog header.
const primaryAddButton: CSSProperties = {
  ...catalogStyles.manageButton,
  borderColor: 'var(--text-primary)',
  backgroundColor: 'var(--text-primary)',
  color: 'var(--bg-primary)',
  fontWeight: 600
}

const galleryAvatar: CSSProperties = { flex: '0 0 auto', display: 'inline-flex' }
const galleryText: CSSProperties = { minWidth: 0, flex: 1, display: 'flex', flexDirection: 'column' }

async function rpc<T>(method: string, params: Record<string, unknown> = {}): Promise<T> {
  return (await window.api.appServer.sendRequest(method, params)) as T
}

function errorText(err: unknown): string {
  return err instanceof Error ? err.message : String(err)
}

function sectionFor(source: string): Filter {
  if (source === 'user') return 'user'
  if (source === 'workspace') return 'workspace'
  return 'builtIn'
}

function writableSource(source: string | null): SaveTarget {
  return source === 'user' ? 'user' : 'workspace'
}

export function AgentBuilderView(): JSX.Element {
  const [profiles, setProfiles] = useState<ProfileEntry[]>([])
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  const [menuPos, setMenuPos] = useState<ContextMenuPosition | null>(null)
  const [route, setRoute] = useState<Route>({ name: 'gallery' })

  const [toolCatalog, setToolCatalog] = useState<ToolInfo[]>([])
  const [skillCatalog, setSkillCatalog] = useState<SkillInfo[]>([])
  const [mcpServers, setMcpServers] = useState<string[]>([])

  const [viewMode, setViewMode] = useState<'edit' | 'preview'>('edit')
  const [autoSaveState, setAutoSaveState] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle')
  const lastSavedMdRef = useRef<string | null>(null)
  const lastSavedIdRef = useRef<string | null>(null)
  const lastSavedSourceRef = useRef<SaveTarget | null>(null)

  // Conversational builder: when non-null, the chat pane is open with these stable session params.
  // Kept separate from the (live-changing) draft so editing the name doesn't recreate the thread.
  const [chat, setChat] = useState<{ targetId: string; targetSource: SaveTarget; initialPrompt: string } | null>(null)
  // The field the agent most recently edited — drives the cursor-on-field highlight (M4).
  const [highlight, setHighlight] = useState<{ field: BuilderField; seq: number } | null>(null)
  const highlightSeqRef = useRef(0)
  // Create flow: the save target (user/workspace) is chosen in a dialog opened from the Create button.
  const [createDialogOpen, setCreateDialogOpen] = useState(false)
  const workspacePath = useConversationStore((s) => s.workspacePath)

  useEffect(() => {
    if (!highlight) return undefined
    const timer = window.setTimeout(() => setHighlight(null), 1500)
    return () => window.clearTimeout(timer)
  }, [highlight])

  // Apply one streamed builder tool result to the live draft and flag the edited field.
  const handleBuilderResult = useCallback((result: BuilderToolResult): void => {
    if (!result.ok || !result.field) return
    setHighlight({ field: result.field, seq: (highlightSeqRef.current += 1) })
    setRoute((r) => (r.name === 'builder' ? { ...r, draft: applyBuilderChange(r.draft, result).draft } : r))
  }, [])

  const builderConversation = useAgentBuilderConversation({
    active: route.name === 'builder' && chat !== null,
    targetId: chat?.targetId ?? '',
    targetSource: chat?.targetSource ?? 'workspace',
    initialPrompt: chat?.initialPrompt ?? null,
    onResult: handleBuilderResult
  })

  const loadProfiles = useCallback(async (): Promise<void> => {
    try {
      const res = await rpc<{ profiles?: ProfileEntry[] }>('agent/profiles/list', { includeInvalid: true })
      setProfiles(Array.isArray(res.profiles) ? res.profiles : [])
      setStatus('ready')
      setLoadError(null)
    } catch (err) {
      setProfiles([])
      setStatus('error')
      setLoadError(errorText(err))
    }
  }, [])

  useEffect(() => {
    void loadProfiles()
  }, [loadProfiles])

  // Load the selector catalogs once. Failures degrade gracefully (free-text fallback).
  useEffect(() => {
    void rpc<{ tools?: ToolInfo[] }>('tool/list', {})
      .then((res) => setToolCatalog(Array.isArray(res.tools) ? res.tools : []))
      .catch(() => setToolCatalog([]))
    void rpc<{ skills?: SkillInfo[] }>('skills/list', { includeUnavailable: true })
      .then((res) => setSkillCatalog(Array.isArray(res.skills) ? res.skills : []))
      .catch(() => setSkillCatalog([]))
    void rpc<{ servers?: { name: string }[] }>('mcp/list', {})
      .then((res) => setMcpServers(Array.isArray(res.servers) ? res.servers.map((s) => s.name).filter(Boolean) : []))
      .catch(() => setMcpServers([]))
  }, [])

  const setDraft: Dispatch<SetStateAction<ProfileDraft>> = useCallback((updater) => {
    setRoute((r) => {
      if (r.name !== 'builder') return r
      const next = typeof updater === 'function' ? (updater as (d: ProfileDraft) => ProfileDraft)(r.draft) : updater
      return { ...r, draft: next }
    })
  }, [])

  const openProfile = useCallback(async (entry: ProfileEntry): Promise<void> => {
    try {
      const res = await rpc<{ profile?: ProfileEntry }>('agent/profiles/read', { id: entry.id, source: entry.source })
      const draft = parseProfile(res.profile?.rawContent)
      if (!draft.name) draft.name = entry.id
      const readOnly = entry.readOnly === true || entry.source === 'builtIn' || entry.source === 'plugin'
      // Seed the auto-save baseline so opening a profile doesn't immediately re-save it (the
      // Markdown + target must match the opened state). Read-only sources have no saved id yet —
      // the first real edit forks a new editable copy under the save target.
      lastSavedMdRef.current = toMarkdown(draft)
      lastSavedIdRef.current = readOnly ? null : entry.id
      lastSavedSourceRef.current = writableSource(entry.source)
      setViewMode('edit')
      setAutoSaveState('idle')
      setRoute({
        name: 'builder',
        draft,
        id: entry.id,
        source: entry.source,
        readOnly,
        isNew: readOnly,
        saveTarget: writableSource(entry.source),
        saving: false,
        avatar: avatarForProfile(draft.name),
        // An existing writable profile is already "created"; a read-only template is an uncreated copy.
        created: !readOnly,
        updatedAt: readOnly ? null : (res.profile?.updatedAt ?? entry.updatedAt ?? null)
      })
    } catch (err) {
      showToast({ message: `Could not open: ${errorText(err)}`, type: 'error' })
    }
  }, [])

  const startDraft = useCallback((draft: ProfileDraft, avatar: AvatarSpec): void => {
    lastSavedMdRef.current = null
    lastSavedIdRef.current = null
    lastSavedSourceRef.current = null
    setViewMode('edit')
    setAutoSaveState('idle')
    setRoute({ name: 'builder', draft, id: null, source: null, readOnly: false, isNew: true, saveTarget: 'workspace', saving: false, avatar, created: false, updatedAt: null })
  }, [])

  const newBlank = useCallback((description = ''): void => {
    startDraft({ ...createEmptyDraft(), description }, randomAvatar())
  }, [startDraft])

  const fromTemplate = useCallback(async (entry: ProfileEntry): Promise<void> => {
    try {
      const res = await rpc<{ profile?: ProfileEntry }>('agent/profiles/read', { id: entry.id, source: entry.source })
      const draft = parseProfile(res.profile?.rawContent)
      if (!draft.name) draft.name = entry.id
      startDraft(draft, avatarForProfile(draft.name))
    } catch (err) {
      showToast({ message: `Could not load template: ${errorText(err)}`, type: 'error' })
    }
  }, [startDraft])

  // Auto-save: persist the draft (debounced) — but only AFTER it has been created (existing profile,
  // or post-"Create"). A new/uncreated agent never auto-saves; it waits for the explicit Create button.
  // A name or save-target change moves the file. The Markdown baseline guards against re-saving
  // unchanged content (which would otherwise loop, since each save updates the route).
  useEffect(() => {
    if (route.name !== 'builder') {
      setAutoSaveState('idle')
      return undefined
    }
    if (!route.created) return undefined
    const name = route.draft.name.trim()
    const target = route.saveTarget
    const md = toMarkdown(route.draft)
    if (!name) return undefined
    if (md === lastSavedMdRef.current && target === lastSavedSourceRef.current) return undefined
    const timer = window.setTimeout(() => {
      void (async () => {
        setAutoSaveState('saving')
        try {
          const prevId = lastSavedIdRef.current
          const prevSource = lastSavedSourceRef.current
          if (prevId && (prevId !== name || prevSource !== target)) {
            await rpc('agent/profiles/remove', { id: prevId, source: prevSource }).catch(() => undefined)
          }
          await rpc('agent/profiles/upsert', { id: name, source: target, rawContent: md })
          lastSavedMdRef.current = md
          lastSavedIdRef.current = name
          lastSavedSourceRef.current = target
          setRoute((r) => (r.name === 'builder' ? { ...r, id: name, source: target, readOnly: false, isNew: false, updatedAt: new Date().toISOString() } : r))
          setAutoSaveState('saved')
          void loadProfiles()
        } catch {
          setAutoSaveState('error')
        }
      })()
    }, 900)
    return () => window.clearTimeout(timer)
  }, [route, loadProfiles])

  // Explicit creation for a new/uncreated agent. The save target (user vs workspace) is chosen in the
  // Create dialog rather than a persistent toggle. After this, auto-save takes over to that target.
  const createProfile = useCallback(async (target: SaveTarget): Promise<void> => {
    if (route.name !== 'builder') return
    const name = route.draft.name.trim()
    if (!name) {
      showToast({ message: 'Give the agent a name first.', type: 'error' })
      return
    }
    const md = toMarkdown(route.draft)
    setAutoSaveState('saving')
    try {
      await rpc('agent/profiles/upsert', { id: name, source: target, rawContent: md })
      lastSavedMdRef.current = md
      lastSavedIdRef.current = name
      lastSavedSourceRef.current = target
      setRoute((r) => (r.name === 'builder'
        ? { ...r, id: name, source: target, saveTarget: target, readOnly: false, isNew: false, created: true, updatedAt: new Date().toISOString() }
        : r))
      setAutoSaveState('saved')
      showToast({ message: `Created "${name}".`, type: 'success' })
      void loadProfiles()
    } catch (err) {
      setAutoSaveState('error')
      showToast({ message: `Create failed: ${errorText(err)}`, type: 'error' })
    }
  }, [route, loadProfiles])

  const removeProfile = useCallback(async (): Promise<void> => {
    if (route.name !== 'builder' || !route.id || route.readOnly) return
    const { id, source } = route
    try {
      await rpc('agent/profiles/remove', { id, source })
      showToast({ message: `Removed "${id}".`, type: 'info' })
      await loadProfiles()
      setChat(null)
      setRoute({ name: 'gallery' })
    } catch (err) {
      showToast({ message: `Remove failed: ${errorText(err)}`, type: 'error' })
    }
  }, [route, loadProfiles])

  // Open/close the chat pane. Opening captures a stable target id (a placeholder for a new agent),
  // so the builder thread is created once and survives name edits.
  const toggleChat = useCallback((): void => {
    setChat((current) => {
      if (current) return null
      if (route.name !== 'builder') return null
      const { name, description } = route.draft
      const intent = description.trim() || name.trim()
      const initialPrompt = route.created
        ? `Let's refine the "${name.trim() || 'this'}" agent together. Review its current configuration and suggest improvements to its instructions, tools, and skills.`
        : `Help me build a new agent.${intent ? ` What I want: "${intent}".` : ''} Propose a name, role instructions, and a sensible set of tools and skills, then we'll refine it together.`
      return { targetId: name.trim() || 'draft-agent', targetSource: route.saveTarget, initialPrompt }
    })
  }, [route])

  const leaveBuilder = useCallback((): void => {
    setChat(null)
    setHighlight(null)
    setRoute({ name: 'gallery' })
  }, [])

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    return profiles.filter((p) => {
      if (p.shadowed) return false
      if (!q) return true
      return p.id.toLowerCase().includes(q) || (p.description || '').toLowerCase().includes(q)
    })
  }, [profiles, query])

  if (route.name === 'builder') {
    const chatOpen = chat !== null
    return (
      <div className={`agent-builder-split${chatOpen ? ' is-chat' : ''}`}>
        <div className="agent-builder-split-main">
          <BuilderView
            route={route}
            setRoute={setRoute}
            setDraft={setDraft}
            toolCatalog={toolCatalog}
            skillCatalog={skillCatalog}
            mcpServers={mcpServers}
            viewMode={viewMode}
            setViewMode={setViewMode}
            autoSaveState={autoSaveState}
            chatOpen={chatOpen}
            onToggleChat={toggleChat}
            highlightField={highlight?.field ?? null}
            onBack={leaveBuilder}
            onDelete={removeProfile}
            onCreate={() => setCreateDialogOpen(true)}
          />
        </div>
        {chatOpen && (
          <aside className="agent-builder-chatpane">
            {builderConversation.status === 'error' ? (
              <div className="agent-builder-chat-error">
                Couldn’t start the builder chat: {builderConversation.error}
              </div>
            ) : builderConversation.status !== 'ready' ? (
              <div className="agent-builder-chat-loading">Starting builder…</div>
            ) : (
              <ConversationPanel workspacePath={workspacePath} minimalComposer />
            )}
          </aside>
        )}
        {createDialogOpen && (
          <AgentSaveTargetDialog
            name={route.draft.name.trim()}
            onChoose={(target) => {
              setCreateDialogOpen(false)
              void createProfile(target)
            }}
            onCancel={() => setCreateDialogOpen(false)}
          />
        )}
      </div>
    )
  }

  if (route.name === 'intro') {
    const templates = profiles.filter((p) => sectionFor(p.source) === 'builtIn')
    return (
      <div className="agent-builder">
        <div className="agent-builder-intro-top">
          <button type="button" className="agent-builder-iconbtn" title="Back" onClick={() => setRoute({ name: 'gallery' })}>
            <ArrowLeft size={18} />
          </button>
          <button type="button" className="agent-builder-intro-blank" onClick={() => newBlank()}>
            Start blank
          </button>
        </div>
        <div className="agent-builder-intro">
          <span className="agent-builder-intro-mascot">
            <RobotAvatar spec={AGENT_BUILDER_AVATAR} size={52} animated />
          </span>
          <h1 className="agent-builder-intro-title">Build a new agent</h1>
          <IntroComposer onSubmit={(text) => newBlank(text)} />
          <div className="agent-builder-intro-sugs">
            {SUGGESTIONS.map((s) => {
              const Icon = s.icon
              return (
                <button key={s.title} type="button" className="agent-builder-intro-sug" onClick={() => newBlank(s.prompt)}>
                  <span className="agent-builder-intro-sug-ic" aria-hidden><Icon size={16} /></span>
                  <span className="agent-builder-intro-sug-t">{s.title}</span>
                  <span className="agent-builder-intro-sug-d">{s.desc}</span>
                </button>
              )
            })}
          </div>
          {templates.length > 0 && (
            <TemplateDeck templates={templates} onPick={(p) => void fromTemplate(p)} />
          )}
        </div>
      </div>
    )
  }

  // ── Gallery (shares the Channels / Automations / Plugins catalog surface) ──
  const sections: { key: Filter; title: string }[] = [
    { key: 'builtIn', title: 'Built-in templates' },
    { key: 'user', title: 'My agents' },
    { key: 'workspace', title: 'This workspace' }
  ]
  const visibleSections = sections
    .map((sec) => ({ sec, items: filtered.filter((p) => sectionFor(p.source) === sec.key) }))
    .filter((group) => group.items.length > 0)

  return (
    <div style={catalogStyles.page}>
      <header style={catalogStyles.browseHeader}>
        <div style={catalogStyles.topActions}>
          <button type="button" style={primaryAddButton} onClick={() => setRoute({ name: 'intro' })}>
            <Plus size={14} aria-hidden />
            <span style={{ lineHeight: 1, transform: 'translateY(-1px)' }}>New agent</span>
          </button>
          <button type="button" style={catalogStyles.iconButton} aria-label="More actions" onClick={(e) => setMenuPos({ x: e.clientX, y: e.clientY })}>
            <MoreHorizontal size={16} aria-hidden />
          </button>
        </div>
        <h1 style={catalogStyles.heroTitle}>Build your agents with DotCraft</h1>
        <div style={catalogStyles.searchRow}>
          <CatalogSearchBox value={query} placeholder="Search agents" onChange={setQuery} />
        </div>
      </header>
      <main style={catalogStyles.browseMain}>
        {status === 'loading' ? (
          <p style={catalogStyles.emptyText}>Loading agents…</p>
        ) : status === 'error' ? (
          <p style={catalogStyles.emptyText}>{loadError || 'Could not load agents.'}</p>
        ) : visibleSections.length === 0 ? (
          <p style={catalogStyles.emptyText}>No agents yet. Use “New agent” to build one.</p>
        ) : (
          visibleSections.map(({ sec, items }) => (
            <CatalogSection key={sec.key} title={sec.title}>
              <CatalogCompactGrid>
                {items.map((p) => (
                  <CatalogHoverButton key={`${p.source}:${p.id}`} type="button" baseStyle={catalogStyles.compactItem} onClick={() => void openProfile(p)}>
                    <span style={galleryAvatar}>
                      <RobotAvatar spec={avatarForProfile(p.id)} size={36} />
                    </span>
                    <span style={galleryText}>
                      <span style={catalogStyles.rowTitleLine}><strong style={catalogStyles.rowTitle}>{p.id}</strong></span>
                      <span style={catalogStyles.rowDesc}>{p.description || ''}</span>
                    </span>
                    {p.valid === false && <span style={catalogStyles.statusIcon}>Issues</span>}
                  </CatalogHoverButton>
                ))}
              </CatalogCompactGrid>
            </CatalogSection>
          ))
        )}
      </main>
      {menuPos && (
        <ContextMenu
          position={menuPos}
          onClose={() => setMenuPos(null)}
          items={[{ label: 'Refresh', icon: <RefreshCw size={14} aria-hidden />, onClick: () => void loadProfiles() }]}
        />
      )}
    </div>
  )
}

// ── "Build a new agent" input ──
// Reuses the real conversation ComposerShell (same card/mascot/send affordance as the chat composer),
// in its minimal form: no permissions / workspace / subscription chrome. Keeps the builder visually
// consistent with the conversational pane since they are one feature.
function IntroComposer({ onSubmit }: { onSubmit: (text: string) => void }): JSX.Element {
  const [text, setText] = useState('')
  const canSend = text.trim().length > 0
  const submit = (): void => {
    if (canSend) onSubmit(text.trim())
  }
  return (
    <div className="agent-builder-introcomposer">
      <ComposerShell
        dragOver={false}
        dropLabel=""
        onDragOver={(e) => e.preventDefault()}
        onDragLeave={() => {}}
        onDrop={(e) => e.preventDefault()}
        editor={
          <textarea
            className="agent-builder-intro-input"
            rows={3}
            value={text}
            autoFocus
            placeholder="Describe the agent you want…"
            onChange={(e) => setText(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault()
                submit()
              }
            }}
          />
        }
        footerLeading={<span />}
        footerAction={
          <ComposerSendButton tone={canSend ? 'enabled' : 'disabled'} disabled={!canSend} aria-label="Create" onClick={submit}>
            <SendIcon />
          </ComposerSendButton>
        }
      />
    </div>
  )
}

// ── Built-in template deck ──
// Fanned, bottom-clipped cards. Hover selection is driven by pointer X over the (stable) container
// rect, not per-card :hover — lifting a card vertically never changes which card is selected, so the
// overlap boundaries no longer cause the active card to oscillate.
function TemplateDeck({ templates, onPick }: { templates: ProfileEntry[]; onPick: (entry: ProfileEntry) => void }): JSX.Element {
  const ref = useRef<HTMLDivElement>(null)
  const [active, setActive] = useState<number | null>(null)
  const cards = templates.slice(0, 5)

  const trackPointer = (clientX: number): void => {
    const el = ref.current
    if (!el) return
    const rect = el.getBoundingClientRect()
    if (rect.width <= 0) return
    const ratio = (clientX - rect.left) / rect.width
    setActive(Math.max(0, Math.min(cards.length - 1, Math.floor(ratio * cards.length))))
  }

  return (
    <div
      ref={ref}
      className="agent-builder-intro-deck"
      onMouseMove={(e) => trackPointer(e.clientX)}
      onMouseLeave={() => setActive(null)}
    >
      {cards.map((p, i) => {
        const f = FAN[i % FAN.length]
        const isActive = active === i
        return (
          <button
            key={p.id}
            type="button"
            className={`agent-builder-deckcard${isActive ? ' is-active' : ''}`}
            style={{
              transform: isActive
                ? 'translateY(-84px) rotate(0deg) scale(1.04)'
                : `translateY(${f.y}px) rotate(${f.rot}deg)`,
              zIndex: isActive ? 40 : 5 - Math.abs((i % 5) - 2)
            }}
            onClick={() => onPick(p)}
          >
            <RobotAvatar spec={avatarForProfile(p.id)} size={34} />
            <span className="agent-builder-deckcard-name">{p.id.replace('team-', '')}</span>
            <span className="agent-builder-deckcard-desc">{p.description || ''}</span>
          </button>
        )
      })}
    </div>
  )
}

// ── Builder (document editor) ──
interface BuilderViewProps {
  route: Extract<Route, { name: 'builder' }>
  setRoute: Dispatch<SetStateAction<Route>>
  setDraft: Dispatch<SetStateAction<ProfileDraft>>
  toolCatalog: ToolInfo[]
  skillCatalog: SkillInfo[]
  mcpServers: string[]
  viewMode: 'edit' | 'preview'
  setViewMode: Dispatch<SetStateAction<'edit' | 'preview'>>
  autoSaveState: 'idle' | 'saving' | 'saved' | 'error'
  chatOpen: boolean
  onToggleChat: () => void
  highlightField: BuilderField | null
  onBack: () => void
  onDelete: () => void
  onCreate: () => void
}

function BuilderView({ route, setRoute, setDraft, toolCatalog, skillCatalog, mcpServers, viewMode, setViewMode, autoSaveState, chatOpen, onToggleChat, highlightField, onBack, onDelete, onCreate }: BuilderViewProps): JSX.Element {
  const locale = useLocale()
  const { draft, avatar } = route
  const nameMissing = !draft.name.trim()
  const preview = viewMode === 'preview'
  const [menuOpen, setMenuOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)
  // While a builder turn is running, the agent "drives" the document: it becomes non-interactive and
  // shows the cursor-on-field affordance so the user watches rather than fights the agent's edits.
  const agentTurnRunning = useConversationStore((s) => s.turnStatus === 'running')
  const agentDriving = chatOpen && agentTurnRunning
  const fieldClass = (fields: BuilderField[]): string =>
    highlightField && fields.includes(highlightField) ? ' is-agent-editing' : ''

  const reroll = (): void => setRoute((r) => (r.name === 'builder' ? { ...r, avatar: randomAvatar(r.avatar) } : r))

  useEffect(() => {
    if (!menuOpen) return undefined
    function onDown(event: MouseEvent): void {
      if (menuRef.current && !menuRef.current.contains(event.target as Node)) setMenuOpen(false)
    }
    document.addEventListener('mousedown', onDown, true)
    return () => document.removeEventListener('mousedown', onDown, true)
  }, [menuOpen])

  const modelOptions = useModelCatalogStore((s) => s.modelOptions)
  useEffect(() => {
    void useModelCatalogStore.getState().loadIfNeeded()
  }, [])
  const modelSelectOptions = useMemo(() => {
    const opts = [{ value: 'inherit', label: 'Inherit (thread default)' }, ...modelOptions.map((id) => ({ value: id, label: id }))]
    if (draft.model && draft.model !== 'inherit' && !modelOptions.includes(draft.model)) {
      opts.push({ value: draft.model, label: draft.model })
    }
    return opts
  }, [modelOptions, draft.model])

  return (
    <div className="agent-builder">
      <header className="agent-builder-edit-head">
        <div className="agent-builder-edit-left">
          <button type="button" className="agent-builder-iconbtn" title="Back" onClick={onBack}>
            <ArrowLeft size={18} />
          </button>
          <span className="agent-builder-headavatar">
            <RobotAvatar spec={avatar} size={26} />
          </span>
          <span className="agent-builder-headname">{draft.name || 'Untitled agent'}</span>
        </div>
        <div className="agent-builder-edit-right">
          {route.created && (
            <span className={`agent-builder-autosave${autoSaveState === 'error' ? ' is-error' : ''}`}>
              {autoSaveState === 'saving'
                ? 'Saving…'
                : autoSaveState === 'error'
                  ? 'Save failed'
                  : route.updatedAt
                    ? `Updated ${formatRelativeTime(route.updatedAt, new Date(), locale)}`
                    : 'Saved'}
            </span>
          )}
          <button
            type="button"
            className={`agent-builder-btn-secondary${chatOpen ? ' is-active' : ''}`}
            onClick={onToggleChat}
            aria-pressed={chatOpen}
          >
            {chatOpen ? <><MessageSquare size={15} /> Chatting</> : <><Sparkles size={15} /> Build with chat</>}
          </button>
          <button type="button" className="agent-builder-btn-secondary" onClick={() => setViewMode(preview ? 'edit' : 'preview')}>
            {preview ? <><Pencil size={15} /> Edit</> : <><Eye size={15} /> Preview</>}
          </button>
          {route.created ? (
            <div className="agent-builder-menu" ref={menuRef}>
              <button type="button" className="agent-builder-iconbtn" aria-label="More actions" aria-expanded={menuOpen} onClick={() => setMenuOpen((v) => !v)}>
                <MoreHorizontal size={18} />
              </button>
              {menuOpen && (
                <div className="agent-builder-menu-pop" role="menu">
                  <button type="button" className="agent-builder-menu-item is-danger" role="menuitem" onClick={() => { setMenuOpen(false); onDelete() }}>
                    <Trash2 size={15} /> Delete
                  </button>
                </div>
              )}
            </div>
          ) : (
            <button type="button" className="agent-builder-btn" disabled={nameMissing} onClick={onCreate}>
              <Plus size={15} /> Create
            </button>
          )}
        </div>
      </header>

      <div className="agent-builder-scroll">
      <div className={`agent-builder-doc${agentDriving ? ' is-agent-driving' : ''}`}>
        {agentDriving && <div className="agent-builder-driving-veil" aria-hidden />}
        <div className={`agent-builder-id${fieldClass(['name', 'description'])}`}>
          <span className="agent-builder-id-avatar">
            <RobotAvatar spec={avatar} size={64} animated />
            {!preview && (
              <button type="button" className="agent-builder-reroll" onClick={reroll} title="Re-roll avatar">
                <Shuffle size={12} />
              </button>
            )}
          </span>
          <div className="agent-builder-id-main">
            <input
              className={`agent-builder-id-name${nameMissing ? ' is-empty' : ''}`}
              value={draft.name}
              placeholder="agent name"
              readOnly={preview}
              onChange={(e) => setDraft((d) => ({ ...d, name: e.target.value }))}
            />
            <input
              className="agent-builder-id-desc"
              value={draft.description}
              placeholder="One line about this agent…"
              readOnly={preview}
              onChange={(e) => setDraft((d) => ({ ...d, description: e.target.value }))}
            />
            {route.readOnly && !preview && <div className="agent-builder-note">Built-in template — click Create to save it as a new agent in the selected source.</div>}
          </div>
        </div>

        <div className="agent-builder-divider" />

        <Section label="Tools" editing={highlightField === 'tools.allow'}>
          <CatalogField
            options={toolCatalog.map((tool) => ({ id: tool.name, label: tool.name, description: tool.description, icon: tool.icon }))}
            selected={draft.tools.allow}
            onChange={(allow) => setDraft((d) => ({ ...d, tools: { ...d.tools, allow, deny: d.tools.deny.filter((x) => !allow.includes(x)) } }))}
            addLabel="Add tool"
            emptyHint="All built-in tools are available — add tools to restrict to an allow-list."
            readOnly={preview}
          />
        </Section>

        <Section label="MCP" editing={highlightField === 'mcp.servers'}>
          <CatalogField
            options={mcpServers.map((name) => ({ id: name, label: name }))}
            selected={draft.mcp.servers}
            onChange={(servers) => setDraft((d) => ({ ...d, mcp: { ...d.mcp, servers } }))}
            addLabel="Add MCP server"
            emptyHint={mcpServers.length === 0 ? 'No MCP servers configured for this workspace.' : undefined}
            readOnly={preview}
          />
        </Section>

        <Section label="Skills" editing={highlightField === 'skills.preload'}>
          <CatalogField
            options={skillCatalog.map((s) => ({ id: s.name, label: s.displayName || s.name, description: s.description }))}
            selected={draft.skills.preload}
            onChange={(preload) => setDraft((d) => ({ ...d, skills: { ...d.skills, preload } }))}
            addLabel="Add skill"
            readOnly={preview}
          />
        </Section>

        <Section label="Instructions" editing={highlightField === 'instructions'}>
          <textarea
            className="agent-builder-instr"
            rows={6}
            value={draft.roleInstructions}
            placeholder="Give your agent instructions on how to operate — its job, boundaries, and what it handles…"
            readOnly={preview}
            onChange={(e) => setDraft((d) => ({ ...d, roleInstructions: e.target.value }))}
          />
        </Section>

        <Section label="Details" editing={highlightField === 'model' || highlightField === 'approval' || highlightField === 'tools.agentControl'}>
          <SettingsGroup>
            <SettingsRow
              label="Model"
              controlMinWidth={200}
              control={(
                <SettingsSelect<string>
                  value={draft.model}
                  onValueChange={(v) => setDraft((d) => ({ ...d, model: v }))}
                  disabled={preview}
                  style={{ width: '100%' }}
                  options={modelSelectOptions}
                />
              )}
            />
            <SettingsRow
              label="Reasoning"
              controlMinWidth={200}
              control={(
                <SettingsSelect<ReasoningEffort>
                  value={draft.reasoningEffort}
                  onValueChange={(v) => setDraft((d) => ({ ...d, reasoningEffort: v }))}
                  disabled={preview}
                  style={{ width: '100%' }}
                  options={REASONING_OPTIONS}
                />
              )}
            />
            <SettingsRow
              label="Tool self-control"
              description="Whether the agent can manage its own available tools at runtime."
              controlMinWidth={200}
              control={(
                <SettingsSelect<AgentControl>
                  value={draft.tools.agentControl}
                  onValueChange={(v) => setDraft((d) => ({ ...d, tools: { ...d.tools, agentControl: v } }))}
                  disabled={preview}
                  style={{ width: '100%' }}
                  options={AGENT_CONTROL_OPTIONS}
                />
              )}
            />
            <SettingsRow
              label="Approval"
              controlMinWidth={200}
              control={(
                <SettingsSelect<ApprovalPolicy>
                  value={draft.permissions.approvalPolicy}
                  onValueChange={(v) => setDraft((d) => ({ ...d, permissions: { ...d.permissions, approvalPolicy: v } }))}
                  disabled={preview}
                  style={{ width: '100%' }}
                  options={APPROVAL_OPTIONS}
                />
              )}
            />
            <SettingsRow
              label="Approve outside workspace"
              description="Require approval before the agent touches paths outside this workspace."
              control={(
                <PillSwitch
                  checked={draft.permissions.requireApprovalOutsideWorkspace}
                  onChange={(checked) => setDraft((d) => ({ ...d, permissions: { ...d.permissions, requireApprovalOutsideWorkspace: checked } }))}
                  disabled={preview}
                  aria-label="Require approval outside workspace"
                />
              )}
            />
          </SettingsGroup>
        </Section>
      </div>
      </div>
    </div>
  )
}

function Section({ label, children, editing = false }: { label: string; children: ReactNode; editing?: boolean }): JSX.Element {
  return (
    <section className={`agent-builder-sec${editing ? ' is-agent-editing' : ''}`}>
      <div className="agent-builder-sec-label">
        {label}
        {editing && (
          <span className="agent-builder-editcursor" aria-hidden>
            <Sparkles size={11} /> editing
          </span>
        )}
      </div>
      {children}
    </section>
  )
}

interface CatalogOption {
  id: string
  label: string
  description?: string
  icon?: string
}

/**
 * Selected items as removable chips (hover → X) + a dashed "+ Add" affordance that opens a
 * searchable popover of the remaining catalog. Selecting an item adds it; the popover stays open
 * for quick multi-add. Used for the Tools (allow-list), MCP, and Skills selectors.
 */
function CatalogField({
  options,
  selected,
  onChange,
  addLabel,
  emptyHint,
  readOnly = false
}: {
  options: CatalogOption[]
  selected: string[]
  onChange: (next: string[]) => void
  addLabel: string
  emptyHint?: string
  readOnly?: boolean
}): JSX.Element {
  const [pickerOpen, setPickerOpen] = useState(false)
  const addRef = useRef<HTMLButtonElement>(null)
  const byId = useMemo(() => new Map(options.map((o) => [o.id, o])), [options])
  const addable = options.filter((o) => !selected.includes(o.id))

  if (readOnly && selected.length === 0) {
    return <span className="agent-builder-pick-empty">None</span>
  }

  return (
    <div className="agent-builder-pick">
      {!readOnly && selected.length === 0 && emptyHint && <span className="agent-builder-pick-empty">{emptyHint}</span>}
      <div className="agent-builder-chiprow">
        {selected.map((id) => {
          const option = byId.get(id)
          return (
            <span key={id} className="agent-builder-chip">
              {option?.icon && <span className="agent-builder-chip-ic" aria-hidden>{option.icon}</span>}
              <span>{option?.label ?? id}</span>
              {!readOnly && (
                <button type="button" className="agent-builder-chip-x" aria-label="remove" onClick={() => onChange(selected.filter((x) => x !== id))}>
                  <X size={12} />
                </button>
              )}
            </span>
          )
        })}
        {!readOnly && (
          <button ref={addRef} type="button" className="agent-builder-add" aria-expanded={pickerOpen} onClick={() => setPickerOpen((v) => !v)}>
            <Plus size={14} /> {addLabel}
          </button>
        )}
      </div>
      {pickerOpen && !readOnly && (
        <CatalogAddPopover
          anchor={addRef.current}
          options={addable}
          onPick={(id) => onChange([...selected, id])}
          onClose={() => setPickerOpen(false)}
        />
      )}
    </div>
  )
}

/** Portal popover anchored under the "+ Add" button: a search box + the addable catalog list. */
function CatalogAddPopover({
  anchor,
  options,
  onPick,
  onClose
}: {
  anchor: HTMLElement | null
  options: CatalogOption[]
  onPick: (id: string) => void
  onClose: () => void
}): JSX.Element | null {
  const menuRef = useRef<HTMLDivElement>(null)
  const [query, setQuery] = useState('')
  const [pos, setPos] = useState<{ top: number; left: number; width: number } | null>(null)

  useLayoutEffect(() => {
    if (!anchor) return
    const rect = anchor.getBoundingClientRect()
    const width = Math.max(rect.width, 260)
    const left = Math.max(8, Math.min(rect.left, window.innerWidth - width - 8))
    setPos({ top: rect.bottom + 6, left, width })
  }, [anchor])

  useEffect(() => {
    function onDown(event: MouseEvent): void {
      const target = event.target as Node
      if (menuRef.current?.contains(target) || anchor?.contains(target)) return
      onClose()
    }
    function onKey(event: KeyboardEvent): void {
      if (event.key === 'Escape') onClose()
    }
    document.addEventListener('mousedown', onDown, true)
    document.addEventListener('keydown', onKey, true)
    return () => {
      document.removeEventListener('mousedown', onDown, true)
      document.removeEventListener('keydown', onKey, true)
    }
  }, [anchor, onClose])

  if (!pos) return null
  const q = query.trim().toLowerCase()
  const filtered = q ? options.filter((o) => o.label.toLowerCase().includes(q) || o.id.toLowerCase().includes(q)) : options

  return createPortal(
    <div ref={menuRef} className="agent-builder-addmenu" style={{ top: pos.top, left: pos.left, width: pos.width }} role="listbox">
      <div className="agent-builder-addmenu-search">
        <Search size={13} />
        <input autoFocus value={query} placeholder="Search…" onChange={(e) => setQuery(e.target.value)} />
      </div>
      <div className="agent-builder-addmenu-list">
        {filtered.length === 0 ? (
          <div className="agent-builder-addmenu-empty">Nothing to add</div>
        ) : (
          filtered.map((o) => (
            <button key={o.id} type="button" role="option" className="agent-builder-addmenu-opt" onClick={() => onPick(o.id)}>
              {o.icon && <span className="agent-builder-addmenu-ic" aria-hidden>{o.icon}</span>}
              <span className="agent-builder-addmenu-copy">
                <span className="agent-builder-addmenu-name">{o.label}</span>
                {o.description && <span className="agent-builder-addmenu-desc">{o.description}</span>}
              </span>
            </button>
          ))
        )}
      </div>
    </div>,
    document.body
  )
}
