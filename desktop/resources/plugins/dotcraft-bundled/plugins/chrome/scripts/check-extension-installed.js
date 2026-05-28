#!/usr/bin/env node
const fs = require('fs');
const path = require('path');
const os = require('os');

const asJson = process.argv.includes('--json');
const metadata = JSON.parse(fs.readFileSync(path.join(__dirname, 'extension-id.json'), 'utf8'));

function chromeUserDataDir() {
  if (process.env.DOTCRAFT_CHROME_USER_DATA_DIR) return process.env.DOTCRAFT_CHROME_USER_DATA_DIR;
  if (process.platform === 'win32') {
    const local = process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local');
    return path.join(local, 'Google', 'Chrome', 'User Data');
  }
  if (process.platform === 'darwin') {
    return path.join(os.homedir(), 'Library', 'Application Support', 'Google', 'Chrome');
  }
  return path.join(os.homedir(), '.config', 'google-chrome');
}

function readJson(file) {
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch {
    return null;
  }
}

function isUsableProfile(root, profile) {
  return typeof profile === 'string' && profile.length > 0 && fs.existsSync(path.join(root, profile, 'Preferences'));
}

function profileSortKey(profile) {
  if (profile === 'Default') return 0;
  const match = /^Profile (\d+)$/.exec(profile);
  return match ? Number(match[1]) : -1;
}

function highestProfile(root, candidates) {
  return candidates
    .filter((profile) => isUsableProfile(root, profile))
    .sort((a, b) => profileSortKey(a) - profileSortKey(b))
    .at(-1) || null;
}

function chooseProfile(root) {
  if (process.env.DOTCRAFT_CHROME_PROFILE) return process.env.DOTCRAFT_CHROME_PROFILE;
  const localState = readJson(path.join(root, 'Local State'));
  const lastUsed = localState?.profile?.last_used;
  if (isUsableProfile(root, lastUsed)) return lastUsed;
  const active = localState?.profile?.last_active_profiles;
  if (Array.isArray(active)) {
    const selected = highestProfile(root, active);
    if (selected) return selected;
  }
  if (fs.existsSync(root)) {
    const discovered = fs.readdirSync(root, { withFileTypes: true })
      .filter((entry) => entry.isDirectory() && (entry.name === 'Default' || /^Profile \d+$/.test(entry.name)))
      .map((entry) => entry.name);
    const selected = highestProfile(root, discovered);
    if (selected) return selected;
  }
  return 'Default';
}

function extensionVersionDirs(profileDir, extensionId) {
  const extensionDir = path.join(profileDir, 'Extensions', extensionId);
  if (!fs.existsSync(extensionDir)) return [];
  return fs.readdirSync(extensionDir, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .sort();
}

function extensionSetting(profileDir, extensionId) {
  for (const file of ['Secure Preferences', 'Preferences']) {
    const preferences = readJson(path.join(profileDir, file));
    const setting = preferences?.extensions?.settings?.[extensionId];
    if (setting) return setting;
  }
  return null;
}

const root = chromeUserDataDir();
const profile = chooseProfile(root);
const profileDir = path.join(root, profile);
const versions = extensionVersionDirs(profileDir, metadata.extensionId);
const setting = extensionSetting(profileDir, metadata.extensionId);
const installed = versions.length > 0 || setting != null;
const enabled = installed && setting?.state !== 0;
const result = {
  ok: installed && enabled,
  installed,
  enabled,
  extensionId: metadata.extensionId,
  profile,
  profileDir,
  userDataDir: root,
  versions
};

if (asJson) {
  process.stdout.write(JSON.stringify(result, null, 2) + '\n');
} else if (result.ok) {
  console.log(`DotCraft Chrome extension is installed and enabled in ${profile}.`);
} else if (installed) {
  console.log(`DotCraft Chrome extension is installed but disabled in ${profile}.`);
} else {
  console.log(`DotCraft Chrome extension ${metadata.extensionId} was not found in ${profile}.`);
}

process.exit(result.ok ? 0 : installed ? 1 : 2);
