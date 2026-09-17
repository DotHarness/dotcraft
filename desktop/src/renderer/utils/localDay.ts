/** `YYYY-MM-DD` of a date in the local frame, matching the server's local-day bucketing. */
export function localDayKey(date: Date): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

/** Minutes to add to UTC to obtain local time, i.e. -getTimezoneOffset(). */
export function localTzOffsetMinutes(): number {
  return -new Date().getTimezoneOffset()
}

export function addLocalDays(date: Date, days: number): Date {
  const next = new Date(date)
  next.setDate(next.getDate() + days)
  return next
}

/** Every local day key from `from` to `to` inclusive, ascending. */
export function localDayKeys(from: Date, to: Date): string[] {
  const keys: string[] = []
  for (let cursor = new Date(from); cursor <= to; cursor = addLocalDays(cursor, 1)) {
    keys.push(localDayKey(cursor))
  }
  return keys
}
