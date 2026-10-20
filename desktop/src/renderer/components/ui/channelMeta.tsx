import type { CSSProperties, JSX, ReactNode } from 'react'
import { Download } from 'lucide-react'
import qqIcon from '../../assets/channels/qq.svg'
import wecomIcon from '../../assets/channels/wecom.svg'
import weixinIcon from '../../assets/channels/weixin.svg'
import telegramIcon from '../../assets/channels/telegram.svg'
import feishuIcon from '../../assets/channels/feishu.svg'
import { AutomationIcon, DesktopIcon, IDEIcon, SparkIcon, TerminalIcon } from './AppIcons'

export type ChannelVisualKind = 'brand' | 'system'
type ChannelIconRenderer = (size: number) => ReactNode

export interface ChannelVisualMeta {
  id: string
  label: string
  tooltip: string
  kind: ChannelVisualKind
  renderIcon?: ChannelIconRenderer
  iconSrc?: string
}

const IMG_ICON_STYLE: CSSProperties = {
  width: '100%',
  height: '100%',
  objectFit: 'contain',
  display: 'block'
}

function createSystemMeta(
  id: string,
  label: string,
  tooltip: string,
  renderIcon: ChannelIconRenderer
): ChannelVisualMeta {
  return { id, label, tooltip, kind: 'system', renderIcon }
}

function createBrandMeta(
  id: string,
  label: string,
  tooltip: string,
  src: string
): ChannelVisualMeta {
  return { id, label, tooltip, kind: 'brand', iconSrc: src }
}

const CHANNEL_META = new Map<string, ChannelVisualMeta>([
  ['qq', createBrandMeta('qq', 'QQ', 'QQ', qqIcon)],
  ['wecom', createBrandMeta('wecom', 'WeCom', 'WeCom', wecomIcon)],
  ['weixin', createBrandMeta('weixin', 'WeChat', 'WeChat', weixinIcon)],
  ['wechat', createBrandMeta('weixin', 'WeChat', 'WeChat', weixinIcon)],
  ['telegram', createBrandMeta('telegram', 'Telegram', 'Telegram', telegramIcon)],
  ['feishu', createBrandMeta('feishu', 'Feishu', 'Feishu', feishuIcon)],
  ['acp', createSystemMeta('acp', 'ACP', 'ACP', (size) => <IDEIcon size={size} />)],
  ['cli', createSystemMeta('cli', 'CLI', 'CLI', (size) => <TerminalIcon size={size} />)],
  ['automations', createSystemMeta('automations', 'Automations', 'Automations', (size) => <AutomationIcon size={size} />)],
  ['dotcraft', createSystemMeta('dotcraft', 'DotCraft', 'DotCraft', (size) => <SparkIcon size={size} />)],
  ['dotcraft-desktop', createSystemMeta('dotcraft-desktop', 'Desktop', 'Desktop', (size) => <DesktopIcon size={size} />)],
  ['session-import', createSystemMeta('session-import', 'Imported chat', 'Imported chat', (size) => (
    <Download size={size} strokeWidth={1.8} aria-hidden="true" />
  ))]
])

export function getChannelVisualMeta(channelName: string, fallbackTooltip?: string): ChannelVisualMeta {
  const key = channelName.trim().toLowerCase()
  const known = CHANNEL_META.get(key)
  if (known) return known

  const label = channelName.trim() || 'Channel'
  return {
    id: key || 'unknown',
    label,
    tooltip: fallbackTooltip ?? label,
    kind: 'system',
    renderIcon: (size) => <DesktopIcon size={size} />
  }
}

export function ChannelIconBadge({
  channelName,
  tooltip,
  muted = false,
  size = 30,
  iconSrc,
  label
}: {
  channelName: string
  tooltip?: string
  muted?: boolean
  size?: number
  /** Explicit branded icon (data URL / safe URL), e.g. an app-origin badge — bypasses the static channel map. */
  iconSrc?: string
  /** Display label/tooltip fallback when using an explicit iconSrc. */
  label?: string
}): JSX.Element {
  const meta: ChannelVisualMeta = iconSrc
    ? {
        id: channelName.trim().toLowerCase() || 'app',
        label: label ?? channelName,
        tooltip: tooltip ?? label ?? channelName,
        kind: 'brand',
        iconSrc
      }
    : getChannelVisualMeta(channelName, tooltip)
  return (
    <span
      title={tooltip ?? meta.tooltip}
      aria-label={tooltip ?? meta.tooltip}
      style={{
        width: `${size}px`,
        height: `${size}px`,
        borderRadius: `${Math.max(2, Math.round(size * 0.22))}px`,
        overflow: 'hidden',
        color: muted ? 'var(--text-dimmed)' : 'var(--text-secondary)',
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        flexShrink: 0
      }}
    >
      {meta.kind === 'brand' && meta.iconSrc ? (
        <img src={meta.iconSrc} alt="" aria-hidden="true" style={IMG_ICON_STYLE} />
      ) : (
        meta.renderIcon?.(size)
      )}
    </span>
  )
}
