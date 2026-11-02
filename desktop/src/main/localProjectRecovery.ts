interface LocalProjectRecoveryClient {
  on(event: 'ready' | 'reconnected', listener: () => void): unknown
}

export interface LocalProjectRecoveryContext {
  refreshProjects: () => Promise<boolean>
  refreshSecondaryConnections: () => Promise<void>
  isCurrent: () => boolean
}

export function bindLocalProjectRecovery(
  client: LocalProjectRecoveryClient,
  context: LocalProjectRecoveryContext
): void {
  const recover = (): void => { void recoverLocalProjects(context) }
  client.on('ready', recover)
  client.on('reconnected', recover)
}

async function recoverLocalProjects(context: LocalProjectRecoveryContext): Promise<void> {
  if (!context.isCurrent()) return
  try {
    if (!await context.refreshProjects() || !context.isCurrent()) return
    await context.refreshSecondaryConnections()
  } catch (error) {
    console.warn('[desktop] failed to recover local Hub projects', error)
  }
}
