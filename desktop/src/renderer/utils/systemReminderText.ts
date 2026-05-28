const SYSTEM_REMINDER_OPEN = '<system-reminder>'
const SYSTEM_REMINDER_CLOSE = '</system-reminder>'

export function stripSystemReminderBlocks(input: string): string {
  let text = input
  while (true) {
    const open = text.indexOf(SYSTEM_REMINDER_OPEN)
    if (open < 0) return text.trimEnd()

    const close = text.indexOf(
      SYSTEM_REMINDER_CLOSE,
      open + SYSTEM_REMINDER_OPEN.length
    )
    if (close < 0) {
      return text.slice(0, open).trimEnd()
    }

    text = (
      text.slice(0, open) +
      text.slice(close + SYSTEM_REMINDER_CLOSE.length)
    ).trimEnd()
  }
}
