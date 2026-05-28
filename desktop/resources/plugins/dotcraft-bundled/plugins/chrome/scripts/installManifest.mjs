#!/usr/bin/env node
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const metadata = JSON.parse(fs.readFileSync(path.join(__dirname, 'extension-id.json'), 'utf8'));
const scriptPath = path.join(__dirname, 'native-host.mjs');
const nodePath = process.execPath;

export function wrapperPath() {
  if (process.env.DOTCRAFT_CHROME_NATIVE_HOST_PATH) return process.env.DOTCRAFT_CHROME_NATIVE_HOST_PATH;
  if (process.platform === 'win32') {
    return path.join(os.homedir(), 'AppData', 'Local', 'DotCraft', 'chrome-extension', 'dotcraft-chrome-host.cmd');
  }
  return path.join(os.homedir(), '.local', 'share', 'dotcraft', 'chrome-extension', 'dotcraft-chrome-host.sh');
}

export function windowsWrapperContent(runtimePath = nodePath, nativeHostPath = scriptPath) {
  return `@echo off\r\nset ELECTRON_RUN_AS_NODE=1\r\n"${runtimePath}" "${nativeHostPath}"\r\n`;
}

export function shellWrapperContent(runtimePath = nodePath, nativeHostPath = scriptPath) {
  return `#!/bin/sh\nELECTRON_RUN_AS_NODE=1 exec "${runtimePath}" "${nativeHostPath}"\n`;
}

export function writeWrapper(target) {
  fs.mkdirSync(path.dirname(target), { recursive: true });
  if (process.platform === 'win32') {
    fs.writeFileSync(target, windowsWrapperContent());
    return;
  }

  fs.writeFileSync(target, shellWrapperContent());
  fs.chmodSync(target, 0o755);
}

export function manifestPath() {
  if (process.env.DOTCRAFT_CHROME_NATIVE_HOST_MANIFEST_PATH) {
    return process.env.DOTCRAFT_CHROME_NATIVE_HOST_MANIFEST_PATH;
  }
  if (process.platform === 'win32') {
    return path.join(os.homedir(), 'AppData', 'Local', 'DotCraft', 'chrome-extension', `${metadata.extensionHostName}.json`);
  }
  if (process.platform === 'darwin') {
    return path.join(os.homedir(), 'Library', 'Application Support', 'Google', 'Chrome', 'NativeMessagingHosts', `${metadata.extensionHostName}.json`);
  }
  return path.join(os.homedir(), '.config', 'google-chrome', 'NativeMessagingHosts', `${metadata.extensionHostName}.json`);
}

export function buildManifest(hostPath) {
  return {
    name: metadata.extensionHostName,
    description: 'DotCraft Chrome native messaging host',
    type: 'stdio',
    path: hostPath,
    allowed_origins: [`chrome-extension://${metadata.extensionId}/`]
  };
}

export function installNativeHostManifest() {
  const hostPath = wrapperPath();
  writeWrapper(hostPath);
  const target = manifestPath();
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, `${JSON.stringify(buildManifest(hostPath), null, 2)}\n`);

  if (process.platform === 'win32') {
    execFileSync('reg', [
      'add',
      `HKCU\\Software\\Google\\Chrome\\NativeMessagingHosts\\${metadata.extensionHostName}`,
      '/ve',
      '/t',
      'REG_SZ',
      '/d',
      target,
      '/f'
    ], { stdio: 'ignore' });
  }

  return {
    ok: true,
    manifestPath: target,
    hostPath,
    exists: fs.existsSync(target),
    hostExists: fs.existsSync(hostPath),
    wrapperValid: true
  };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.stdout.write(JSON.stringify(installNativeHostManifest(), null, 2) + '\n');
}
