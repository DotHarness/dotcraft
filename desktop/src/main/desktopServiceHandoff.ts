let handler: ((url: string) => Promise<void>) | null = null

export function setDesktopServiceHandoffHandler(next: ((url: string) => Promise<void>) | null): void {
  handler = next
}

export async function openDesktopServiceHandoff(url: string): Promise<void> {
  if (!handler) throw new Error('Desktop service handoff is unavailable')
  await handler(url)
}
