import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ChevronDown, ChevronUp, Search, X } from 'lucide-react'
import { Input } from '../components/ui/Input'
import { IconButton } from '../components/ui/IconButton'
import { useT } from '../contexts/LocaleContext'
import css from './FindOverlay.module.css'

export function FindPanel({ label, placeholder, query, counter, hasMatches, scope, onChange, onNext, onPrevious, onClose }: {
  label: string
  placeholder: string
  query: string
  counter: string
  hasMatches: boolean
  scope?: ReactNode
  onChange: (query: string) => void
  onNext: () => void
  onPrevious: () => void
  onClose: () => void
}): JSX.Element {
  const t = useT()
  const field = useRef<HTMLInputElement>(null)
  const [right, setRight] = useState(16)
  useLayoutEffect(() => {
    field.current?.select()
    const controls = document.querySelector<HTMLElement>('[data-window-controls]')
    const measure = () => setRight(16 + (controls ? window.innerWidth - controls.getBoundingClientRect().left : 0))
    measure()
    window.addEventListener('resize', measure)
    return () => window.removeEventListener('resize', measure)
  }, [])
  return createPortal(
    <div className={css.overlay} style={{ right }} role="search" aria-label={label}>
      <div className={css.field}>
        <Search size={16} aria-hidden />
        <Input ref={field} bare className={css.input} value={query} aria-label={label} placeholder={placeholder}
          onChange={event => onChange(event.target.value)} onKeyDown={event => {
            if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); onClose() }
            if (event.key === 'Enter') { event.preventDefault(); event.stopPropagation(); event.shiftKey ? onPrevious() : onNext() }
            if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'f') {
              event.preventDefault(); event.stopPropagation(); field.current?.select()
            }
          }} />
      </div>
      {scope && <div className={css.scope}>{scope}</div>}
      <div className={css.close}>
        <IconButton size={24} icon={<X size={16} />} label={t('find.close')} onClick={onClose} />
      </div>
      {query.trim() && <div className={css.results}>
        <IconButton size={20} icon={<ChevronUp size={16} />} label={t('find.previous')} disabled={!hasMatches} onClick={onPrevious} />
        <IconButton size={20} icon={<ChevronDown size={16} />} label={t('find.next')} disabled={!hasMatches} onClick={onNext} />
        <span className={css.counter} role="status">{counter}</span>
      </div>}
    </div>, document.body
  )
}
