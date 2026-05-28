#!/usr/bin/env node
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const DESKTOP_DEEP_LINK_PORT = Number.parseInt(process.env.DOTCRAFT_DESKTOP_DEEPLINK_PORT || '32178', 10);
const HOST = '127.0.0.1';
const DEFAULT_COMMAND_TIMEOUT_MS = 30000;
const MAX_COMMAND_TIMEOUT_MS = 120000;
const HOST_PROTOCOL_VERSION = 3;
const PIPE_PREFIX = 'dotcraft-chrome-';
const DOTCRAFT_CHROME_SETTINGS_URL = 'dotcraft://settings/computer-control/chrome';

let stdinBuffer = Buffer.alloc(0);
let nextExtensionRequestId = 1;
const extensionPending = new Map();
const extensionPendingByCommandId = new Map();
const pipeClients = new Set();

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

function sendNativeMessage(message) {
  process.stdout.write(encodeFrame(message));
}

function sendPipe(socket, message) {
  socket.write(encodeFrame(message));
}

export function commandTimeoutMs(envelopeOrParams) {
  const params = envelopeOrParams?.params ?? envelopeOrParams;
  const candidate = Number(envelopeOrParams?.timeoutMs ?? params?.timeoutMs ?? params?.options?.timeoutMs);
  if (!Number.isFinite(candidate) || candidate <= 0) return DEFAULT_COMMAND_TIMEOUT_MS;
  return Math.max(1, Math.min(Math.floor(candidate), MAX_COMMAND_TIMEOUT_MS));
}

function classifiedError(category, message) {
  return new Error(`${category}: ${message}`);
}

function buildExtensionParams(envelope) {
  return {
    ...(envelope.params ?? {}),
    browserSession: envelope.browserSession,
    commandId: envelope.commandId,
    timeoutMs: envelope.timeoutMs
  };
}

export function buildExtensionCommandMessage(envelope, extensionId) {
  return {
    type: 'dotcraft-request',
    id: extensionId,
    commandId: envelope.commandId,
    method: envelope.method,
    params: buildExtensionParams(envelope),
    timeoutMs: commandTimeoutMs(envelope)
  };
}

function forwardToExtension(envelope) {
  const extensionId = nextExtensionRequestId++;
  const timeoutMs = commandTimeoutMs(envelope);
  const message = buildExtensionCommandMessage(envelope, extensionId);
  sendNativeMessage(message);
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      extensionPending.delete(extensionId);
      if (envelope.commandId) extensionPendingByCommandId.delete(envelope.commandId);
      sendNativeMessage({
        type: 'dotcraft-cancel',
        commandId: envelope.commandId,
        browserSession: envelope.browserSession,
        reason: 'timeout'
      });
      reject(classifiedError('CommandTimeout', `Chrome extension command '${envelope.method}' timed out after ${timeoutMs}ms.`));
    }, timeoutMs);
    const pending = {
      extensionId,
      commandId: envelope.commandId,
      resolve,
      reject,
      timer,
      browserSession: envelope.browserSession
    };
    extensionPending.set(extensionId, pending);
    if (envelope.commandId) extensionPendingByCommandId.set(envelope.commandId, pending);
  });
}

function cancelExtensionCommand(envelope) {
  const pending = envelope.commandId ? extensionPendingByCommandId.get(envelope.commandId) : null;
  if (pending) {
    clearTimeout(pending.timer);
    extensionPending.delete(pending.extensionId);
    extensionPendingByCommandId.delete(envelope.commandId);
    pending.reject(classifiedError('CommandCancelled', `Chrome command ${envelope.commandId} was cancelled: ${envelope.reason || 'cancelled'}.`));
  }
  sendNativeMessage({
    type: 'dotcraft-cancel',
    commandId: envelope.commandId,
    browserSession: envelope.browserSession,
    reason: envelope.reason || 'cancelled'
  });
  return { ok: true, cancelled: Boolean(pending) };
}

function openDotCraftChromeSettingsViaProtocol() {
  const platform = process.platform;
  let command;
  let args;

  if (platform === 'win32') {
    command = 'cmd.exe';
    args = ['/c', 'start', '', DOTCRAFT_CHROME_SETTINGS_URL];
  } else if (platform === 'darwin') {
    command = 'open';
    args = [DOTCRAFT_CHROME_SETTINGS_URL];
  } else {
    command = 'xdg-open';
    args = [DOTCRAFT_CHROME_SETTINGS_URL];
  }

  const child = spawn(command, args, {
    detached: true,
    stdio: 'ignore',
    windowsHide: true
  });
  child.unref();
}

