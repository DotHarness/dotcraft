import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Box, Terminal } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import type { CustomCommandInfo } from '../../hooks/useCustomCommandCatalog'
import {
  resolveDesktopPluginLabel,
  type ActiveDesktopPluginCommand
} from '../../plugins/desktopPluginRegistry'
import { resolveDesktopPluginIcon } from '../desktopPlugins/DesktopPluginIcon'
import { fuzzyFilter, fuzzyHighlightRanges } from '../../utils/fuzzyMatch'
import { ActionTooltip } from '../ui/ActionTooltip'
import {
  MentionRowIcon,
  MentionSectionHeader,
  mentionEmptyStyle,
  MentionPopoverSurface,
  mentionRowDescStyle,
  mentionRowNameStyle,
  mentionRowStyle
} from './mentionPopoverUi'

export interface SlashSystemActionInfo {
  id: string
  label: string
  description: string
  keywords?: string[]
  icon?: ReactNode
}

export interface SlashSkillInfo {
  name: string
  description: string
}

interface CommandSearchPopoverProps {
  query: string
  visible: boolean
  loading: boolean
  systemActions?: SlashSystemActionInfo[]
  commands: CustomCommandInfo[]
  desktopCommands?: readonly ActiveDesktopPluginCommand[]
  skills?: SlashSkillInfo[]
  onSelectSystemAction?: (actionId: string) => void
  onSelectCommand: (commandName: string) => void
  onSelectDesktopCommand?: (contributionKey: string) => void
  onSelectSkill?: (skillName: string) => void
  onDismiss: () => void
}

const EMPTY_SYSTEM_ACTIONS: SlashSystemActionInfo[] = []
const EMPTY_DESKTOP_COMMANDS: readonly ActiveDesktopPluginCommand[] = []
const EMPTY_SKILLS: SlashSkillInfo[] = []

