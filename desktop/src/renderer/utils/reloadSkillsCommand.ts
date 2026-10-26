export async function executeReloadSkills(threadId: string): Promise<void> {
  const result = await window.api.appServer.sendRequest('command/execute', {
    threadId,
    command: '/reload-skills',
    arguments: []
  }) as { handled?: boolean }
  if (result.handled === false) {
    throw new Error('The server did not reload skills.')
  }
}
