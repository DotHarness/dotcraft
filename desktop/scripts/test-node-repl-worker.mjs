import { execFile } from 'node:child_process'
import { mkdtemp, writeFile, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { resolve, join } from 'node:path'
import { createRequire } from 'node:module'
import { promisify } from 'node:util'
import { createPackage } from '@electron/asar'

const require = createRequire(import.meta.url)
const directory = await mkdtemp(join(tmpdir(), 'dotcraft-repl-package-'))
try {
  const archive = join(directory, 'app.asar')
  await createPackage(resolve('out/main'), archive)
  const harness = join(directory, 'test.cjs')
  await writeFile(harness, `
    const { app, utilityProcess } = require('electron');
    const assert = require('node:assert/strict');
    const { once } = require('node:events');
    app.whenReady().then(async () => {
      const worker = utilityProcess.fork(${JSON.stringify(join(archive, 'nodeReplWorker.js'))}, [], { stdio: 'pipe' });
      worker.stdout.on('data', data => process.stdout.write(data));
      worker.stderr.on('data', data => process.stderr.write(data));
      const pending = new Map();
      let ready;
      const started = new Promise(resolve => { ready = resolve; });
      const images = [];
      let loopStarted;
      const looping = new Promise(resolve => { loopStarted = resolve; });
      worker.on('message', message => {
        if (message.type === 'ready') ready();
        if (message.type === 'output' && message.text === 'looping') loopStarted();
        if (message.type === 'result') {
          pending.get(message.context.evaluationId)?.(message.result);
          pending.delete(message.context.evaluationId);
        }
        if (message.type === 'host') {
          if (message.method === 'emitImage') images.push(message.value);
          worker.postMessage({ type: 'hostResult', context: message.context, id: message.id, value: null });
        }
      });
      worker.on('exit', code => { if (pending.size) throw new Error('Unexpected worker exit: ' + code); });
      let sequence = 0;
      const evaluate = code => new Promise(resolve => {
        const evaluationId = String(++sequence);
        pending.set(evaluationId, resolve);
        worker.postMessage({ type: 'evaluate', code, context: {
          threadId: 'packaged-test', evaluationId, generation: 'test', workspacePath: ${JSON.stringify(directory)},
          dotcraft: {}, browserSession: {}
        } });
      });
      await started;
      assert.equal((await evaluate('const value = await Promise.resolve(41); value')).resultText, '41');
      assert.equal((await evaluate('value + 1')).resultText, '42');
      assert.equal((await evaluate('const value = 2; value')).resultText, '2');
      assert.match((await evaluate('const duplicate = 1; const duplicate = 2')).error, /already been declared/);
      assert.equal((await evaluate("(await import('node:path')).basename('/one/two')")).resultText, 'two');
      assert.equal((await evaluate('await nodeRepl.emitImage(new Uint8Array([1, 2, 3])); nodeRepl.write(value)')).error, undefined);
      assert.deepEqual(Array.from(images[0]), [1, 2, 3]);
      void evaluate('nodeRepl.write("looping"); while (true) {}');
      await looping;
      pending.clear();
      const exited = once(worker, 'exit');
      worker.kill();
      await exited;
      console.log('Packaged Electron REPL: lexical state, await, import, errors, image bridge and synchronous-loop termination passed.');
      app.exit(0);
    }).catch(error => { console.error(error); app.exit(1); });
  `)
  const env = { ...process.env }
  delete env.ELECTRON_RUN_AS_NODE
  const result = await promisify(execFile)(require('electron'), [harness], {
    env, windowsHide: true, timeout: 15000
  })
  process.stdout.write(result.stdout)
  process.stderr.write(result.stderr)
} finally {
  await rm(directory, { recursive: true, force: true })
}
