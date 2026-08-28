import { fireEvent, screen, waitFor, within } from '@testing-library/react'

/**
 * `Select` renders a button plus a portalled listbox rather than a `<select>`, so
 * `fireEvent.change` on the trigger does nothing. Options carry `data-value`, letting
 * callers name the stored value rather than the localized label.
 */
export async function chooseSelectValue(name: string | RegExp, value: string): Promise<void> {
  await chooseValueIn(await screen.findByRole('combobox', { name }), value)
}

/** Same, for a trigger the caller already has a handle on. */
export async function chooseValueIn(trigger: HTMLElement, value: string): Promise<void> {
  fireEvent.click(trigger)

  // The menu is portalled, so it is reached through the trigger's own aria-controls
  // rather than by role lookup — several selects can be on screen at once.
  let listbox: HTMLElement | null = null
  await waitFor(() => {
    const id = trigger.getAttribute('aria-controls')
    listbox = id ? document.getElementById(id) : null
    if (!listbox) throw new Error('select menu did not open')
  })

  const options = within(listbox!).getAllByRole('option')
  const option = options.find((entry) => entry.getAttribute('data-value') === value)
  if (!option) {
    const available = options.map((entry) => entry.getAttribute('data-value')).join(', ')
    throw new Error(`No option with value "${value}". Available: ${available}`)
  }
  fireEvent.click(option)
}
