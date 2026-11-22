export { Avatar, AppearanceAvatar, sizeTier, type AvatarProps, type AppearanceAvatarProps, type SizeTier } from './Avatar.js'
export { DecorationSwatch, SlotDecoration, HeadDecoration, HeadBackDecoration } from './Decorations.js'
export {
  AVATAR_RIG_GEOMETRY,
  Silhouette,
  Plane,
  Detail,
  Glow,
  useClipId,
  FaceplateSurface,
  FaceplateLayer,
} from './DecorationShapes.js'
export { materialBounds } from './RigMaterial.js'
export {
  defineAvatarPack,
  createAvatarCatalog,
  defaultAvatarCatalog,
  AvatarCatalogProvider,
  useAvatarCatalog,
  AvatarOutfitProvider,
  type AvatarOutfitWear,
  type AvatarCatalog,
  type AvatarCatalogOptions,
  type AvatarComponentPack,
  type AvatarComponentItem,
  type AvatarHeadComponentItem,
  type AvatarFaceAccessoryComponentItem,
  type AvatarFaceplateComponentItem,
  type AvatarHandComponentItem,
  type AvatarBackComponentItem,
  type AvatarSkinOverlayComponentItem,
  type AvatarSkinPaintComponentItem,
} from './pack.js'
export { useAvatarGaze, type AvatarGazeOptions } from './useAvatarGaze.js'

export { ComposerMascot } from './composer/ComposerMascot.js'
export type { ComposerMascotProps, ComposerMascotContext, MascotExpression, MascotLight } from './composer/types.js'
export { ComposerMascotShadow } from './composer/ComposerMascotShadow.js'
export { useComposerAvatarBehavior } from './composer/useComposerAvatarBehavior.js'
export { useMascotActiveIdle, type MascotActiveIdleAttributes, type MascotActiveIdleHandle, type MascotActiveIdleOptions } from './composer/useMascotActiveIdle.js'
export { MascotIdleStage } from './composer/MascotIdleStage.js'
export { MASCOT_SLEEP_AFTER_MS } from './composer/constants.js'
export type { MascotActiveIdle, MascotActiveIdleMotion, MascotActiveIdlePhase, MascotActiveIdlePlan, MascotIdleDirection } from './composer/constants.js'
