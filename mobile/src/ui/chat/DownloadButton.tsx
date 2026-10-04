import { useEffect, useState } from 'react'
import { isUnsupportedSave, saveToDownloads, showToast } from '../../platform/downloads'
import { useI18n } from '../../i18n'
import { RoundIconButton } from '../parts'

export function DownloadButton({
  name,
  mimeType,
  data,
  tone,
}: {
  name: string
  mimeType: string
  data: string
  tone?: 'default' | 'camera'
}) {
  const { t } = useI18n()
  const [state, setState] = useState<'idle' | 'saving' | 'saved'>('idle')
  useEffect(() => {
    if (state !== 'saved') return
    const timer = setTimeout(() => setState('idle'), 2000)
    return () => clearTimeout(timer)
  }, [state])

  const save = async () => {
    setState('saving')
    try {
      const saved = await saveToDownloads(data, name, mimeType)
      setState('saved')
      showToast(t('file.saved', { file: saved }))
    } catch (error) {
      setState('idle')
      showToast(t(isUnsupportedSave(error) ? 'file.saveUnsupported' : 'file.saveFailed'))
    }
  }

  return (
    <RoundIconButton
      label={t('file.download')}
      icon={state === 'saved' ? 'check' : 'download'}
      tone={tone}
      onPress={() => {
        if (state === 'idle') void save()
      }}
    />
  )
}
