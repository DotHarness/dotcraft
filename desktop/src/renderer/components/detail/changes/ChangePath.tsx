import styles from './ChangePath.module.css'

export function ChangePath({ path }: { path: string }): JSX.Element {
  const split = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\')) + 1
  return (
    <span className={styles.path}>
      {split > 0 && <span className={styles.directory}>{path.slice(0, split)}</span>}
      <span className={styles.name}>{path.slice(split)}</span>
    </span>
  )
}
