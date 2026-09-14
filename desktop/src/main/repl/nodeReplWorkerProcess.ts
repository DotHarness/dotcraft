import { startReplWorker } from './nodeReplWorker'
const port = process.parentPort
if (!port) throw new Error('Node REPL worker parent missing.')
startReplWorker({ send: message => port.postMessage(message), listen: handler => port.on('message', event => handler(event.data)) })
