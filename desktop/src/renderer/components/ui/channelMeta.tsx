import type { CSSProperties, JSX, ReactNode } from 'react'
import qqIcon from '../../assets/channels/qq.svg'
import wecomIcon from '../../assets/channels/wecom.svg'
import weixinIcon from '../../assets/channels/weixin.svg'
import telegramIcon from '../../assets/channels/telegram.svg'
import feishuIcon from '../../assets/channels/feishu.svg'
import { AutomationIcon, ClockIcon, DesktopIcon, HeartbeatIcon, IDEIcon, SparkIcon, TerminalIcon } from './AppIcons'

export type ChannelVisualKind = 'brand' | 'system'
type ChannelIconRenderer = (size: number) => ReactNode

export interface ChannelVisualMeta {
  id: string
  label: string
  tooltip: string
  kind: ChannelVisualKind
  renderIcon?: ChannelIconRenderer
  iconSrc?: string
  iconScale?: number
  needsBackdrop?: boolean
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
  src: string,
  iconScale = 1,
  needsBackdrop = false
): ChannelVisualMeta {
  return { id, label, tooltip, kind: 'brand', iconSrc: src, iconScale, needsBackdrop }
}

const CHANNEL_META = new Map<string, ChannelVisualMeta>([
  ['qq', createBrandMeta('qq', 'QQ', 'QQ', qqIcon, 1.12, true)],
  ['wecom', createBrandMeta('wecom', 'WeCom', 'WeCom', wecomIcon, 1.12, true)],
  ['weixin', createBrandMeta('weixin', 'WeChat', 'WeChat', weixinIcon)],
  ['wechat', createBrandMeta('weixin', 'WeChat', 'WeChat', weixinIcon)],
  ['telegram', createBrandMeta('telegram', 'Telegram', 'Telegram', telegramIcon)],
  ['feishu', createBrandMeta('feishu', 'Feishu', 'Feishu', feishuIcon)],
  ['acp', createSystemMeta('acp', 'ACP', 'ACP', (size) => <IDEIcon size={size} />)],
  ['cli', createSystemMeta('cli', 'CLI', 'CLI', (size) => <TerminalIcon size={size} />)],
  ['automations', createSystemMeta('automations', 'Automations', 'Automations', (size) => <AutomationIcon size={size} />)],
  ['cron', createSystemMeta('cron', 'Cron', 'Cron', (size) => <ClockIcon size={size} />)],
  ['heartbeat', createSystemMeta('heartbeat', 'Heartbeat', 'Heartbeat', (size) => <HeartbeatIcon size={size} />)],
  ['dotcraft', createSystemMeta('dotcraft', 'DotCraft', 'DotCraft', (size) => <SparkIcon size={size} />)],
  ['dotcraft-desktop', createSystemMeta('dotcraft-desktop', 'Desktop', 'Desktop', (size) => <DesktopIcon size={size} />)]
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
  active = false,
  muted = false,
  size = 30,
  framed = true
}: {
  channelName: string
  tooltip?: string
  active?: boolean
  muted?: boolean
  size?: number
  framed?: boolean
}): JSX.Element {
  const meta = getChannelVisualMeta(channelName, tooltip)
  const brandMaskSize = meta.needsBackdrop
    ? framed
      ? Math.max(18, size - 8)
      : Math.max(18, size - 2)
    : size
  const brandIconSize = Math.min(
    brandMaskSize,
    Math.round((brandMaskSize - (meta.needsBackdrop ? 4 : 0)) * (meta.iconScale ?? 1))
  )
  const systemIconSize = Math.max(16, size - (framed ? 10 : 6))
  return (
    <span
      title={tooltip ?? meta.tooltip}
      aria-label={tooltip ?? meta.tooltip}
      style={{
        width: `${size}px`,
        height: `${size}px`,
        borderRadius: '10px',
        border: framed
          ? active
            ? '1px solid color-mix(in srgb, var(--accent) 55%, transparent)'
            : '1px solid var(--border-default)'
          : 'none',
        background: framed
          ? active
            ? 'color-mix(in srgb, var(--accent) 12%, var(--bg-secondary))'
            : meta.kind === 'brand'
              ? 'color-mix(in srgb, var(--bg-secondary) 84%, white 16%)'
              : 'var(--bg-secondary)'
          : 'transparent',
        color: muted ? 'var(--text-dimmed)' : active ? 'var(--accent)' : 'var(--text-secondary)',
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        boxSizing: 'border-box',
        flexShrink: 0
      }}
    >
      {meta.kind === 'brand' && meta.iconSrc ? (
        meta.needsBackdrop ? (
          <span
            style={{
              width: `${brandMaskSize}px`,
              height: `${brandMaskSize}px`,
              borderRadius: `${Math.max(7, Math.round(brandMaskSize * 0.34))}px`,
              background: active
                ? 'color-mix(in srgb, white 88%, var(--accent) 12%)'
                : 'color-mix(in srgb, white 78%, var(--bg-tertiary) 22%)',
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'center',
              overflow: 'hidden',
              flexShrink: 0
            }}
          >
            <img
              src={meta.iconSrc}
              alt=""
              aria-hidden="true"
              style={{
                ...IMG_ICON_STYLE,
                width: `${brandIconSize}px`,
                height: `${brandIconSize}px`
              }}
            />
          </span>
        ) : (
          <img
            src={meta.iconSrc}
            alt=""
            aria-hidden="true"
            style={{
              ...IMG_ICON_STYLE,
              width: `${brandIconSize}px`,
              height: `${brandIconSize}px`
            }}
          />
        )
      ) : (
        meta.renderIcon?.(systemIconSize)
      )}
    </span>
  )
}
