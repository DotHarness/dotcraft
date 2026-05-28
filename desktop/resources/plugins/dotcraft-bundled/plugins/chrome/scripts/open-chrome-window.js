#!/usr/bin/env node
const { spawn, execFileSync } = require('child_process');
const path = require('path');
const fs = require('fs');
const os = require('os');

const asJson = process.argv.includes('--json');
const dryRun = process.argv.includes('--dry-run') || process.argv.includes('--check');
const urlArg = process.argv.find((arg) => /^https?:\/\//i.test(arg) || /^chrome:\/\/extensions(?:\/|\?|$)/i.test(arg) || arg === 'about:blank');
const targetUrl = urlArg || 'about:blank';

function exists(file) {
  try {
    return fs.existsSync(file);
  } catch {
    return false;
  }
}

function commandPath(command) {
  try {
    const tool = process.platform === 'win32' ? 'where.exe' : 'which';
    const output = execFileSync(tool, [command], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
    return output.split(/\r?\n/).map((line) => line.trim()).find(Boolean) || null;
  } catch {
    return null;
  }
}

function readJson(file) {
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch {
    return null;
  }
}

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

function registryValue(keyPath, valueName) {
  if (process.platform !== 'win32') return null;
  try {
    const args = ['query', keyPath];
    if (valueName == null) args.push('/ve');
    else args.push('/v', valueName);
    const output = execFileSync('reg.exe', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
    const label = valueName == null ? '(Default)' : valueName;
    for (const line of output.split(/\r?\n/)) {
      const match = line.match(/^\s*(.*?)\s+REG_\w+\s+(.+?)\s*$/);
      if (match && match[1] === label) return match[2].replace(/^"(.*)"$/, '$1');
    }
  } catch {
    return null;
  }
  return null;
}

function chromePath() {
  if (process.platform === 'win32') {
    const roots = [
      registryValue('HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\chrome.exe', null),
      registryValue('HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\chrome.exe', null),
      registryValue('HKLM\\Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\App Paths\\chrome.exe', null),
      process.env.ProgramFiles ? path.join(process.env.ProgramFiles, 'Google', 'Chrome', 'Application', 'chrome.exe') : null,
      process.env['ProgramFiles(x86)'] ? path.join(process.env['ProgramFiles(x86)'], 'Google', 'Chrome', 'Application', 'chrome.exe') : null,
      process.env.LOCALAPPDATA ? path.join(process.env.LOCALAPPDATA, 'Google', 'Chrome', 'Application', 'chrome.exe') : null,
      commandPath('chrome.exe')
    ].filter(Boolean);
    return roots.find(exists) || null;
  }
  if (process.platform === 'darwin') {
    const appPath = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
    return exists(appPath) ? appPath : null;
  }
  return commandPath('google-chrome') || commandPath('google-chrome-stable') || commandPath('chromium') || commandPath('chromium-browser');
}

const executablePath = chromePath();
const userDataDir = chromeUserDataDir();
const profile = chooseProfile(userDataDir);
const launchArgs = [`--profile-directory=${profile}`, targetUrl];
const result = { ok: Boolean(executablePath), executablePath, url: targetUrl, profile, userDataDir, launchArgs, opened: false };

if (result.ok && !dryRun) {
  const child = spawn(executablePath, launchArgs, { detached: true, stdio: 'ignore' });
  child.unref();
  result.opened = true;
}

if (asJson) {
  process.stdout.write(JSON.stringify(result, null, 2) + '\n');
} else if (!result.ok) {
  console.log('Google Chrome was not found in standard install locations.');
} else if (dryRun) {
  console.log(`Google Chrome is available at ${executablePath}.`);
} else {
  console.log(`Opened ${targetUrl} in Google Chrome.`);
}

process.exit(result.ok ? 0 : 1);
