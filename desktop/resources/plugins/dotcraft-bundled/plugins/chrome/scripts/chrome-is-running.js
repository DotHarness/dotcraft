#!/usr/bin/env node
const { execFileSync } = require('child_process');

const asJson = process.argv.includes('--json');

function runningProcesses() {
  try {
    if (process.platform === 'win32') {
      const output = execFileSync('tasklist.exe', ['/FI', 'IMAGENAME eq chrome.exe', '/FO', 'CSV', '/NH'], {
        encoding: 'utf8',
        stdio: ['ignore', 'pipe', 'ignore']
      });
      return output
        .split(/\r?\n/)
        .map((line) => line.trim())
        .filter((line) => /^"chrome\.exe"/i.test(line));
    }

    const command = process.platform === 'darwin' ? 'pgrep' : 'pgrep';
    const names = process.platform === 'darwin' ? ['-x', 'Google Chrome'] : ['-f', 'chrome|chromium'];
    const output = execFileSync(command, names, {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'ignore']
    });
    return output.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  } catch {
    return [];
  }
}

const processes = runningProcesses();
const result = {
  ok: processes.length > 0,
  running: processes.length > 0,
  processCount: processes.length
};

if (asJson) {
  process.stdout.write(JSON.stringify(result, null, 2) + '\n');
} else if (result.running) {
  console.log(`Chrome is running (${result.processCount} process${result.processCount === 1 ? '' : 'es'} found).`);
} else {
  console.log('Chrome does not appear to be running.');
}

process.exit(result.ok ? 0 : 1);
