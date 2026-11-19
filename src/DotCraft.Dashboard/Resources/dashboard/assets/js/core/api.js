import { dashboardPath } from './workspace-route.js';

export function apiUrl(path) {
  return `${dashboardPath()}/api${path}`;
}

export function redirectToLogin() {
  location.replace(`${dashboardPath()}/login${location.hash}`);
}

export async function fetchApi(path, options) {
  const res = await fetch(apiUrl(path), options);
  if (res.status === 401) {
    const error = await res.clone().json().catch(() => null);
    if (error?.error === 'unauthorized') redirectToLogin();
  }
  return res;
}

export async function fetchJson(url) {
  try {
    const res = await fetchApi(url);
    if (!res.ok) return null;
    return await res.json();
  } catch (error) {
    console.warn('Dashboard request failed:', url, error);
    return null;
  }
}

export async function requestJson(url, method, body) {
  const res = await fetchApi(url, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const data = await res.json().catch(() => null);
  if (!res.ok) {
    const message = res.status === 401 && data?.error === 'unauthorized'
      ? 'Your session ended. Sign in again to continue.'
      : data?.error || data?.errorMessage || `The request failed (HTTP ${res.status}).`;
    const error = new Error(message);
    error.status = res.status;
    error.body = data;
    throw error;
  }
  return data;
}
