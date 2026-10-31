import type { ReactNode } from 'react'
import type { SpreadsheetSheetSummary } from '../../../../../shared/viewer/spreadsheet'
import { useT } from '../../../../contexts/LocaleContext'

export function FormulaBar({ address, value }: { address: string; value: string }): JSX.Element {
  const t = useT()
  return (
    <div className="dc-sheet__formula" role="group" aria-label={t('viewer.sheet.formulaBar')}>
      <span className="dc-sheet__address" aria-label={t('viewer.sheet.address')}>{address}</span>
      <span className="dc-sheet__value" title={value || undefined}>{value}</span>
    </div>
  )
}

export function SheetTabs({
  sheets,
  active,
  onSelect,
  trailing
}: {
  sheets: SpreadsheetSheetSummary[]
  active: number
  onSelect: (index: number) => void
  trailing: ReactNode
}): JSX.Element {
  const t = useT()
  return (
    <div className="dc-sheet__footer">
      {sheets.length > 1 ? (
        <div className="dc-sheet__tabs" role="tablist" aria-label={t('viewer.sheet.sheets')}>
          {sheets.map((sheet, index) => (
            <button
              key={`${index}:${sheet.name}`}
              type="button"
              role="tab"
              aria-selected={index === active}
              className="dc-sheet__tab"
              onClick={() => onSelect(index)}
            >
              {sheet.tabColor && <span className="dc-sheet__tab-color" style={{ background: sheet.tabColor }} aria-hidden />}
              {sheet.name}
            </button>
          ))}
        </div>
      ) : (
        <span className="dc-sheet__tabs" />
      )}
      {trailing}
    </div>
  )
}
