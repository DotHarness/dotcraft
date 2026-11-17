import { spawn } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const RELAY_PROTOCOL_VERSION = 4;
const PIPE_PREFIX = 'dotcraft-chrome-';
const DOTCRAFT_CHROME_SETTINGS_URL = 'dotcraft://settings/computer-control/chrome';

export function encodeFrame(message) {
  const body = Buffer.from(JSON.stringify(message), 'utf8');
  const header = Buffer.alloc(4);
  header.writeUInt32LE(body.length, 0);
  return Buffer.concat([header, body]);
}

export class FrameDecoder {
  constructor() {
    this.buffer = Buffer.alloc(0);
  }

  push(chunk) {
    this.buffer = Buffer.concat([this.buffer, Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk)]);
    const frames = [];
    while (this.buffer.length >= 4) {
      const length = this.buffer.readUInt32LE(0);
      if (this.buffer.length < length + 4) break;
      const body = this.buffer.subarray(4, 4 + length);
      this.buffer = this.buffer.subarray(4 + length);
      frames.push(JSON.parse(body.toString('utf8')));
    }
    return frames;
  }
}

function nonce() {
  return Math.random().toString(36).slice(2);
}

export function nativePipePath(pid = process.pid, random = nonce()) {
  if (process.platform === 'win32') {
    return `\\\\.\\pipe\\${PIPE_PREFIX}${pid}-${random}`;
  }
  return path.join(os.tmpdir(), `${PIPE_PREFIX}${pid}-${random}.sock`);
}

function errorMessage(error) {
  return error instanceof Error ? error.message : String(error);
}

function openDotCraftChromeSettings() {
  const [command, args] = process.platform === 'win32'
    ? ['cmd.exe', ['/c', 'start', '', DOTCRAFT_CHROME_SETTINGS_URL]]
    : process.platform === 'darwin'
      ? ['open', [DOTCRAFT_CHROME_SETTINGS_URL]]
      : ['xdg-open', [DOTCRAFT_CHROME_SETTINGS_URL]];
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { detached: true, stdio: 'ignore', windowsHide: true });
    child.once('error', reject);
    child.once('spawn', () => {
      child.unref();
      resolve();
    });
  });
}

export function createNativeRelay({ sendNative, openSettings = openDotCraftChromeSettings }) {
  const clients = new Set();
  const pending = new Map();
  let nextHostId = 1;

  function sendClient(client, message) {
    if (!client.socket.destroyed) client.socket.write(encodeFrame(message));
  }

  function forward(client, message) {
    const hostId = nextHostId++;
    pending.set(hostId, { client, id: message.id });
    sendNative({ type: 'relay-request', id: hostId, method: message.method, params: message.params ?? {} });
  }

  function attachClient(socket) {
    const client = { socket, decoder: new FrameDecoder() };
    clients.add(client);
    socket.on('data', (chunk) => {
      try {
        for (const message of client.decoder.push(chunk)) forward(client, message);
      } catch {
        socket.destroy();
      }
    });
    const detach = () => {
      if (!clients.delete(client)) return;
      for (const [hostId, entry] of pending) {
        if (entry.client === client) pending.delete(hostId);
      }
      if (clients.size === 0) sendNative({ type: 'clients-released' });
    };
    socket.on('close', detach);
    socket.on('error', detach);
    return client;
  }

  function handleExtensionMessage(message) {
    if (message.type === 'relay-response') {
      const entry = pending.get(message.id);
      if (!entry) return;
      pending.delete(message.id);
      sendClient(entry.client, message.error
        ? { id: entry.id, error: message.error }
        : { id: entry.id, result: message.result });
      return;
    }
    if (message.type === 'relay-event') {
      for (const client of clients) sendClient(client, { event: message.event, params: message.params ?? {} });
      return;
    }
    if (message.type === 'dotcraft-open-settings') {
      openSettings()
        .then(() => sendNative({ type: 'dotcraft-settings-opened', ok: true }))
        .catch((error) => sendNative({ type: 'dotcraft-host-error', error: errorMessage(error) }));
    }
  }

  function extensionEnded() {
    for (const [hostId, entry] of pending) {
      pending.delete(hostId);
      sendClient(entry.client, { id: entry.id, error: { message: 'BridgeDisconnected: the DotCraft Chrome extension disconnected.' } });
    }
  }

  return { attachClient, handleExtensionMessage, extensionEnded };
}

export function createRelayServer(relay, pipePath) {
  const removeSocketFile = () => {
    if (process.platform !== 'win32') fs.rmSync(pipePath, { force: true });
  };
  removeSocketFile();
  const server = net.createServer((socket) => relay.attachClient(socket));
  server.on('close', removeSocketFile);
  return server;
}

export function startNativeHost() {
  const sendNative = (message) => process.stdout.write(encodeFrame(message));
  const relay = createNativeRelay({ sendNative });
  const pipePath = nativePipePath();
  const server = createRelayServer(relay, pipePath);
  server.listen(pipePath, () => sendNative({ type: 'dotcraft-host-ready', protocolVersion: RELAY_PROTOCOL_VERSION }));
  server.on('error', (error) => sendNative({ type: 'dotcraft-host-error', error: errorMessage(error) }));

  const decoder = new FrameDecoder();
  process.stdin.on('data', (chunk) => {
    for (const message of decoder.push(chunk)) relay.handleExtensionMessage(message);
  });
  process.stdin.on('end', () => {
    relay.extensionEnded();
    server.close();
    setImmediate(() => process.exit(0));
  });
  process.stdin.resume();
  return server;
}

if (import.meta.url === pathToFileURL(process.argv[1] || '').href) {
  startNativeHost();
}
