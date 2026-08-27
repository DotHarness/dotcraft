import { Component, type ErrorInfo, type ReactNode } from 'react'

import { useT } from '../../contexts/LocaleContext'
import styles from './DesktopPluginContributions.module.css'

interface DesktopPluginContributionBoundaryProps {
  identity: string
  fallback?: ReactNode
  children: ReactNode
}

interface DesktopPluginContributionBoundaryState {
  failed: boolean
}

export class DesktopPluginContributionBoundary extends Component<
  DesktopPluginContributionBoundaryProps,
  DesktopPluginContributionBoundaryState
> {
  override state = { failed: false }

  static getDerivedStateFromError(): DesktopPluginContributionBoundaryState {
    return { failed: true }
  }

  override componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error(`Desktop Plugin contribution '${this.props.identity}' failed to render:`, error, info.componentStack)
  }

  override render(): ReactNode {
    if (!this.state.failed) return this.props.children
    return this.props.fallback === undefined ? <DesktopPluginContributionFailure /> : this.props.fallback
  }
}

function DesktopPluginContributionFailure(): JSX.Element {
  const t = useT()
  return (
    <div className={styles.contributionError} role="alert">
      {t('desktopPlugins.contributionFailed')}
    </div>
  )
}
