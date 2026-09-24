import { execFile } from 'node:child_process'
import { basename } from 'node:path'

export interface ResolvedWindowApp {
  hwnd: number
  /** Process that owns the top-level window; the driver addresses windows through it. */
  windowPid: number
  id: string
  displayName: string
  exePath?: string
}

export type ResolveWindowApps = (hwnds: number[]) => Promise<ResolvedWindowApp[]>

const WINDOW_IDENTITY_SOURCE = String.raw`
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class DcWindowIdentity {
  delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder buf, int max);
  [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder buf, ref uint size);
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetApplicationUserModelId(IntPtr h, ref uint len, StringBuilder buf);
  const uint QueryLimited = 0x1000;

  public static string[] Resolve(long value) {
    var hwnd = new IntPtr(value);
    uint windowPid;
    GetWindowThreadProcessId(hwnd, out windowPid);
    if (windowPid == 0) return null;
    uint pid = windowPid;
    var exe = ImagePath(pid);
    if (exe != null && exe.EndsWith("\\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)) {
      uint core = 0;
      EnumChildWindows(hwnd, (h, l) => {
        var cls = new StringBuilder(256);
        GetClassName(h, cls, cls.Capacity);
        if (cls.ToString() != "Windows.UI.Core.CoreWindow") return true;
        GetWindowThreadProcessId(h, out core);
        return false;
      }, IntPtr.Zero);
      if (core == 0) return new[] { windowPid.ToString(), "", "" };
      pid = core;
      exe = ImagePath(pid);
    }
    return new[] { windowPid.ToString(), exe ?? "", Aumid(pid) ?? "" };
  }

  static string ImagePath(uint pid) {
    var h = OpenProcess(QueryLimited, false, pid);
    if (h == IntPtr.Zero) return null;
    try {
      var buf = new StringBuilder(1024);
      uint size = (uint)buf.Capacity;
      return QueryFullProcessImageName(h, 0, buf, ref size) ? buf.ToString() : null;
    } finally { CloseHandle(h); }
  }

  static string Aumid(uint pid) {
    var h = OpenProcess(QueryLimited, false, pid);
    if (h == IntPtr.Zero) return null;
    try {
      uint len = 512;
      var buf = new StringBuilder((int)len);
      return GetApplicationUserModelId(h, ref len, buf) == 0 ? buf.ToString() : null;
    } finally { CloseHandle(h); }
  }
}
`

function buildWindowIdentityScript(hwnds: number[]): string {
  const list = hwnds.map((hwnd) => String(Math.trunc(hwnd))).join(',')
  return `$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -TypeDefinition @'
${WINDOW_IDENTITY_SOURCE}
'@
$startApps = $null
$rows = foreach ($h in @(${list})) {
  $r = [DcWindowIdentity]::Resolve([long]$h)
  if ($null -eq $r) { continue }
  $name = $null
  if ($r[2]) {
    if ($null -eq $startApps) { $startApps = @(Get-StartApps) }
    $name = ($startApps | Where-Object { $_.AppID -eq $r[2] } | Select-Object -First 1).Name
  } elseif ($r[1]) {
    try {
      $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($r[1])
      $name = if ($info.FileDescription -and $info.FileDescription -ne 'Electron') { $info.FileDescription } else { $info.ProductName }
    } catch {}
  }
  [pscustomobject]@{ hwnd = [long]$h; windowPid = [int]$r[0]; exePath = $r[1]; aumid = $r[2]; displayName = $name }
}
ConvertTo-Json -InputObject @($rows) -Compress`
}

interface IdentityRow {
  hwnd?: unknown
  windowPid?: unknown
  exePath?: unknown
  aumid?: unknown
  displayName?: unknown
}

function toResolvedWindowApp(row: IdentityRow): ResolvedWindowApp | null {
  const hwnd = typeof row.hwnd === 'number' ? row.hwnd : Number.NaN
  const windowPid = typeof row.windowPid === 'number' ? row.windowPid : Number.NaN
  const exePath = typeof row.exePath === 'string' && row.exePath ? row.exePath : undefined
  const aumid = typeof row.aumid === 'string' && row.aumid ? row.aumid : undefined
  if (!Number.isFinite(hwnd) || !Number.isFinite(windowPid)) return null
  const id = aumid ?? exePath
  if (!id) return null
  const named = typeof row.displayName === 'string' ? row.displayName.trim() : ''
  const displayName = named || (exePath ? basename(exePath).replace(/\.exe$/i, '') : id)
  return { hwnd, windowPid, id, displayName, exePath }
}

export function resolveWindowAppsWithPowerShell(hwnds: number[]): Promise<ResolvedWindowApp[]> {
  if (hwnds.length === 0) return Promise.resolve([])
  const encoded = Buffer.from(buildWindowIdentityScript(hwnds), 'utf16le').toString('base64')
  return new Promise((resolve, reject) => {
    execFile(
      'powershell.exe',
      ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', encoded],
      { windowsHide: true, timeout: 20_000, maxBuffer: 4 * 1024 * 1024 },
      (error, stdout) => {
        if (error) {
          reject(new Error(`app_unidentified: ${error.message}`))
          return
        }
        try {
          const rows = JSON.parse(stdout.trim() || '[]') as IdentityRow[]
          resolve(rows.map(toResolvedWindowApp).filter((row): row is ResolvedWindowApp => row !== null))
        } catch (parseError) {
          reject(new Error(`app_unidentified: ${parseError instanceof Error ? parseError.message : String(parseError)}`))
        }
      })
  })
}
