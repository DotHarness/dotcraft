import type { CatalogPageProps } from '@dotcraft/plugin'
import type { JSX } from 'react'

import {
  CatalogFilterButton,
  CatalogScrollArea,
  CatalogSearchBox,
  CatalogTopBar,
  styles
} from '../catalog/CatalogSurface'

export function DesktopPluginCatalogPage({ title, actions, search, filters, children }: CatalogPageProps): JSX.Element {
  return (
    <div style={styles.page}>
      <CatalogTopBar actions={actions} />
      <header style={styles.browseHeader}>
        <h1 style={styles.heroTitle}>{title}</h1>
        {(search || filters) && (
          <div style={styles.searchRow}>
            {search && (
              <CatalogSearchBox value={search.value} placeholder={search.placeholder} onChange={search.onValueChange} />
            )}
            {filters && (
              <CatalogFilterButton
                ariaLabel={filters.ariaLabel}
                groups={filters.groups.map((group) => ({
                  label: group.label,
                  icon: group.icon,
                  value: group.value,
                  options: [...group.options],
                  onChange: group.onValueChange,
                  isActive: group.options.length > 0 && group.value !== group.options[0].value
                }))}
              />
            )}
          </div>
        )}
      </header>
      <CatalogScrollArea>
        <div style={styles.browseColumn}>{children}</div>
      </CatalogScrollArea>
    </div>
  )
}
