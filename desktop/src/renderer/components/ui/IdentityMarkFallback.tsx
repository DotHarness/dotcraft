import styles from './IdentityMarkFallback.module.css'

export type IdentityMarkFallbackKind = 'skill' | 'plugin' | 'channel'

/** Shared artwork for an item that ships no icon of its own. */
export function IdentityMarkFallback({ kind }: { kind: IdentityMarkFallbackKind }): JSX.Element {
  return (
    <svg className={styles.art} viewBox="0 0 24 24" aria-hidden>
      {kind === 'skill' ? (
        <>
          <polygon className={styles.lit} points="12,2 21,7.2 12,12.4 3,7.2" />
          <polygon className={styles.mid} points="3,7.2 12,12.4 12,22 3,16.8" />
          <polygon className={styles.shaded} points="21,7.2 12,12.4 12,22 21,16.8" />
        </>
      ) : kind === 'plugin' ? (
        <>
          <path
            className={styles.lit}
            d="M8 3h2.6v4.4h2.8V3H16v4.4h1.6a2 2 0 0 1 2 2V13a6 6 0 0 1-6 6h-3a6 6 0 0 1-6-6V9.4a2 2 0 0 1 2-2H8V3Z"
          />
          <rect className={styles.lit} x="10.8" y="17" width="2.4" height="4.8" rx="1.2" />
        </>
      ) : (
        <>
          <rect className={styles.lit} x="3" y="3.5" width="18" height="13.5" rx="3.5" />
          <polygon className={styles.lit} points="7,14 7,21.2 12.6,16.2" />
        </>
      )}
    </svg>
  )
}
