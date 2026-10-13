import { Fragment, type ReactNode } from 'react'
import { useLocale } from '../../contexts/LocaleContext'
import { translate } from '../../../shared/locales'
import type { SubAgentLookupSources } from '../../utils/subAgentIdentity'
import { findSubAgentChild } from '../../utils/subAgentIdentity'
import { openSubAgent } from '../../utils/subAgentNavigation'
import { boardAgentName, isRootAgentPath } from '../../utils/boardToolDisplay'
import { RobotAvatar } from '../agents/RobotAvatar'
import styles from './BoardToolCard.module.css'

export interface BoardAgentContext {
  threadId: string
  lookup: SubAgentLookupSources
}

export function BoardAgentName({
  path,
  context,
  mark = false
}: {
  path: string
  context: BoardAgentContext
  mark?: boolean
}): JSX.Element {
  const locale = useLocale()
  if (isRootAgentPath(path)) return <span className={styles.rootAgent}>{boardAgentName(path, locale)}</span>
  const child = findSubAgentChild(context.lookup, null, path, 'tree')
  const name = child?.nickname ?? boardAgentName(path, locale)
  return (
    <span className={styles.agent}>
      {mark && (
        <span className="dc-subagent-mark" aria-hidden>
          <RobotAvatar name={name} size={16} />
        </span>
      )}
      <button
        type="button"
        className="dc-subagent-name"
        onClick={(event) => {
          event.stopPropagation()
          openSubAgent(context.threadId, child?.childThreadId)
        }}
        aria-label={translate(locale, 'subagentsPanel.openAria', { name })}
      >
        {name}
      </button>
    </span>
  )
}

export function BoardAgentList({ paths, context }: { paths: string[]; context: BoardAgentContext }): JSX.Element {
  const locale = useLocale()
  const parts = new Intl.ListFormat(locale, { style: 'long', type: 'conjunction' })
    .formatToParts(paths.map((path) => boardAgentName(path, locale)))
  let index = -1
  return (
    <>
      {parts.map((part, position) => {
        if (part.type !== 'element') return <Fragment key={`sep-${position}`}>{part.value}</Fragment>
        index += 1
        return <BoardAgentName key={paths[index]} path={paths[index]} context={context} mark />
      })}
    </>
  )
}

const TOKEN = '__DOTCRAFT_BOARD_TOKEN__'

/** Puts a node where one translated placeholder goes, whatever the locale's word order. */
export function renderWithNode(
  translateTemplate: (placeholder: string) => string,
  node: ReactNode
): ReactNode {
  const parts = translateTemplate(TOKEN).split(TOKEN)
  if (parts.length === 1) return parts[0]
  return parts.map((part, index) => (
    <Fragment key={index}>
      {part}
      {index < parts.length - 1 && node}
    </Fragment>
  ))
}
