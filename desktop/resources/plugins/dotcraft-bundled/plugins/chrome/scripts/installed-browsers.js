#!/usr/bin/env node
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const asJson = process.argv.includes('--json');

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

function candidates() {
  if (process.platform === 'win32') {
    const registryCandidates = [
      registryValue('HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\chrome.exe', null),
      registryValue('HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\chrome.exe', null),
      registryValue('HKLM\\Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\App Paths\\chrome.exe', null)
    ].filter(Boolean);
    const programFiles = [
      process.env.ProgramFiles,
      process.env['ProgramFiles(x86)'],
      process.env.LOCALAPPDATA ? path.join(process.env.LOCALAPPDATA, 'Google', 'Chrome', 'Application') : null
    ].filter(Boolean);
    return [
      ...registryCandidates,
      ...programFiles.map((root) => path.join(root, 'Google', 'Chrome', 'Application', 'chrome.exe')),
      ...programFiles.map((root) => path.join(root, 'chrome.exe')),
      commandPath('chrome.exe')
    ].filter(Boolean);
  }

  if (process.platform === 'darwin') {
    return [
      '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
      path.join(process.env.HOME || '', 'Applications', 'Google Chrome.app', 'Contents', 'MacOS', 'Google Chrome')
    ];
  }

  return [
    commandPath('google-chrome'),
    commandPath('google-chrome-stable'),
    commandPath('chromium'),
    commandPath('chromium-browser')
  ].filter(Boolean);
}

const browsers = [...new Set(candidates())]
  .filter(exists)
  .map((executablePath) => ({
    name: 'Google Chrome',
    executablePath
  }));

const result = {
  ok: browsers.length > 0,
  browsers
};

if (asJson) {
  process.stdout.write(JSON.stringify(result, null, 2) + '\n');
} else if (result.ok) {
  for (const browser of browsers) console.log(`${browser.name}: ${browser.executablePath}`);
} else {
  console.log('Google Chrome was not found in standard install locations.');
}

process.exit(result.ok ? 0 : 1);
