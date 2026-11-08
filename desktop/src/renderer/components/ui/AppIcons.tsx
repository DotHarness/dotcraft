import type { CSSProperties, JSX } from 'react'
import {
  Bot,
  ChevronDown,
  CircleCheck,
  Code2,
  Clock,
  ExternalLink,
  Folder,
  Monitor,
  Puzzle,
  RotateCw,
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

export function GitHubIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="currentColor" aria-hidden="true" focusable="false">
      <path d="M12 .5C5.65.5.5 5.65.5 12c0 5.08 3.29 9.39 7.86 10.91.58.1.79-.25.79-.56v-2c-3.2.7-3.87-1.54-3.87-1.54-.52-1.33-1.28-1.68-1.28-1.68-1.05-.72.08-.7.08-.7 1.16.08 1.77 1.19 1.77 1.19 1.03 1.77 2.7 1.26 3.36.96.1-.75.4-1.26.73-1.55-2.55-.29-5.24-1.28-5.24-5.69 0-1.26.45-2.29 1.18-3.1-.12-.29-.51-1.46.11-3.04 0 0 .97-.31 3.18 1.18a11.04 11.04 0 0 1 5.78 0c2.2-1.49 3.17-1.18 3.17-1.18.63 1.58.23 2.75.11 3.04.74.81 1.18 1.84 1.18 3.1 0 4.42-2.69 5.39-5.26 5.68.41.36.78 1.06.78 2.14v3.17c0 .31.21.67.8.56C20.22 21.39 23.5 17.08 23.5 12 23.5 5.65 18.35.5 12 .5Z" />
    </svg>
  )
}

export function ExtensionsIcon({ size = 16 }: { size?: number }): JSX.Element {
  return <Puzzle size={size} strokeWidth={1.8} aria-hidden="true" />
}

export function WrenchIcon({ size = 16 }: { size?: number }): JSX.Element {
  return <Wrench size={size} strokeWidth={1.8} aria-hidden="true" />
}

export function CommitIcon({ size = 16 }: { size?: number }): JSX.Element {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" aria-hidden="true">
      <circle cx="10" cy="10" r="4" />
      <path d="M2 10h4m8 0h4" />
    </svg>
  )
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
