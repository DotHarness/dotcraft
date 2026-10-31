import { useEffect, useState } from 'react'
import type { ReadSpreadsheetResult } from '../../../../../shared/viewer/spreadsheet'

type SpreadsheetData =
  | { status: 'loading'; result: ReadSpreadsheetResult | null }
  | { status: 'ready'; result: ReadSpreadsheetResult }
  | { status: 'error'; result: null }

export function useSpreadsheetData(absolutePath: string, version: number, sheetIndex: number, attempt: number): SpreadsheetData {
  const [data, setData] = useState<SpreadsheetData>({ status: 'loading', result: null })
  useEffect(() => {
    let disposed = false
    setData((current) => ({ status: 'loading', result: current.result }))
    window.api.workspace.viewer
      .readSpreadsheet({ absolutePath, sheetIndex })
      .then((result) => {
        if (!disposed) setData({ status: 'ready', result })
      })
      .catch(() => {
        if (!disposed) setData({ status: 'error', result: null })
      })
    return () => {
      disposed = true
    }
  }, [absolutePath, version, sheetIndex, attempt])
  return data
}
