import { useEffect, useRef, type JSX, type ReactNode } from 'react'
import { Link2 } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'
import type { AppInfo } from '../../stores/appBindingStore'

/** Apps shown in conversation pickers are installed, enabled, and ready to bind. */
export function isAppReadyForBindingPicker(app: AppInfo): boolean {
  return app.installed
    && app.enabled
    && (app.requiresExternalConnection === false || app.connectionState === 'connected')
}

interface AppBindingsPickerProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  disabled?: boolean
  loading?: boolean
  error?: string | null
  empty?: boolean
  emptyLabel: string
  onRetry?: () => void
  onOpen?: () => void
  children: ReactNode
}

/** Shared Apps trigger and popover shell used before and after thread creation. */
export function AppBindingsPicker({
  open,
  onOpenChange,
  disabled = false,
  loading = false,
  error,
  empty = false,
  emptyLabel,
  onRetry,
  onOpen,
  children
}: AppBindingsPickerProps): JSX.Element {
  const t = useT()
  const rootRef = useRef<HTMLDivElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (!open) return
    const handlePointerDown = (event: PointerEvent): void => {
      if (rootRef.current?.contains(event.target as Node)) return
      onOpenChange(false)
    }
    const handleKeyDown = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return
      onOpenChange(false)
      window.setTimeout(() => triggerRef.current?.focus(), 0)
    }
    window.addEventListener('pointerdown', handlePointerDown)
    window.addEventListener('keydown', handleKeyDown)
    return () => {
      window.removeEventListener('pointerdown', handlePointerDown)
      window.removeEventListener('keydown', handleKeyDown)
    }
  }, [onOpenChange, open])

  function toggle(): void {
    const next = !open
    onOpenChange(next)
    if (next) onOpen?.()
  }

  return (
    <div ref={rootRef} className="dc-app-bindings-picker">
      <IconButton
        ref={triggerRef}
        className="dc-app-bindings-picker__trigger"
        label={t('appBinding.title')}
        tooltipLabel={t('appBinding.title')}
        tooltipPlacement="bottom"
        size={28}
        aria-haspopup="dialog"
        aria-expanded={open}
        disabled={disabled}
        onClick={toggle}
        icon={<Link2 size={15} aria-hidden />}
      />
      {open && (
        <div className="dc-app-bindings-picker__popover" role="dialog" aria-label={t('appBinding.title')}>
          <strong className="dc-app-bindings-picker__title">{t('appBinding.title')}</strong>
          {error && (
            <div className="dc-app-bindings-picker__error" role="alert">
              <span>{error}</span>
              {onRetry && (
                <Button size="sm" variant="secondary" disabled={loading} onClick={onRetry}>
                  {t('common.retry')}
                </Button>
              )}
            </div>
          )}
          {loading && empty && <div className="dc-app-bindings-picker__empty" role="status">{t('appBinding.loading')}</div>}
          {!loading && empty && <div className="dc-app-bindings-picker__empty">{emptyLabel}</div>}
          <div className="dc-app-bindings-picker__list">{children}</div>
        </div>
      )}
    </div>
  )
}

interface AppBindingPickerRowProps {
  icon: ReactNode
  title: string
  subtitle?: ReactNode
  action?: ReactNode
  details?: ReactNode
}

/** Shared visual row; surface containers own state mapping and side effects. */
export function AppBindingPickerRow({
  icon,
  title,
  subtitle,
  action,
  details
}: AppBindingPickerRowProps): JSX.Element {
  return (
    <div className="dc-app-binding-row">
      {icon}
      <div className="dc-app-binding-row__main">
        <strong className="dc-app-binding-row__title">{title}</strong>
        {subtitle && <div className="dc-app-binding-row__subtitle">{subtitle}</div>}
      </div>
      {action && <div className="dc-app-binding-row__action">{action}</div>}
      {details && <div className="dc-app-binding-row__details">{details}</div>}
    </div>
  )
}
