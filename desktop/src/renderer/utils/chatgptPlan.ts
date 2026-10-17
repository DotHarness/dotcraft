const PLAN_NAMES: Record<string, string> = {
  free: 'Free',
  go: 'Go',
  plus: 'Plus',
  prolite: 'Pro 100',
  pro: 'Pro 200',
  promax: 'Pro 500',
  team: 'Business',
  self_serve_business_usage_based: 'Business',
  self_serve_business_prolite: 'Business Premium',
  business: 'Enterprise',
  enterprise: 'Enterprise',
  enterprise_cbp_usage_based: 'Enterprise',
  enterprise_cbp_automation: 'Enterprise (Automation)',
  edu: 'Edu',
  education: 'Edu',
  edu_plus: 'Edu Plus',
  edu_pro: 'Edu Pro'
}

export function formatPlanName(plan: string | null | undefined): string | null {
  const raw = plan?.trim().toLowerCase()
  if (!raw || raw === 'unknown') return null
  return PLAN_NAMES[raw] ?? raw.charAt(0).toUpperCase() + raw.slice(1)
}
