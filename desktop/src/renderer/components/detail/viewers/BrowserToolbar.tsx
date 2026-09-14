import { type FormEvent, type JSX, type ReactNode, useEffect, useState } from 'react'
import { ArrowLeft, ArrowRight, ExternalLink, RotateCw, Square } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { IconButton } from '../../ui/IconButton'
import { Input } from '../../ui/Input'

export interface BrowserToolbarProps {
  url: string
  loading: boolean
  canGoBack: boolean
  canGoForward: boolean
  onBack: () => void
  onForward: () => void
  onReload: () => void
  onStop: () => void
  onNavigate: (url: string) => void
  onOpenExternal: () => void
  onAddressDismiss?: () => void
  annotate?: ReactNode
  controls?: ReactNode
}

export function BrowserToolbar({
  url,
  loading,
  canGoBack,
  canGoForward,
  onBack,
  onForward,
  onReload,
  onStop,
  onNavigate,
  onOpenExternal,
  onAddressDismiss,
  annotate,
  controls
}: BrowserToolbarProps): JSX.Element {
  const t = useT()
  const [value, setValue] = useState(url)
  const [editing, setEditing] = useState(false)

  useEffect(() => {
    if (!editing) setValue(url)
  }, [url, editing])

  function submit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault()
    onNavigate(value)
    setEditing(false)
  }

  const reloadLabel = loading ? t('viewer.browser.stop') : t('viewer.browser.reload')
  return (
    <div className="dc-browser-toolbar">
      <NavigationButton
        label={t('viewer.browser.back')}
        icon={<ArrowLeft size={16} aria-hidden />}
        disabled={!canGoBack}
        onClick={onBack}
      />
      <NavigationButton
        label={t('viewer.browser.forward')}
        icon={<ArrowRight size={16} aria-hidden />}
        disabled={!canGoForward}
        onClick={onForward}
      />
      <NavigationButton
        label={reloadLabel}
        icon={loading ? <Square size={14} aria-hidden /> : <RotateCw size={16} aria-hidden />}
        onClick={loading ? onStop : onReload}
      />
      <form className="dc-browser-toolbar__address" onSubmit={submit}>
        <Input
          size="toolbar"
          frameless
          className="dc-browser-toolbar__field"
          value={value}
          onFocus={() => setEditing(true)}
          onBlur={() => setEditing(false)}
          onChange={(event) => setValue(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              event.preventDefault()
              setEditing(false)
              onAddressDismiss?.()
            }
          }}
          placeholder={t('viewer.browser.urlPlaceholder')}
          autoCapitalize="off"
          autoCorrect="off"
        />
        <span className="dc-browser-toolbar__external">
          <IconButton
            size={24}
            radius={8}
            icon={<ExternalLink size={14} aria-hidden />}
            label={t('viewer.browser.openExternal')}
            tooltipLabel={t('viewer.browser.openExternal')}
            tooltipPlacement="bottom"
            onClick={onOpenExternal}
          />
        </span>
      </form>
      <div className="dc-browser-toolbar__actions">
        {annotate}
        {controls}
      </div>
      {loading && <div className="dc-browser-toolbar__progress" aria-hidden="true" />}
    </div>
  )
}

function NavigationButton({
  label,
  icon,
  disabled,
  onClick
}: {
  label: string
  icon: ReactNode
  disabled?: boolean
  onClick: () => void
}): JSX.Element {
  return (
    <IconButton
      icon={icon}
      label={label}
      tooltipLabel={label}
      tooltipPlacement="bottom"
      disabledReason={label}
      disabled={disabled}
      onClick={onClick}
    />
  )
}
