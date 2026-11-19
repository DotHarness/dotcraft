const workspaceRoutePattern = /^\/dashboard\/workspaces\/([a-f0-9]{32})(?:\/|$)/;

export function currentWorkspaceId() {
  return workspaceRoutePattern.exec(location.pathname)?.[1] ?? '';
}

export function dashboardPath(workspaceId = currentWorkspaceId()) {
  return workspaceId ? `/dashboard/workspaces/${workspaceId}` : '/dashboard';
}
