import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { Sparkle, Terminal } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import type { CustomCommandInfo } from '../../hooks/useCustomCommandCatalog'
import { ActionTooltip } from '../ui/ActionTooltip'
import {
  MentionRowIcon,
  MentionSectionHeader,
  mentionEmptyStyle,
  mentionPopoverContainerStyle,
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
  skills?: SlashSkillInfo[]
  onSelectSystemAction?: (actionId: string) => void
  onSelectCommand: (commandName: string) => void
  onSelectSkill?: (skillName: string) => void
  onDismiss: () => void
  constrainToAnchor?: boolean
}

export function CommandSearchPopover({
  query,
  visible,
  loading,
  systemActions,
  commands,
  skills,
  onSelectSystemAction,
  onSelectCommand,
  onSelectSkill,
  onDismiss,
  constrainToAnchor = false
}: CommandSearchPopoverProps): JSX.Element | null {
  const t = useT()
  const skillList = skills ?? []
  const systemActionList = systemActions ?? []
  const [highlight, setHighlight] = useState(0)
  const filteredSystemActions = useMemo(() => {
    const prefix = query.toLowerCase()
    if (!prefix) return systemActionList
    return systemActionList.filter((action) => {
      if (action.label.toLowerCase().startsWith(prefix)) return true
      if (action.id.toLowerCase().startsWith(prefix)) return true
      return (action.keywords ?? []).some((keyword) => keyword.toLowerCase().startsWith(prefix))
    })
  }, [query, systemActionList])
  const filteredCommands = useMemo(() => {
    const prefix = query.toLowerCase()
    if (!prefix) return commands
    return commands.filter((cmd) => {
      if (cmd.name.slice(1).toLowerCase().startsWith(prefix)) return true
      return cmd.aliases.some((alias) => {
        const bare = alias.startsWith('/') ? alias.slice(1) : alias
        return bare.toLowerCase().startsWith(prefix)
      })
    })
  }, [commands, query])
  const filteredSkills = useMemo(() => {
    const prefix = query.toLowerCase()
    if (!prefix) return skillList
    return skillList.filter((skill) => skill.name.toLowerCase().startsWith(prefix))
  }, [query, skillList])
  const entries = useMemo(
    () => [
      ...filteredSystemActions.map((action) => ({ type: 'system' as const, action })),
      ...filteredCommands.map((command) => ({ type: 'command' as const, command })),
      ...filteredSkills.map((skill) => ({ type: 'skill' as const, skill }))
    ],
    [filteredCommands, filteredSkills, filteredSystemActions]
  )

  useEffect(() => {
    setHighlight(0)
  }, [entries, query])

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
        setHighlight((h) => Math.min(entries.length - 1, h + 1))
      } else if (e.key === 'ArrowUp') {
        e.preventDefault()
        e.stopPropagation()
        setHighlight((h) => Math.max(0, h - 1))
      } else if (e.key === 'Enter' || e.key === 'Tab') {
        e.preventDefault()
        e.stopPropagation()
        const item = entries[highlight]
        if (!item) return
        if (item.type === 'system') onSelectSystemAction?.(item.action.id)
        else if (item.type === 'command') onSelectCommand(item.command.name)
        else onSelectSkill?.(item.skill.name)
      }
    }
    window.addEventListener('keydown', onKey, true)
    return () => {
      window.removeEventListener('keydown', onKey, true)
    }
  }, [entries, highlight, onDismiss, onSelectCommand, onSelectSkill, onSelectSystemAction, visible])

  if (!visible) return null

  return (
    <div
      role="listbox"
      style={mentionPopoverContainerStyle({
        constrainToAnchor,
        minWidth: '320px',
        maxWidth: '480px',
        maxHeight: '280px'
      })}
    >
      {loading && <div style={mentionEmptyStyle}>{t('slashSearch.loading')}</div>}
      {!loading && entries.length === 0 && query.trim() !== '' && (
        <div style={mentionEmptyStyle}>{t('slashSearch.noMatch')}</div>
      )}
      {!loading && entries.length === 0 && query.trim() === '' && (
        <div style={mentionEmptyStyle}>{t('slashSearch.hint')}</div>
      )}
      {!loading && filteredSystemActions.length > 0 && (
        <MentionSectionHeader label={t('slashSearch.systemGroup')} />
      )}
      {!loading &&
        filteredSystemActions.map((action) => {
          const index = entries.findIndex((entry) => entry.type === 'system' && entry.action.id === action.id)
          return (
            <ActionTooltip key={action.id} label={action.description} wrapperStyle={{ display: 'block', width: '100%' }}>
              <button
                type="button"
                role="option"
                aria-selected={index === highlight}
                onMouseEnter={() => {
                  setHighlight(index)
                }}
                onClick={() => {
                  onSelectSystemAction?.(action.id)
                }}
                style={mentionRowStyle(index === highlight)}
              >
                <MentionRowIcon tint="var(--info)">{action.icon}</MentionRowIcon>
                <span style={mentionRowNameStyle}>{highlightMatch(action.label, query)}</span>
                <span style={mentionRowDescStyle}>{action.description}</span>
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
                aria-selected={index === highlight}
                onMouseEnter={() => {
                  setHighlight(index)
                }}
                onClick={() => {
                  onSelectCommand(cmd.name)
                }}
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
                aria-selected={index === highlight}
                onMouseEnter={() => {
                  setHighlight(index)
                }}
                onClick={() => {
                  onSelectSkill?.(skill.name)
                }}
                style={mentionRowStyle(index === highlight)}
              >
                <MentionRowIcon tint="var(--ref-skill)">
                  <Sparkle size={15} strokeWidth={2} aria-hidden />
                </MentionRowIcon>
                <span style={mentionRowNameStyle}>{highlightMatch(skill.name, query)}</span>
                <span style={mentionRowDescStyle}>{description}</span>
              </button>
            </ActionTooltip>
          )
        })}
    </div>
  )
}

function highlightMatch(name: string, query: string): JSX.Element {
  const target = name.replace(/^\/+/, '')
  const lower = target.toLowerCase()
  const lowerQuery = query.toLowerCase()
  const idx = lower.indexOf(lowerQuery)
  if (!query || idx < 0) return <>{target}</>
  return (
    <>
      {target.slice(0, idx)}
      <span style={{ color: 'var(--accent)' }}>{target.slice(idx, idx + query.length)}</span>
      {target.slice(idx + query.length)}
    </>
  )
}
