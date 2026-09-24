export const COMPUTER_API_METHODS = [
  'list_apps',
  'list_windows',
  'get_window',
  'launch_app',
  'get_window_state',
  'click',
  'type_text',
  'press_key',
  'scroll',
  'set_value',
  'drag',
  'activate_window'
] as const

export type ComputerApiMethod = typeof COMPUTER_API_METHODS[number]

export function isComputerApiMethod(value: string): value is ComputerApiMethod {
  return (COMPUTER_API_METHODS as readonly string[]).includes(value)
}
