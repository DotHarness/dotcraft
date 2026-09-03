import type { CSSProperties, JSX } from 'react'
import {
  Bot,
  ChevronDown,
  CircleCheck,
  Code2,
  Clock,
  ExternalLink,
  Folder,
  GitCommitHorizontal,
  Monitor,
  Puzzle,
  RotateCw,
  Settings,
  Sparkle,
  SquareTerminal,
  Wrench
} from 'lucide-react'

export function FolderIcon({ size = 16 }: { size?: number }): JSX.Element {
  return <Folder size={size} strokeWidth={1.8} aria-hidden="true" />
}

export function RefreshIcon({ size = 16, style }: { size?: number; style?: CSSProperties }): JSX.Element {
  return <RotateCw size={size} strokeWidth={1.8} style={style} aria-hidden="true" />
}

export function CheckCircleIcon({ size = 16 }: { size?: number }): JSX.Element {
  return <CircleCheck size={size} strokeWidth={1.8} aria-hidden="true" />
}

export function OpenInBrowserIcon({ size = 16 }: { size?: number }): JSX.Element {
  return <ExternalLink size={size} strokeWidth={1.8} aria-hidden="true" />
}

export function ExtensionsIcon({ size = 16 }: { size?: number }): JSX.Element {
  return <Puzzle size={size} strokeWidth={1.8} aria-hidden="true" />
}

export function WrenchIcon({ size = 16 }: { size?: number }): JSX.Element {
  return <Wrench size={size} strokeWidth={1.8} aria-hidden="true" />
}

export function CommitIcon({ size = 16 }: { size?: number }): JSX.Element {
  return <GitCommitHorizontal size={size} strokeWidth={1.8} aria-hidden="true" />
}

export function EditorGenericIcon({ size = 16 }: { size?: number }): JSX.Element {
  return <Code2 size={size} strokeWidth={1.8} aria-hidden="true" />
}

export function ChevronDownIcon({ size = 16 }: { size?: number }): JSX.Element {
  return <ChevronDown size={size} strokeWidth={1.8} aria-hidden="true" />
}

export function SparkIcon({
  size = 16,
  style,
  strokeWidth = 1.8
}: {
  size?: number
  style?: CSSProperties
  strokeWidth?: number
}): JSX.Element {
  return <Sparkle size={size} strokeWidth={strokeWidth} style={style} aria-hidden="true" />
}

export function AutomationIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <circle cx="6" cy="5" r="2" />
      <circle cx="6" cy="19" r="2" />
      <circle cx="17" cy="5" r="2" />
      <path d="M6 7v10" />
      <path d="M6 12C6 8 8 5 12 5h3" />
    </svg>
  )
}

export function DesktopIcon({ size = 16 }: { size?: number }): JSX.Element {
  return <Monitor size={size} strokeWidth={1.8} aria-hidden="true" />
}

export function BotIcon({ size = 16 }: { size?: number }): JSX.Element {
  return <Bot size={size} strokeWidth={1.8} aria-hidden="true" />
}

export function IDEIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <rect
        x="3"
        y="4.5"
        width="18"
        height="15"
        rx="2.4"
        stroke="currentColor"
        strokeWidth="1.7"
      />
      <path d="M3 8.5h18" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
      <circle cx="5.8" cy="6.5" r=".8" fill="currentColor" />
      <circle cx="8.4" cy="6.5" r=".8" fill="currentColor" />
      <path
        d="m9.8 11.8-2 2 2 2"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="m14.2 11.8 2 2-2 2"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="m12.8 10.4-1.6 6.6"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
      />
    </svg>
  )
}

export function TerminalIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <rect x="3.4" y="4.6" width="17.2" height="14.8" rx="2.5" />
      <path d="m8.1 9.65 3.1 2.85-3.1 2.85" />
      <path d="M13.4 15.35h3.25" />
    </svg>
  )
}

export function ExplorerIcon({ size = 16 }: { size?: number }): JSX.Element {
  return <Folder size={size} strokeWidth={1.8} aria-hidden="true" />
}

export function TerminalBashIcon({ size = 16 }: { size?: number }): JSX.Element {
  return <SquareTerminal size={size} strokeWidth={1.8} aria-hidden="true" />
}

export function ClockIcon({ size = 16 }: { size?: number }): JSX.Element {
  return <Clock size={size} strokeWidth={1.8} aria-hidden="true" />
}

export function SettingsIcon({ size = 16 }: { size?: number }): JSX.Element {
  return <Settings size={size} strokeWidth={1.8} aria-hidden="true" />
}
