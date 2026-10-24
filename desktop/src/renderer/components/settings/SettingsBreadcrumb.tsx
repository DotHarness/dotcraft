import { Fragment, type JSX } from 'react'
import { ChevronLeft } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { BreadcrumbSeparator, styles as catalogStyles, type CatalogBreadcrumbSegment } from '../catalog/CatalogSurface'
import { Button } from '../ui/Button'

interface SettingsBreadcrumbProps {
  parentLabel: string
  currentLabel: string
  onBack: () => void
  trail?: CatalogBreadcrumbSegment[]
  returnsToCaller?: boolean
  disabled?: boolean
}

export function SettingsBreadcrumb({
  parentLabel,
  currentLabel,
  onBack,
  trail = [],
  returnsToCaller = false,
  disabled = false
}: SettingsBreadcrumbProps): JSX.Element {
  const t = useT()
  if (returnsToCaller) {
    return (
      <div style={catalogStyles.breadcrumb}>
        <Button
          type="button"
          size="sm"
          variant="ghost"
          onClick={onBack}
          disabled={disabled}
          iconLeft={<ChevronLeft size={14} aria-hidden />}
          style={catalogStyles.catalogBreadcrumbButton}
        >
          {t('settings.breadcrumb.back')}
        </Button>
        <BreadcrumbSeparator />
        <span style={catalogStyles.breadcrumbCurrent}>{currentLabel}</span>
      </div>
    )
  }
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
      {trail.map((segment) => (
        <Fragment key={segment.label}>
          <BreadcrumbSeparator />
          <Button
            type="button"
            size="sm"
            variant="ghost"
            onClick={segment.onClick}
            disabled={disabled}
            style={catalogStyles.catalogBreadcrumbButton}
          >
            {segment.label}
          </Button>
        </Fragment>
      ))}
      <BreadcrumbSeparator />
      <span style={catalogStyles.breadcrumbCurrent}>{currentLabel}</span>
    </div>
  )
}
