import { useState } from 'react'
import { Globe } from 'lucide-react'
import type { ConversationItem } from '../../types/conversation'
import { useLocale } from '../../contexts/LocaleContext'
import { translate } from '../../../shared/locales'
import { useConversationStore } from '../../stores/conversationStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { openConversationLink } from '../../utils/conversationDeepLink'
import { getWebSearchDisplay, getWebSearchToolDisplay, type WebSearchDisplay } from '../../utils/webSearchDisplay'
import { RunningShimmer } from '../ui/RunningShimmer'
import { ToolDisclosure } from './ToolDisclosure'
import { AnsiPre } from './AnsiPre'

const ICON = { icon: Globe }

function WebSearchSummaryRow({ display }: { display: WebSearchDisplay }): JSX.Element {
  const locale = useLocale()
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const currentThreadId = useViewerTabStore((s) => s.currentThreadId)
  const [expanded, setExpanded] = useState(false)
  const openSource = (url: string): void => {
    if (!workspacePath || !currentThreadId) return
    void openConversationLink({
      target: url,
      workspacePath,
      threadId: currentThreadId,
      t: (key) => translate(locale, key)
    })
  }
  const expandable = display.sources.length > 0 || display.errorMessage.length > 0
  const line = (
    <>
      <span className="dc-web-search-summary__label">{display.label}</span>
      {display.detail ? <span className="dc-web-search-summary__detail">{display.detail}</span> : null}
    </>
  )

  return (
    <div className="dc-web-search-row">
      <ToolDisclosure
        expanded={expandable && expanded}
        onToggle={() => setExpanded((value) => !value)}
        expandable={expandable}
        icon={ICON}
        title={display.state === 'running'
          ? <RunningShimmer className="dc-tool-row-running-label">{line}</RunningShimmer>
          : line}
      >
        {display.errorMessage ? (
          <div className="dc-tool-panel-surface" data-padded="true">
            <div className="dc-web-search-error selectable">
              <AnsiPre text={display.errorMessage} truncatedLinesOver={10} maxHeight={160} colorWhenNoSgr="var(--error)" />
            </div>
          </div>
        ) : (
          <ul className="dc-web-search-sources dc-tool-children">
            {display.sources.map((source, index) => (
              <li key={`${source.url}-${index}`}>
                <a
                  className="dc-web-search-source"
                  href={source.url}
                  title={source.url}
                  onClick={(event) => {
                    event.preventDefault()
                    openSource(source.url)
                  }}
                >
                  <span className="dc-web-search-source__domain">{source.domain}</span>
                  <span className="dc-web-search-source__title">{source.title}</span>
                </a>
              </li>
            ))}
          </ul>
        )}
      </ToolDisclosure>
    </div>
  )
}

export function WebSearchItemRow({ item }: { item: ConversationItem }): JSX.Element {
  const locale = useLocale()
  return <WebSearchSummaryRow display={getWebSearchDisplay(item, locale)} />
}

export function WebSearchToolRow({ item, running, success }: {
  item: ConversationItem
  running: boolean
  success: boolean
}): JSX.Element {
  const locale = useLocale()
  return <WebSearchSummaryRow display={getWebSearchToolDisplay(item, { running, success }, locale)} />
}
