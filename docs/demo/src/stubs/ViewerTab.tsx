/**
 * Build-time stub for the Desktop viewer tab subtree (Monaco editor, xterm
 * terminal, PDF/image viewers). The demo never opens viewer tabs, and stubbing
 * the lazy-loaded module keeps those heavy dependencies out of the build.
 */
export function ViewerTab(): JSX.Element | null {
  return null
}
