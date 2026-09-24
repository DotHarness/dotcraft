import { win32 } from 'node:path'

const BLOCKED_EXECUTABLES = new Set([
  'cmd', 'powershell', 'powershell_ise', 'pwsh', 'conhost', 'openconsole', 'windowsterminal', 'wt',
  'wsl', 'wslhost', 'bash', 'git-bash', 'mintty', 'alacritty', 'wezterm', 'wezterm-gui', 'hyper', 'tabby',
  'kitty', 'putty', 'puttytel', 'cmder', 'conemu', 'conemu64', 'conemuc', 'mobaxterm', 'termius', 'warp',
  'terminus', 'fluentterminal', 'ttermpro',
  'lockapp', 'logonui', 'credentialuibroker', 'consent',
  '1password', 'bitwarden', 'keepass', 'keepassxc', 'lastpass', 'dashlane', 'enpass', 'nordpass', 'roboform',
  'keeper', 'keeperpasswordmanager', 'protonpass',
  'securityhealthsystray', 'securityhealthhost', 'msmpeng', 'mpcmdrun', 'avastui', 'avgui', 'mcuicnt', 'mcui32',
  'nortonui', 'ns', 'egui', 'bdagent', 'seccenter', 'mbam', 'mbamtray', 'avp', 'avpui', 'ksde', 'sophosui',
  'sophosuihost', 'wrsa', 'avguard', 'avira.systray', 'hitmanpro', 'zam', 'sentinelui',
  'dotcraft', 'cua-driver'
])

const BLOCKED_PACKAGE_FAMILIES = [
  'microsoft.windowsterminal_',
  'microsoft.windowsterminalpreview_',
  'microsoft.sechealthui_',
  'microsoftcorporationii.windowssubsystemforlinux_',
  'canonicalgrouplimited.',
  'microsoft.powershell_'
]

function isBlockedExecutable(path: string, ownExecutable: string): boolean {
  const lower = path.toLowerCase()
  return lower === ownExecutable.toLowerCase()
    || BLOCKED_EXECUTABLES.has(win32.basename(lower).replace(/\.exe$/, '').replace(/\s+/g, ''))
}

export function isBlockedApp(appId: string, exePath?: string, ownExecutable = process.execPath): boolean {
  if (!appId.includes('!')) return isBlockedExecutable(appId, ownExecutable)
  const aumid = appId.toLowerCase()
  return BLOCKED_PACKAGE_FAMILIES.some((family) => aumid.startsWith(family))
    || (exePath !== undefined && isBlockedExecutable(exePath, ownExecutable))
}
