export function toAbsoluteWorkspacePath(workspacePath: string, filePath: string): string {
  if (filePath.startsWith('/') || /^[A-Za-z]:[\\/]/.test(filePath)) return filePath
  const ws = workspacePath.replace(/\\/g, '/').replace(/\/$/, '')
  const rel = filePath.replace(/\\/g, '/')
  return `${ws}/${rel}`.replace(/\/+/g, '/')
}

export function toWorkspaceRelativePath(workspacePath: string, filePath: string): string {
  const file = filePath.replace(/\\/g, '/')
  const workspace = workspacePath.replace(/\\/g, '/').replace(/\/+$/, '')
  if (!workspace || !(file.startsWith('/') || /^[A-Za-z]:\//.test(file))) return file
  const prefix = `${workspace}/`
  const inside = /^[A-Za-z]:\//.test(workspace)
    ? file.toLowerCase().startsWith(prefix.toLowerCase())
    : file.startsWith(prefix)
  return inside ? file.slice(prefix.length) : file
}
