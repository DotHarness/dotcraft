import { fork } from 'node:child_process'
import { mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { resolve, join } from 'node:path'
import { buildSync } from 'esbuild'
import type { ForkReplWorker, ReplProcess } from '../../repl/NodeReplWorkerClient'

export function createReplWorkerFixture(): { fork: ForkReplWorker; dispose(): void } {
  const directory = mkdtempSync(join(tmpdir(), 'dotcraft-repl-test-'))
  const entry = join(directory, 'worker.cjs')
  buildSync({
    stdin: { contents: `import { startReplWorker } from ${JSON.stringify(resolve('src/main/repl/nodeReplWorker.ts'))};
      startReplWorker({ send: message => process.send(message), listen: handler => process.on('message', handler) });`,
      resolveDir: process.cwd(), loader: 'ts' },
    bundle: true, platform: 'node', format: 'cjs', outfile: entry
  })
  return {
    fork: () => {
      const child = fork(entry, [], { stdio: ['ignore', 'ignore', 'pipe', 'ipc'], serialization: 'advanced' })
      child.stderr?.on('data', data => process.stderr.write(data))
      return { postMessage: message => { child.send(message) },
        on: (event, handler) => child.on(event, handler), kill: () => child.kill() } as ReplProcess
    },
    dispose: () => rmSync(directory, { recursive: true, force: true })
  }
}
