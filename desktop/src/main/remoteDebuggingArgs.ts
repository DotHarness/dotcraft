const REMOTE_DEBUGGING_PORT_ARG = '--remote-debugging-port'

export function stripRemoteDebuggingPortArgs(argv: readonly string[]): string[] {
  const result: string[] = []

  for (let index = 0; index < argv.length; index++) {
    const arg = argv[index]
    if (arg === REMOTE_DEBUGGING_PORT_ARG) {
      index++
      continue
    }
    if (arg.startsWith(`${REMOTE_DEBUGGING_PORT_ARG}=`)) {
      continue
    }
    result.push(arg)
  }

  return result
}
