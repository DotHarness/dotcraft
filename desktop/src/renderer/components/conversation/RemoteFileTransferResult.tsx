import { ArrowRight } from 'lucide-react'
import { translate, type AppLocale } from '../../../shared/locales'
import { formatTransferSize, type RemoteFileTransferDisplay } from '../../utils/remoteToolHostDisplay'
import { ActionTooltip } from '../ui/ActionTooltip'
import { CopyButton } from '../ui/CopyButton'
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
  return (
    <span className={styles.pathCell}>
      <ActionTooltip label={path} wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}>
        <code className={styles.path}>{path}</code>
      </ActionTooltip>
      <CopyButton
        size={22}
        iconSize={13}
        className={styles.copy}
        getText={() => path}
        label={translate(locale, 'toolCall.remoteToolHost.transfer.copyPath')}
        copiedLabel={translate(locale, 'common.copied')}
        tooltipWrapperStyle={{ flexShrink: 0 }}
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
