import { expect, it } from 'vitest'
import { formatSpendAmount } from '../utils/providerBalance'

it('formats API-key spending in the reported currency, defaulting to USD', () => {
  expect(formatSpendAmount(1.5, 'CNY', 'en-US')).toContain('CN¥')
  expect(formatSpendAmount(1.5, undefined, 'en-US')).toBe('$1.50')
  expect(formatSpendAmount(0.000125, 'USD', 'en-US')).toBe('$0.000125')
})
