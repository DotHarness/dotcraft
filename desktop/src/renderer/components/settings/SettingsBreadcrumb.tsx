import type { JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { BreadcrumbSeparator, styles as catalogStyles } from '../catalog/CatalogSurface'
import { Button } from '../ui/Button'

interface SettingsBreadcrumbProps {
  /** Label of the list page the back button returns to. */
  parentLabel: string
  currentLabel: string
  onBack: () => void
  /** Disables the back affordance (e.g. while a save is in flight). */
  disabled?: boolean
}

/**
 * Mirrors the plugins detail breadcrumb so every second-level page shares one back
 * affordance instead of a top-right Back button.
 */
export function SettingsBreadcrumb({ parentLabel, currentLabel, onBack, disabled = false }: SettingsBreadcrumbProps): JSX.Element {
  const t = useT()
  return (
    <div style={catalogStyles.breadcrumb}>
      <Button
        type="button"
        size="sm"
        variant="ghost"
        onClick={onBack}
        disabled={disabled}
        aria-label={t('settings.breadcrumb.backTo', { label: parentLabel })}
        style={catalogStyles.catalogBreadcrumbButton}
      >
        {parentLabel}
      </Button>
      <BreadcrumbSeparator />
      <span style={catalogStyles.breadcrumbCurrent}>{currentLabel}</span>
    </div>
  )
}
