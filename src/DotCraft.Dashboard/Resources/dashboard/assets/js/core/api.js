export const API = '/dashboard/api';

export async function fetchJson(url) {
  try {
    const res = await fetch(API + url, { credentials: 'same-origin' });
    if (!res.ok) return null;
    return await res.json();
  } catch (error) {
    console.warn('Dashboard request failed:', url, error);
    return null;
  }
}

export async function requestJson(url, method, body) {
  const res = await fetch(API + url, {
    method,
    credentials: 'same-origin',
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const data = await res.json().catch(() => null);
  if (!res.ok) {
    const message = res.status === 401
      ? 'Your session ended. Sign in again to continue.'
      : data?.error || `The request failed (HTTP ${res.status}).`;
    const error = new Error(message);
    error.status = res.status;
    error.body = data;
    throw error;
  }
  return data;
}
