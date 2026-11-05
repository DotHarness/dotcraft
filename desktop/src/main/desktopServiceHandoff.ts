const handlers = new Map<number, (url: string) => Promise<void>>()

export function setDesktopServiceHandoffHandler(senderId: number, handler: ((url: string) => Promise<void>) | null): void {
  if (handler) handlers.set(senderId, handler)
  else handlers.delete(senderId)
}

export async function openDesktopServiceHandoff(url: string, senderId: number): Promise<void> {
  const handler = handlers.get(senderId)
  if (!handler) throw new Error('Desktop service handoff is unavailable')
  await handler(url)
}