export function CommandSearchPopover({
  query,
  visible,
  loading,
  systemActions,
  commands,
  desktopCommands,
  skills,
  onSelectSystemAction,
  onSelectCommand,
  onSelectDesktopCommand,
  onSelectSkill,
  onDismiss
}: CommandSearchPopoverProps): JSX.Element | null {
  const t = useT()
  const locale = useLocale()
  const skillList = skills ?? EMPTY_SKILLS
  const desktopCommandList = desktopCommands ?? EMPTY_DESKTOP_COMMANDS
  const systemActionList = systemActions ?? EMPTY_SYSTEM_ACTIONS
  const [highlight, setHighlight] = useState(0)
  const containerRef = useRef<HTMLDivElement>(null)
  const keyboardNavRef = useRef(false)
  const filteredSystemActions = useMemo(
    () => fuzzyFilter(systemActionList, query, (action) => [action.label, action.id, ...(action.keywords ?? [])]),
    [query, systemActionList]
  )
  const filteredCommands = useMemo(
    () => fuzzyFilter(commands, query, (cmd) => [cmd.name.replace(/^\/+/, ''), ...cmd.aliases.map((alias) => alias.replace(/^\/+/, ''))]),
    [commands, query]
  )
  const filteredDesktopCommands = useMemo(
    () => fuzzyFilter(desktopCommandList, query, (command) => [resolveDesktopPluginLabel(command.label, locale), command.id]),
    [desktopCommandList, locale, query]
  )
  const filteredSkills = useMemo(
    () => fuzzyFilter(skillList, query, (skill) => [skill.name]),
    [query, skillList]
  )
  const entries = useMemo(
    () => [
      ...filteredSystemActions.map((action) => ({ type: 'system' as const, action })),
      ...filteredDesktopCommands.map((command) => ({ type: 'desktopCommand' as const, command })),
      ...filteredCommands.map((command) => ({ type: 'command' as const, command })),
      ...filteredSkills.map((skill) => ({ type: 'skill' as const, skill }))
    ],
    [filteredCommands, filteredDesktopCommands, filteredSkills, filteredSystemActions]
  )

  useEffect(() => {
    if (visible) setHighlight(0)
  }, [query, visible])

  useEffect(() => {
    setHighlight((current) => Math.min(current, Math.max(0, entries.length - 1)))
  }, [entries.length])

  // Only Arrow keys may move the list. Hover also sets `highlight`, and `entries`
  // changes identity on unrelated re-renders, so scrolling on every change would
  // yank a list the user is reading back to the highlighted row.
  useEffect(() => {
    if (!visible || !keyboardNavRef.current) return
    keyboardNavRef.current = false
    const active = containerRef.current?.querySelector(`[data-entry-index="${highlight}"]`)
    if (active instanceof HTMLElement && typeof active.scrollIntoView === 'function') {
      active.scrollIntoView({ block: 'nearest' })
    }
  }, [highlight, visible, entries])

  useEffect(() => {
    if (!visible) return
    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') {
        e.preventDefault()
        e.stopPropagation()
        onDismiss()
        return
      }
      if (entries.length === 0) return
      if (e.key === 'ArrowDown') {
        e.preventDefault()
        e.stopPropagation()
        keyboardNavRef.current = true
        setHighlight((h) => Math.min(entries.length - 1, h + 1))
      } else if (e.key === 'ArrowUp') {
        e.preventDefault()
        e.stopPropagation()
        keyboardNavRef.current = true
        setHighlight((h) => Math.max(0, h - 1))
      } else if (e.key === 'Enter' || e.key === 'Tab') {
        e.preventDefault()
        e.stopPropagation()
        const item = entries[highlight]
        if (!item) return
        if (item.type === 'system') onSelectSystemAction?.(item.action.id)
        else if (item.type === 'desktopCommand') onSelectDesktopCommand?.(item.command.contributionKey)
        else if (item.type === 'command') onSelectCommand(item.command.name)
        else onSelectSkill?.(item.skill.name)
      }
    }
    window.addEventListener('keydown', onKey, true)
    return () => {
      window.removeEventListener('keydown', onKey, true)
    }
  }, [entries, highlight, onDismiss, onSelectCommand, onSelectDesktopCommand, onSelectSkill, onSelectSystemAction, visible])

  if (!visible) return null

  return (
    <MentionPopoverSurface
      popupRef={containerRef}
      open={visible}
      role="listbox"
      maxHeight={280}
    >
      {loading && <div style={mentionEmptyStyle}>{t('slashSearch.loading')}</div>}
      {!loading && entries.length === 0 && query.trim() !== '' && (
        <div style={mentionEmptyStyle}>{t('slashSearch.noMatch')}</div>
      )}
      {!loading && entries.length === 0 && query.trim() === '' && (
        <div style={mentionEmptyStyle}>{t('slashSearch.hint')}</div>
      )}
      {/* Deliberately headerless: a header above the very first row only pushes it down. */}
      {!loading &&
        filteredSystemActions.map((action) => {
          const index = entries.findIndex((entry) => entry.type === 'system' && entry.action.id === action.id)
          return (
            <ActionTooltip key={action.id} label={action.description} wrapperStyle={{ display: 'block', width: '100%' }}>
              <button
                type="button"
                role="option"
                data-entry-index={index}
                aria-selected={index === highlight}
                onMouseEnter={() => {
                  setHighlight(index)
                }}
                onClick={() => {
                  onSelectSystemAction?.(action.id)
                }}
                className="dotcraft-sidebar-row-radius"
                style={mentionRowStyle(index === highlight)}
              >
                <MentionRowIcon tint="var(--info)">{action.icon}</MentionRowIcon>
                <span style={mentionRowNameStyle}>{highlightMatch(action.label, query)}</span>
                <span style={mentionRowDescStyle}>{action.description}</span>
              </button>
            </ActionTooltip>
          )
        })}
      {!loading && filteredDesktopCommands.length > 0 && (
        <MentionSectionHeader label={t('slashSearch.desktopCommandsGroup')} />
      )}
      {!loading &&
        filteredDesktopCommands.map((command) => {
          const index = entries.findIndex((entry) =>
            entry.type === 'desktopCommand' && entry.command.contributionKey === command.contributionKey
          )
          const Icon = resolveDesktopPluginIcon(command.icon)
          const label = resolveDesktopPluginLabel(command.label, locale)
          const description = command.description
            ? resolveDesktopPluginLabel(command.description, locale)
            : null
          const detail = description
            ? `${description} · ${command.host.plugin.displayName}`
            : command.host.plugin.displayName
          return (
            <ActionTooltip
              key={command.contributionKey}
              label={detail}
              wrapperStyle={{ display: 'block', width: '100%' }}
            >
              <button
                type="button"
                role="option"
                data-entry-index={index}
                aria-selected={index === highlight}
                onMouseEnter={() => setHighlight(index)}
                onClick={() => onSelectDesktopCommand?.(command.contributionKey)}
                className="dotcraft-sidebar-row-radius"
                style={mentionRowStyle(index === highlight)}
              >
                <MentionRowIcon tint="var(--accent)">
                  <Icon size={15} strokeWidth={2} aria-hidden />
                </MentionRowIcon>
                <span style={mentionRowNameStyle}>{highlightMatch(label, query)}</span>
                <span style={mentionRowDescStyle}>{detail}</span>
              </button>
            </ActionTooltip>
          )
        })}
      {!loading && filteredCommands.length > 0 && (
        <MentionSectionHeader label={t('slashSearch.commandsGroup')} />
      )}
      {!loading &&
        filteredCommands.map((cmd) => {
          const index = entries.findIndex((entry) => entry.type === 'command' && entry.command.name === cmd.name)
          const description = cmd.description || t('slashSearch.noDescription')
          return (
            <ActionTooltip key={cmd.name} label={description} wrapperStyle={{ display: 'block', width: '100%' }}>
              <button
                type="button"
                role="option"
                data-entry-index={index}
                aria-selected={index === highlight}
                onMouseEnter={() => {
                  setHighlight(index)
                }}
                onClick={() => {
                  onSelectCommand(cmd.name)
                }}
                className="dotcraft-sidebar-row-radius"
                style={mentionRowStyle(index === highlight)}
              >
                <MentionRowIcon tint="var(--accent)">
                  <Terminal size={15} strokeWidth={2} aria-hidden />
                </MentionRowIcon>
                <span style={mentionRowNameStyle}>{highlightMatch(cmd.name.replace(/^\/+/, ''), query)}</span>
                <span style={mentionRowDescStyle}>{description}</span>
              </button>
            </ActionTooltip>
          )
        })}
      {!loading && filteredSkills.length > 0 && (
        <MentionSectionHeader label={t('slashSearch.skillsGroup')} />
      )}
      {!loading &&
        filteredSkills.map((skill) => {
          const index = entries.findIndex((entry) => entry.type === 'skill' && entry.skill.name === skill.name)
          const description = skill.description || t('slashSearch.noDescription')
          return (
            <ActionTooltip key={skill.name} label={description} wrapperStyle={{ display: 'block', width: '100%' }}>
              <button
                type="button"
                role="option"
                data-entry-index={index}
                aria-selected={index === highlight}
                onMouseEnter={() => {
                  setHighlight(index)
                }}
                onClick={() => {
                  onSelectSkill?.(skill.name)
                }}
                className="dotcraft-sidebar-row-radius"
                style={mentionRowStyle(index === highlight)}
              >
                <MentionRowIcon tint="var(--ref-skill)">
                  <Box size={15} strokeWidth={2} aria-hidden />
                </MentionRowIcon>
                <span style={mentionRowNameStyle}>{highlightMatch(skill.name, query)}</span>
                <span style={mentionRowDescStyle}>{description}</span>
              </button>
            </ActionTooltip>
          )
        })}
    </MentionPopoverSurface>
  )
}

function highlightMatch(name: string, query: string): JSX.Element {
  const target = name.replace(/^\/+/, '')
  const ranges = fuzzyHighlightRanges(target, query)
  if (ranges.length === 0) return <>{target}</>
  const parts: ReactNode[] = []
  let cursor = 0
  for (const range of ranges) {
    if (range.start > cursor) parts.push(target.slice(cursor, range.start))
    parts.push(<span key={range.start} style={{ color: 'var(--accent)' }}>{target.slice(range.start, range.end)}</span>)
    cursor = range.end
  }
  if (cursor < target.length) parts.push(target.slice(cursor))
  return <>{parts}</>
}
