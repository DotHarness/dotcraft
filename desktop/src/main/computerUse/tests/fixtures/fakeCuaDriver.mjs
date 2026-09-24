import { createInterface } from 'node:readline'

const version = process.env.FAKE_DRIVER_VERSION ?? '0.28.2'
const reply = (id, result) => process.stdout.write(`${JSON.stringify({ jsonrpc: '2.0', id, result })}\n`)

createInterface({ input: process.stdin }).on('line', (line) => {
  const message = JSON.parse(line)
  if (message.method === 'initialize') {
    reply(message.id, { protocolVersion: '2025-06-18', serverInfo: { name: 'cua-driver', version } })
    return
  }
  const tool = message.params?.name
  if (tool === 'hang') return
  if (tool === 'crash') {
    process.stderr.write('fatal: simulated crash\n')
    process.exit(3)
  }
  reply(message.id, {
    content: [{ type: 'text', text: `called ${tool}` }],
    structuredContent: { arguments: message.params?.arguments ?? {} }
  })
})
