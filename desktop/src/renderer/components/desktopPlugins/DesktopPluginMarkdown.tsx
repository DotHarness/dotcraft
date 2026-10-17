import type { MarkdownProps } from '@dotcraft/plugin'

import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import styles from './DesktopPluginMarkdown.module.css'

export function DesktopPluginMarkdown({ content }: MarkdownProps): JSX.Element {
  return (
    <div className={styles.markdown}>
      <MarkdownRenderer content={content} linkMode="external" enableMermaid={false} containOverflow />
    </div>
  )
}
