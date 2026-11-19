import type { SummaryRowProps, SummarySectionProps } from '@dotcraft/plugin'
import type { JSX } from 'react'

import { Row, Section } from '../conversation/summary/SummaryRows'

export function DesktopPluginSummarySection({ title, actions, children }: SummarySectionProps): JSX.Element {
  return <Section title={title} actions={actions}>{children}</Section>
}

export function DesktopPluginSummaryRow({ leading, label, meta, title, onClick }: SummaryRowProps): JSX.Element {
  return <Row leading={leading} label={label} meta={meta} title={title} onClick={onClick} />
}
