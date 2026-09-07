interface MascotHandoffRecord {
  top: number
  time: number
}

let record: MascotHandoffRecord | null = null

export function recordMascotHandoff(el: HTMLElement): void {
  record = { top: el.getBoundingClientRect().top, time: performance.now() }
}

export function consumeMascotHandoff(el: HTMLElement): number | null {
  if (!record) return null
  const { top, time } = record
  record = null
  if (performance.now() - time > 400) return null
  const dy = top - el.getBoundingClientRect().top
  return Math.abs(dy) < 8 ? null : dy
}
