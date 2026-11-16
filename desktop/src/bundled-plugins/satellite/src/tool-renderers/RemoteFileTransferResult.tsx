import { useState } from 'react'
import { ArrowRight, Copy, Check } from 'lucide-react'
import { translate } from '../i18n'
import type { AppLocale } from '../types'
import { formatTransferSize, type RemoteFileTransferDisplay } from './display'
import { ActionTooltip, IconButton } from '@dotcraft/plugin'
import styles from './RemoteFileTransferResult.module.css'

export function RemoteFileTransferResult({ display, locale }: {
  display: RemoteFileTransferDisplay
  locale: AppLocale
}): JSX.Element {
  const upload = display.direction === 'upload'
  const from = { machine: upload ? localMachine(locale) : display.host, path: upload ? display.localPath : display.remotePath }
  const to = { machine: upload ? display.host : localMachine(locale), path: upload ? display.remotePath : display.localPath }
  const hasProgress = display.totalBytes != null && display.transferredBytes != null
  const percent = hasProgress
    ? display.totalBytes === 0 ? 0 : Math.floor(display.transferredBytes! / display.totalBytes! * 100)
    : undefined

  return (
    <div className={styles.transfer}>
      <div className={styles.route}>
        <Endpoint {...from} locale={locale} />
        <ArrowRight size={13} strokeWidth={1.8} aria-hidden className={styles.arrow} />
        <Endpoint {...to} locale={locale} />
      </div>
      {display.stage !== 'completed' && (display.stage === 'preparing' || hasProgress) && (
        <div className={styles.meter}>
          <progress
            aria-label={translate(locale, 'toolCall.remoteToolHost.transfer.progress')}
            max={display.totalBytes || 1}
            value={hasProgress ? (display.totalBytes === 0 ? 0 : display.transferredBytes) : undefined}
          />
          {percent != null && <span className={styles.percent}>{percent}%</span>}
        </div>
      )}
      {(display.stage === 'transferring' || display.stage === 'failed') && (
        <div className={styles.stats}>
          {hasProgress && (
            <span>{formatTransferSize(display.transferredBytes!, locale)} / {formatTransferSize(display.totalBytes!, locale)}</span>
          )}
          <span>{filesDone(display.completedFiles, display.totalFiles, locale)}</span>
          {display.currentFile && <code className={styles.current}>{display.currentFile}</code>}
          {display.stage === 'failed' && (
            <span className={styles.retained}>
              {translate(locale, 'toolCall.remoteToolHost.transfer.retained', {
                size: formatTransferSize(display.completedBytes, locale)
              })}
            </span>
          )}
        </div>
      )}
    </div>
  )
}

function Endpoint({ machine, path, locale }: { machine: string; path: string; locale: AppLocale }): JSX.Element {
  return (
    <span className={styles.endpoint}>
      <span className={styles.machine}>{machine}</span>
      <PathCell path={path} locale={locale} />
    </span>
  )
}

function PathCell({ path, locale }: { path: string; locale: AppLocale }): JSX.Element {
  const [copied, setCopied] = useState(false)
  async function copy(): Promise<void> {
    if (!navigator.clipboard) return
    try { await navigator.clipboard.writeText(path); setCopied(true) } catch { }
  }
  return (
    <span className={styles.pathCell}>
      <ActionTooltip label={path}>
        <code className={styles.path}>{path}</code>
      </ActionTooltip>
      <IconButton
        size={22}
        className={styles.copy}
        data-copied={copied || undefined}
        icon={copied ? <Check size={13} /> : <Copy size={13} />}
        onClick={() => void copy()}
        label={translate(locale, copied ? 'common.copied' : 'toolCall.remoteToolHost.transfer.copyPath')}
      />
    </span>
  )
}

function filesDone(done: number, total: number | undefined, locale: AppLocale): string {
  return total == null
    ? translate(locale, 'toolCall.remoteToolHost.transfer.filesDone.known', { done })
    : translate(locale, 'toolCall.remoteToolHost.transfer.filesDone.total', { done, total })
}

function localMachine(locale: AppLocale): string {
  return translate(locale, 'toolCall.remoteToolHost.transfer.thisPc')
}
