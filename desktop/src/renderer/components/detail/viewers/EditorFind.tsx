import { useEffect, useMemo, useRef, useState } from 'react'
import { EditorView } from '@codemirror/view'
import { findNext, findPrevious, SearchQuery, setSearchQuery } from '@codemirror/search'
import { ChevronDown, ChevronUp, X } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { IconButton } from '../../ui/IconButton'
import { Input } from '../../ui/Input'

export function EditorFind({
  view,
  revision,
  onClose
}: {
  view: EditorView
  revision: number
  onClose: () => void
}): JSX.Element {
  const t = useT()
  const [query, setQuery] = useState('')
  const input = useRef<HTMLInputElement>(null)
  useEffect(() => {
    input.current?.focus()
  }, [])
  useEffect(() => {
    view.dispatch({ effects: setSearchQuery.of(new SearchQuery({ search: query, literal: true })) })
    if (query) findNext(view)
  }, [view, query])
  useEffect(
    () => () => {
      if (view.dom.isConnected)
        view.dispatch({ effects: setSearchQuery.of(new SearchQuery({ search: '' })) })
    },
    [view]
  )
  const matches = useMemo(() => {
    if (!query) return { count: 0, index: 0 }
    const cursor = new SearchQuery({ search: query, literal: true }).getCursor(view.state.doc)
    let count = 0
    let index = 0
    for (let match = cursor.next(); !match.done; match = cursor.next()) {
      count++
      if (match.value.from <= view.state.selection.main.from) index = count
    }
    return { count, index }
  }, [query, revision, view])
  const close = () => {
    onClose()
    view.focus()
  }
  return (
    <div
      className="dc-file-editor__find"
      role="search"
      aria-label={t('find.title')}
      onKeyDown={(event) => {
        event.stopPropagation()
        if (event.key === 'Escape') {
          event.preventDefault()
          close()
        }
        if (event.key === 'Enter') {
          event.preventDefault()
          event.shiftKey ? findPrevious(view) : findNext(view)
        }
      }}
    >
      <Input
        ref={input}
        bare
        value={query}
        aria-label={t('find.title')}
        placeholder={t('find.placeholder')}
        onChange={(event) => setQuery(event.target.value)}
      />
      <span role="status">
        {query
          ? matches.count
            ? t('find.count', { index: matches.index, total: matches.count })
            : t('find.noResults')
          : ''}
      </span>
      <IconButton
        size={24}
        label={t('find.previous')}
        icon={<ChevronUp size={14} />}
        disabled={!matches.count}
        onClick={() => findPrevious(view)}
      />
      <IconButton
        size={24}
        label={t('find.next')}
        icon={<ChevronDown size={14} />}
        disabled={!matches.count}
        onClick={() => findNext(view)}
      />
      <IconButton size={24} label={t('find.close')} icon={<X size={16} />} onClick={close} />
    </div>
  )
}
