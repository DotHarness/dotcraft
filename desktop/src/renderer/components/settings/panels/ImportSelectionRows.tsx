import { useId, useState, type JSX, type ReactNode } from 'react'
import { Anchor, Box, FileText, FolderCog, MessagesSquare, Plug, Settings, SquareSlash } from 'lucide-react'
import type { ImportCandidate, ImportSelection } from '@dotcraft/sdk/contracts'
import { useLocale, useT } from '../../../contexts/LocaleContext'
import { McpIcon } from '../McpIcon'
import { ImportOptionRow } from './ImportOptionRow'
import {
  SETUP_CATEGORIES,
  categoryInfo,
  categoryLocation,
  describeLocation,
  importable,
  sessionsInfo,
  unavailableStatus
} from './importPresentation'
import styles from './ImportDialog.module.css'

type ImportGroup = 'user' | 'workspace' | 'sessions'
const GROUPS: readonly ImportGroup[] = ['user', 'workspace', 'sessions']

export const importItemKey = (item: Pick<ImportCandidate, 'source' | 'sourceId'>): string => `${item.source}:${item.sourceId}`

export function selectionFor(items: ImportCandidate[], selected: Set<string>): ImportSelection {
  const chosen = items.filter(item => selected.has(importItemKey(item)))
  return {
    all: false,
    user: [...new Set(chosen.filter(i => i.scope === 'user').map(i => i.category))],
    workspace: [...new Set(chosen.filter(i => i.scope === 'workspace' && i.category !== 'sessions').map(i => i.category))],
    sessions: chosen.some(i => i.category === 'sessions')
  }
}

export function importGroupIcon(group: ImportGroup): ReactNode {
  const props = { size: 18, strokeWidth: 1.8 }
  if (group === 'user') return <Settings {...props} />
  if (group === 'workspace') return <FolderCog {...props} />
  return <MessagesSquare {...props} />
}

export function importCategoryIcon(category: string): ReactNode {
  const props = { size: 16, strokeWidth: 1.8 }
  switch (category) {
    case 'skills': return <Box {...props} />
    case 'instructions': return <FileText {...props} />
    case 'commands': return <SquareSlash {...props} />
    case 'hooks': return <Anchor {...props} />
    case 'mcp': return <McpIcon {...props} />
    case 'plugins': return <Plug {...props} />
    default: return <MessagesSquare {...props} />
  }
}

function groupItems(items: ImportCandidate[], group: ImportGroup): ImportCandidate[] {
  return items.filter(item => group === 'sessions'
    ? item.category === 'sessions'
    : item.scope === group && item.category !== 'sessions')
}

export function ImportSelectionRows({ items, selected, onChange, disabled, workspaceName, workspacePath }: {
  items: ImportCandidate[]
  selected: Set<string>
  onChange: (next: Set<string>) => void
  disabled: boolean
  workspaceName: string
  workspacePath?: string
}): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const listId = useId()
  const present = GROUPS.filter(group => groupItems(items, group).length > 0)
  const [expanded, setExpanded] = useState<Set<ImportGroup>>(
    () => new Set(present.filter(group => group !== 'sessions').slice(0, 1))
  )

  function toggle(list: ImportCandidate[], checked: boolean): void {
    const next = new Set(selected)
    for (const item of list.filter(importable)) {
      if (checked) next.add(importItemKey(item))
      else next.delete(importItemKey(item))
    }
    onChange(next)
  }

  function selectionState(list: ImportCandidate[]): { checked: boolean; indeterminate: boolean; empty: boolean } {
    const ready = list.filter(importable)
    const count = ready.filter(item => selected.has(importItemKey(item))).length
    return { checked: ready.length > 0 && count === ready.length, indeterminate: count > 0 && count < ready.length, empty: ready.length === 0 }
  }

  return (
    <div className={styles.options}>
      {present.map(group => {
        const content = groupItems(items, group)
        const state = selectionState(content)
        if (group === 'sessions') {
          const title = `${t('settings.import.category.sessions')} (${content.filter(importable).length})`
          return (
            <div key={group} className={styles.group}>
              <ImportOptionRow
                icon={importGroupIcon(group)}
                title={title}
                description={t('settings.import.setup.sessionsHint', { project: workspaceName })}
                info={sessionsInfo(content, locale, t)}
                infoLabel={t('settings.import.setup.itemInfo', { item: title })}
                checked={state.checked}
                indeterminate={state.indeterminate}
                disabled={disabled || state.empty}
                onChange={checked => toggle(content, checked)}
              />
            </div>
          )
        }

        const label = t(`settings.import.setup.${group}`)
        const open = expanded.has(group)
        const nestedId = `${listId}-${group}`
        return (
          <div key={group} className={styles.group}>
            <ImportOptionRow
              icon={importGroupIcon(group)}
              title={label}
              description={t(`settings.import.setup.${group}Hint`, { project: workspaceName })}
              expanded={open}
              expandLabel={t(open ? 'settings.import.setup.hideDetails' : 'settings.import.setup.showDetails', { group: label })}
              controlsId={nestedId}
              onToggleExpanded={() => setExpanded(previous => {
                const next = new Set(previous)
                if (open) next.delete(group)
                else next.add(group)
                return next
              })}
              checked={state.checked}
              indeterminate={state.indeterminate}
              disabled={disabled || state.empty}
              onChange={checked => toggle(content, checked)}
            />
            {open && (
              <div id={nestedId} role="group" aria-label={label} className={styles.nested}>
                {SETUP_CATEGORIES.map(category => {
                  const list = content.filter(item => item.category === category)
                  if (list.length === 0) return null
                  const categoryState = selectionState(list)
                  const title = `${t(`settings.import.category.${category}`)} (${list.filter(importable).length})`
                  return (
                    <ImportOptionRow
                      key={category}
                      nested
                      icon={importCategoryIcon(category)}
                      title={title}
                      description={categoryState.empty
                        ? unavailableStatus(list, t)
                        : describeLocation(categoryLocation(list, workspacePath), t)}
                      info={categoryInfo(category, list, locale, t)}
                      infoLabel={t('settings.import.setup.itemInfo', { item: title })}
                      checked={categoryState.checked}
                      indeterminate={categoryState.indeterminate}
                      disabled={disabled || categoryState.empty}
                      onChange={checked => toggle(list, checked)}
                    />
                  )
                })}
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}
