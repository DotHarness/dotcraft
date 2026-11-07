import { useEffect, useRef, useState, type CSSProperties, type JSX, type KeyboardEvent } from 'react'
import { Search, X } from 'lucide-react'

import { useT } from '../../contexts/LocaleContext'
import { Input } from '../ui/Input'

export function SettingsSearchField({
  value,
  autoFocus = false,
  onChange,
  onKeyDown
}: {
  value: string
  autoFocus?: boolean
  onChange: (value: string) => void
  onKeyDown: (event: KeyboardEvent<HTMLInputElement>) => void
}): JSX.Element {
  const t = useT()
  const [focused, setFocused] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (autoFocus) inputRef.current?.focus()
  }, [autoFocus])

  useEffect(() => {
    const focusSearch = (event: globalThis.KeyboardEvent): void => {
      if (!(event.ctrlKey || event.metaKey) || event.shiftKey || event.altKey) return
      if (event.key.toLowerCase() !== 'f') return
      if (event.target instanceof Element && event.target.closest('[role="dialog"], [aria-modal="true"]')) return
      event.preventDefault()
      event.stopImmediatePropagation()
      inputRef.current?.focus()
      inputRef.current?.select()
    }
    window.addEventListener('keydown', focusSearch, true)
    return () => window.removeEventListener('keydown', focusSearch, true)
  }, [])

  return (
    <div
      onFocusCapture={() => setFocused(true)}
      onBlurCapture={() => setFocused(false)}
      style={{ ...shellStyle, borderColor: focused ? 'var(--border-active)' : 'transparent' }}
    >
      <Search size={15} aria-hidden style={{ flexShrink: 0 }} />
      <Input
        bare
        ref={inputRef}
        role="searchbox"
        aria-label={t('settings.search.label')}
        value={value}
        placeholder={t('settings.search.placeholder')}
        onChange={(event) => onChange(event.target.value)}
        onKeyDown={onKeyDown}
        style={inputStyle}
      />
      {value && (
        <button
          type="button"
          aria-label={t('settings.search.clear')}
          onClick={() => {
            onChange('')
            inputRef.current?.focus()
          }}
          style={clearStyle}
        >
          <X size={14} aria-hidden />
        </button>
      )}
    </div>
  )
}

const shellStyle: CSSProperties = {
  flexShrink: 0,
  height: '36px',
  margin: '0 4px',
  boxSizing: 'border-box',
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  padding: '0 11px',
  borderRadius: '999px',
  border: '1px solid transparent',
  backgroundColor: 'var(--bg-hover)',
  color: 'var(--text-secondary)'
}

const inputStyle: CSSProperties = {
  width: '100%',
  minWidth: 0,
  border: 'none',
  outline: 'none',
  backgroundColor: 'transparent',
  color: 'var(--text-primary)',
  fontSize: 'var(--type-ui-size)'
}

const clearStyle: CSSProperties = {
  display: 'flex',
  flexShrink: 0,
  padding: 0,
  border: 'none',
  background: 'transparent',
  color: 'var(--text-dimmed)',
  cursor: 'pointer'
}
