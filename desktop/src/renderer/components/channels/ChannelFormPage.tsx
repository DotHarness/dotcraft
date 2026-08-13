import type { ReactNode } from 'react'
import { useT } from '../../contexts/LocaleContext'
import {
  CatalogBreadcrumb,
  CatalogTopBar,
  type CatalogBreadcrumbSegment
} from '../catalog/CatalogSurface'
import styles from './ChannelFormPage.module.css'

interface ChannelFormPageProps {
  /** Ordered outermost first. */
  trail?: CatalogBreadcrumbSegment[]
  breadcrumbLabel: string
  title: string
  description: string
  onBack: () => void
  children?: ReactNode
}

/**
 * Shared by module management and external channel creation and management, so
 * the three keep one intro rhythm and one breadcrumb instead of drifting apart.
 */
export function ChannelFormPage({
  trail,
  breadcrumbLabel,
  title,
  description,
  onBack,
  children
}: ChannelFormPageProps): JSX.Element {
  const t = useT()
  return (
    <div className={styles.page}>
      <CatalogTopBar
        navigation={(
          <CatalogBreadcrumb
            parentLabel={t('channels.title')}
            currentLabel={breadcrumbLabel}
            onBack={onBack}
            trail={trail}
          />
        )}
      />
      <main className={styles.scroll}>
        <div className={styles.content}>
          <header className={styles.intro}>
            <h1>{title}</h1>
            <p>{description}</p>
          </header>
          {children}
        </div>
      </main>
    </div>
  )
}
