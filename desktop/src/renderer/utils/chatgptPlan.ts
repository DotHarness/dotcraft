/** Input is the raw tier from `auth/openai/status` or `auth/openai/usage`. */
export function formatPlanLabel(
  plan: string | null | undefined,
  t: (key: string) => string
): string {
  const raw = plan?.trim().toLowerCase()
  switch (raw) {
    case 'free':
      return t('composer.chatgptBadge.plan.free')
    case 'plus':
      return t('composer.chatgptBadge.plan.plus')
    case 'pro':
      return t('composer.chatgptBadge.plan.pro')
    case 'business':
      return t('composer.chatgptBadge.plan.business')
    case 'enterprise':
      return t('composer.chatgptBadge.plan.enterprise')
    case 'edu':
    case 'education':
      return t('composer.chatgptBadge.plan.edu')
    default:
      return t('composer.chatgptBadge.plan.unknown')
  }
}
