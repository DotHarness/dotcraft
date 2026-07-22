import type { CSSProperties, JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { BreadcrumbSeparator, CatalogHoverButton, styles as catalogStyles } from '../catalog/CatalogSurface'

interface SettingsBreadcrumbProps {
  /** Label of the list page the back button returns to. */
  parentLabel: string
  /** Label of the current sub-page. */
  currentLabel: string
  onBack: () => void
  /** Disables the back affordance (e.g. while a save is in flight). */
  disabled?: boolean
}

/**
 * Top-left breadcrumb for settings sub-pages: `Parent › Current`, where the
 * parent segment is a hover-pill button that returns to the list. Mirrors the
 * plugins detail breadcrumb so settings second-level pages share one back
 * affordance instead of a top-right Back button.
 */
export function SettingsBreadcrumb({ parentLabel, currentLabel, onBack, disabled = false }: SettingsBreadcrumbProps): JSX.Element {
  const t = useT()
  return (
    <div style={catalogStyles.breadcrumb}>
      <CatalogHoverButton
        type="button"
        onClick={onBack}
        disabled={disabled}
        aria-label={t('settings.breadcrumb.backTo', { label: parentLabel })}
        baseStyle={disabled ? { ...backButtonStyle, cursor: 'default', opacity: 0.6 } : backButtonStyle}
      >
        {parentLabel}
      </CatalogHoverButton>
      <BreadcrumbSeparator />
      <span style={catalogStyles.breadcrumbCurrent}>{currentLabel}</span>
    </div>
  )
}

// Pad the back segment into a rounded hover pill, then pull it left by the same
// padding so the leading word still lines up with the page title at x=0.
const backButtonStyle: CSSProperties = {
  ...catalogStyles.breadcrumbButton,
  padding: '6px 10px',
  borderRadius: '8px',
  lineHeight: 1.2,
  marginLeft: '-10px'
}
