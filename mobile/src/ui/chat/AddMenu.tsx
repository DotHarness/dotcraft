import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useI18n } from '../../i18n'
import { MenuRow, PopoverMenu } from '../Menu'

const ABOVE_COMPOSER_BAR = 58

export function AddMenu({
  visible,
  canAttachFiles,
  canPlan,
  planMode,
  onClose,
  onPhoto,
  onFile,
  onPlanMode,
}: {
  visible: boolean
  canAttachFiles: boolean
  canPlan: boolean
  planMode: boolean
  onClose: () => void
  onPhoto: () => void
  onFile: () => void
  onPlanMode: (on: boolean) => void
}) {
  const { t } = useI18n()
  const insets = useSafeAreaInsets()
  return (
    <PopoverMenu visible={visible} label={t('composer.add')} anchor={{ bottom: insets.bottom + ABOVE_COMPOSER_BAR }} onClose={onClose}>
      <MenuRow icon="image" label={t('composer.photo')} onPress={onPhoto} />
      {canAttachFiles ? <MenuRow icon="file" label={t('composer.file')} onPress={onFile} /> : null}
      {canPlan ? (
        <MenuRow icon="listChecks" label={t('composer.planMode')} checked={planMode} onPress={() => onPlanMode(!planMode)} />
      ) : null}
    </PopoverMenu>
  )
}
