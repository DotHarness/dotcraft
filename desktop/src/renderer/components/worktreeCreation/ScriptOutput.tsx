import { useEffect, useRef, type JSX } from 'react'
import styles from './ScriptOutput.module.css'

export function ScriptOutput({ text, label }: { text: string; label?: string }): JSX.Element {
  const ref = useRef<HTMLPreElement>(null)
  useEffect(() => {
    const node = ref.current
    if (node) node.scrollTop = node.scrollHeight
  }, [text])
  return <pre ref={ref} className={styles.output} aria-label={label} tabIndex={0}>{text}</pre>
}
