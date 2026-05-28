#!/usr/bin/env node
const fs = require('fs');
const path = require('path');
const os = require('os');
const { execFileSync } = require('child_process');

const asJson = process.argv.includes('--json');
const metadata = JSON.parse(fs.readFileSync(path.join(__dirname, 'extension-id.json'), 'utf8'));
const expectedOrigin = `chrome-extension://${metadata.extensionId}/`;

function defaultManifestPath() {
  if (process.env.DOTCRAFT_CHROME_NATIVE_HOST_MANIFEST_PATH) {
    return process.env.DOTCRAFT_CHROME_NATIVE_HOST_MANIFEST_PATH;
  }
  if (process.platform === 'win32') {
    try {
      const output = execFileSync('reg', [
        'query',
        `HKCU\\Software\\Google\\Chrome\\NativeMessagingHosts\\${metadata.extensionHostName}`,
        '/ve'
      ], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
      const match = output.match(/REG_SZ\s+(.+)$/m);
      if (match) return match[1].trim();
    } catch {
      return path.join(os.homedir(), 'AppData', 'Local', 'DotCraft', 'chrome-extension', `${metadata.extensionHostName}.json`);
    }
  }
  if (process.platform === 'darwin') {
    return path.join(os.homedir(), 'Library', 'Application Support', 'Google', 'Chrome', 'NativeMessagingHosts', `${metadata.extensionHostName}.json`);
  }
  return path.join(os.homedir(), '.config', 'google-chrome', 'NativeMessagingHosts', `${metadata.extensionHostName}.json`);
}

function readManifest(file) {
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch {
    return null;
  }
}

function readText(file) {
  try {
    return fs.readFileSync(file, 'utf8');
  } catch {
    return '';
  }
}

const manifestPath = defaultManifestPath();
const manifest = readManifest(manifestPath);
const origins = Array.isArray(manifest?.allowed_origins) ? manifest.allowed_origins : [];
const exists = fs.existsSync(manifestPath);
const nameMatches = manifest?.name === metadata.extensionHostName;
const allowedOriginMatches = origins.includes(expectedOrigin);
const hostPath = typeof manifest?.path === 'string' ? manifest.path : '';
const hostExists = hostPath ? fs.existsSync(hostPath) : false;
const wrapperText = hostExists ? readText(hostPath) : '';
const wrapperPointsToNativeHost = wrapperText.includes('native-host.mjs');
const wrapperHasElectronRunAsNode = wrapperText.includes('ELECTRON_RUN_AS_NODE=1');
const wrapperValid = hostExists && wrapperPointsToNativeHost && wrapperHasElectronRunAsNode;
const ok = exists && nameMatches && allowedOriginMatches && wrapperValid;
const result = {
  ok,
  code: ok ? 'nativeHostReady' : (exists ? 'nativeHostNeedsRepair' : 'nativeHostMissing'),
  message: ok ? 'Chrome Native Host is installed.' : 'Chrome Native Host needs to be installed or repaired.',
  exists,
  nameMatches,
  allowedOriginMatches,
  hostExists,
  wrapperValid,
  wrapperPointsToNativeHost,
  wrapperHasElectronRunAsNode
};

if (asJson) {
  process.stdout.write(JSON.stringify(result, null, 2) + '\n');
} else if (result.ok) {
  console.log('DotCraft Chrome Native Messaging host manifest is valid.');
} else {
  console.log('DotCraft Chrome Native Messaging host manifest is missing or invalid.');
}

process.exit(result.ok ? 0 : 1);
