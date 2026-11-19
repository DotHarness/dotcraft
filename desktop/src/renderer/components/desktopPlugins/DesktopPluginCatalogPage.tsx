import type { CatalogPageEditableText, CatalogPageProps } from '@dotcraft/plugin'
import type { CSSProperties, JSX } from 'react'

import {
  CatalogBreadcrumb,
  CatalogFilterButton,
  CatalogTabs,
  CatalogScrollArea,
  CatalogSearchBox,
  CatalogTopBar,
  styles
} from '../catalog/CatalogSurface'
import { Input } from '../ui/Input'

/** Frameless while focused too: the caret and selection say the text is editable. */
function HeadingInput({ field, style }: { field: CatalogPageEditableText; style: CSSProperties }): JSX.Element {
  return (
    <Input
      bare
      value={field.value}
      placeholder={field.placeholder}
      aria-label={field.ariaLabel}
      invalid={field.invalid}
      readOnly={field.readOnly}
      onChange={(event) => field.onValueChange(event.target.value)}
      style={{ display: 'block', width: '100%', height: 'auto', padding: 0, border: 0, background: 'transparent', ...style }}
    />
  )
}

const editableTitle: CSSProperties = { fontSize: 'inherit', fontWeight: 'inherit', lineHeight: 'inherit', textAlign: 'center', color: 'var(--text-primary)' }
const editableDescription: CSSProperties = {
  ...styles.browseColumn,
  fontSize: '13px',
  lineHeight: '20px',
  textAlign: 'center',
  color: 'var(--text-secondary)'
}

export function DesktopPluginCatalogPage({ title, titleEditor, descriptionEditor, meta, tabs, breadcrumb, actions, search, filters, children }: CatalogPageProps): JSX.Element {
  return (
    <div style={styles.page}>
      <CatalogTopBar
        navigation={breadcrumb ? (
          <CatalogBreadcrumb
            parentLabel={breadcrumb.parentLabel}
            currentLabel={breadcrumb.currentLabel}
            onBack={breadcrumb.onBack}
            trail={breadcrumb.trail ? [...breadcrumb.trail] : undefined}
          />
        ) : tabs && (
          <CatalogTabs inTopBar value={tabs.value} items={[...tabs.items]} onChange={tabs.onValueChange} />
        )}
        actions={actions}
      />
      <header style={styles.browseHeader}>
        <div style={{ display: 'grid', gap: '8px', marginBottom: search || filters ? '24px' : 0 }}>
          <h1 style={{ ...styles.heroTitle, margin: 0 }}>{titleEditor ? <HeadingInput field={titleEditor} style={editableTitle} /> : title}</h1>
          {descriptionEditor && <HeadingInput field={descriptionEditor} style={editableDescription} />}
          {meta && <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'center', gap: '4px' }}>{meta}</div>}
        </div>
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
