export interface ContextBreakdownCategory {
  readonly id: string
  readonly tokens: number
  readonly onDemand: boolean
  readonly items?: readonly { readonly name: string; readonly tokens: number }[] | null
}

export interface ContextRow {
  readonly id: string
  readonly tokens: number
  readonly items: readonly { readonly name: string; readonly tokens: number }[]
  readonly series: number | null
}

export interface ContextBreakdownView {
  readonly rows: readonly ContextRow[]
  readonly foldedTokens: number
  readonly sinceLastUpdate: number
  readonly onDemand: readonly ContextRow[]
}

const CONTEXT_SERIES_LIMIT = 4

export function buildContextBreakdownView(
  categories: readonly ContextBreakdownCategory[],
  liveTokens: number
): ContextBreakdownView {
  const loaded = categories
    .filter((category) => !category.onDemand && category.tokens > 0)
    .sort((a, b) => b.tokens - a.tokens)
  const measured = loaded.reduce((sum, category) => sum + category.tokens, 0)
  const rows = loaded.map((category, index) => ({
    id: category.id,
    tokens: category.tokens,
    items: category.items ?? [],
    series: index < CONTEXT_SERIES_LIMIT ? index + 1 : null
  }))
  const onDemand = categories
    .filter((category) => category.onDemand && category.tokens > 0)
    .sort((a, b) => b.tokens - a.tokens)
    .map((category) => ({ id: category.id, tokens: category.tokens, items: category.items ?? [], series: null }))

  return {
    rows,
    foldedTokens: rows.filter((row) => row.series === null).reduce((sum, row) => sum + row.tokens, 0),
    sinceLastUpdate: Math.max(0, liveTokens - measured),
    onDemand
  }
}
