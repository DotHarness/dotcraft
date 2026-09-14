import { useEffect } from 'react'
import { useFindStore } from '../../../stores/findStore'
import { Globe } from 'lucide-react'
import type { BrowserFindState } from '../../../../shared/viewer/browserFeedback'
import { useT } from '../../../contexts/LocaleContext'
import { FindPanel } from '../../../find/FindPanel'

export function BrowserFindBar({ tabId, state, run, active = true }: {
  tabId: string
  state: BrowserFindState
  active?: boolean
  run: (operation: () => Promise<unknown>) => void
}): JSX.Element | null {
  const t = useT()
  useEffect(() => {
    if (!state.open || !active) return
    useFindStore.getState().closeFind()
    return useFindStore.subscribe((current, previous) => {
      if (current.open && !previous.open) run(() => window.api.workspace.viewer.browser.closeFind({ tabId, restoreFocus: false }))
    })
  }, [active, state.open, tabId, run])
  if (!state.open || !active) return null
  const api = window.api.workspace.viewer.browser
  return <FindPanel label={t('viewer.browser.find')} placeholder={`${t('viewer.browser.find')}…`}
    query={state.query} hasMatches={state.total > 0}
    counter={state.total ? t('find.count', { index: state.current, total: state.total }) : t('find.noResults')}
    scope={<Globe size={16} aria-label={t('settings.browserUse.pageTitle')} />}
    onChange={query => run(() => api.find({ tabId, query }))}
    onNext={() => run(() => api.find({ tabId, query: state.query, direction: 'next' }))}
    onPrevious={() => run(() => api.find({ tabId, query: state.query, direction: 'previous' }))}
    onClose={() => run(() => api.closeFind({ tabId }))} />
}