function requestDesktopChromeSettings() {
  if (!Number.isFinite(DESKTOP_DEEP_LINK_PORT) || DESKTOP_DEEP_LINK_PORT <= 0) {
    return Promise.reject(new Error('Invalid DotCraft deep link port.'));
  }

  return new Promise((resolve, reject) => {
    const socket = net.createConnection({ host: HOST, port: DESKTOP_DEEP_LINK_PORT });
    let buffer = '';
    let settled = false;
    let timer;
    const finish = (error) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      socket.destroy();
      if (error) reject(error);
      else resolve();
    };

    timer = setTimeout(() => {
      finish(new Error('DotCraft did not respond.'));
    }, 600);

    socket.setEncoding('utf8');
    socket.on('connect', () => {
      socket.write(JSON.stringify({ type: 'openChromeSettings' }) + '\n', 'utf8');
    });
    socket.on('data', (chunk) => {
      buffer += chunk;
      const newline = buffer.indexOf('\n');
      if (newline < 0) return;
      const line = buffer.slice(0, newline).trim();
      if (!line) return;
      try {
        const response = JSON.parse(line);
        finish(response?.ok === true ? null : new Error(response?.error || 'DotCraft rejected the request.'));
      } catch {
        finish(new Error('DotCraft returned invalid JSON.'));
      }
    });
    socket.on('error', (error) => finish(error));
    socket.on('close', () => finish(new Error('DotCraft closed the connection.')));
  });
}

async function openDotCraftChromeSettings() {
  try {
    await requestDesktopChromeSettings();
  } catch {
    openDotCraftChromeSettingsViaProtocol();
  }
}

function handleExtensionMessage(message) {
  if (message?.type === 'dotcraft-open-settings') {
    openDotCraftChromeSettings()
      .then(() => {
        sendNativeMessage({ type: 'dotcraft-settings-opened', ok: true });
      })
      .catch((error) => {
        sendNativeMessage({
          type: 'dotcraft-host-error',
          error: error instanceof Error ? error.message : String(error)
        });
      });
    return;
  }

  if (message?.type === 'dotcraft-response') {
    const pending = extensionPending.get(message.id);
    if (!pending) return;
    clearTimeout(pending.timer);
    extensionPending.delete(message.id);
    if (pending.commandId) extensionPendingByCommandId.delete(pending.commandId);
    if (message.ok === false) pending.reject(new Error(message.error || 'Chrome extension command failed.'));
    else pending.resolve(message.result);
    return;
  }

  for (const socket of pipeClients) {
    sendPipe(socket, { kind: 'event', event: message?.type || 'chrome.message', data: message });
  }
}

function handleNativeData(chunk) {
  stdinBuffer = Buffer.concat([stdinBuffer, chunk]);
  while (stdinBuffer.length >= 4) {
    const length = stdinBuffer.readUInt32LE(0);
    if (stdinBuffer.length < length + 4) return;
    const body = stdinBuffer.subarray(4, 4 + length);
    stdinBuffer = stdinBuffer.subarray(4 + length);
    try {
      handleExtensionMessage(JSON.parse(body.toString('utf8')));
    } catch (error) {
      sendNativeMessage({
        type: 'dotcraft-host-error',
        error: error instanceof Error ? error.message : String(error)
      });
    }
  }
}

function handlePipeMessage(socket, message) {
  if (message?.kind === 'command' && message.method === 'getInfo') {
    sendPipe(socket, {
      id: message.id,
      ok: true,
      result: {
        backendId: 'chrome-extension',
        protocolVersion: HOST_PROTOCOL_VERSION,
        supportsCommandCancel: true
      }
    });
    return;
  }

  if (message?.kind === 'command') {
    forwardToExtension(message)
      .then((result) => sendPipe(socket, { id: message.id, ok: true, result }))
      .catch((error) => sendPipe(socket, {
        id: message.id,
        ok: false,
        error: error instanceof Error ? error.message : String(error)
      }));
    return;
  }

  if (message?.kind === 'cancel') {
    const result = cancelExtensionCommand(message);
    sendPipe(socket, { id: message.id, ok: true, result });
    return;
  }

  sendPipe(socket, { id: message?.id, ok: false, error: 'UnsupportedApi: Unsupported Chrome host envelope.' });
}

function createPipeServer(pipePath) {
  if (process.platform !== 'win32') {
    try {
      fs.unlinkSync(pipePath);
    } catch {
      // The socket path may not exist.
    }
  }

  const server = net.createServer((socket) => {
    const decoder = new FrameDecoder();
    pipeClients.add(socket);
    socket.on('data', (chunk) => {
      try {
        for (const message of decoder.push(chunk)) {
          handlePipeMessage(socket, message);
        }
      } catch (error) {
        sendPipe(socket, {
          ok: false,
          error: error instanceof Error ? error.message : String(error)
        });
      }
    });
    socket.on('close', () => pipeClients.delete(socket));
    socket.on('error', () => pipeClients.delete(socket));
  });

  server.on('close', () => {
    if (process.platform !== 'win32') {
      try {
        fs.unlinkSync(pipePath);
      } catch {
        // Ignore cleanup failures.
      }
    }
  });
  return server;
}

export function startNativeHost() {
  const pipePath = nativePipePath();
  const server = createPipeServer(pipePath);
  server.listen(pipePath, () => {
    sendNativeMessage({
      type: 'dotcraft-host-ready',
      pipePath,
      protocolVersion: HOST_PROTOCOL_VERSION
    });
  });
  server.on('error', (error) => {
    sendNativeMessage({
      type: 'dotcraft-host-error',
      error: error instanceof Error ? error.message : String(error)
    });
  });

  process.stdin.on('data', handleNativeData);
  process.stdin.on('end', () => process.exit(0));
  process.stdin.resume();
  return server;
}

if (import.meta.url === pathToFileURL(process.argv[1] || '').href) {
  startNativeHost();
}
