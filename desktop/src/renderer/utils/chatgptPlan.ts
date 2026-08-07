/**
 * Translates a raw ChatGPT subscription plan tier (as returned by
 * auth/openai/status / auth/openai/usage) into a localized label for the UI.
 * Shared by ChatGptUsageBadge (composer footer) and the provider card in
 * Settings → Model providers.
 */
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
