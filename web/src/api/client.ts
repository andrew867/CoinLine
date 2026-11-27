import { consoleSession } from './consoleSession'

export async function apiGet<T>(path: string): Promise<T> {
  const r = await apiFetch(path)
  if (!r.ok) throw new Error(`${path} ${r.status}`)
  return (await r.json()) as T
}

const jsonHeaders = { 'Content-Type': 'application/json' } as const

async function request(path: string, init: RequestInit, token: string | null, apiKey: string | null): Promise<Response> {
  const origin = window.location.origin
  const url = new URL(path, origin)
  if (url.origin !== origin || url.username || url.password ||
      (url.pathname !== '/api' && !url.pathname.startsWith('/api/')) || /%2f|%5c/i.test(url.pathname)) {
    throw new TypeError('CoinLine API requests must use a same-origin /api URL.')
  }

  const headers = new Headers(init.headers)
  headers.delete('Authorization')
  headers.delete('X-API-Key')
  if (token) headers.set('Authorization', `Bearer ${token}`)
  if (apiKey) headers.set('X-API-Key', apiKey)
  return fetch(url.href, { ...init, headers, redirect: 'error', mode: 'same-origin', credentials: 'omit' })
}

export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const sent = consoleSession.credentials()
  const response = await request(path, init, sent.token, sent.apiKey)
  if (response.status === 401) consoleSession.expireIfCurrent(sent)
  return response
}

export function apiGetRaw(path: string): Promise<Response> {
  return apiFetch(path)
}

export async function apiDownload(path: string, filename: string): Promise<void> {
  const response = await apiFetch(path)
  if (!response.ok) throw new Error(`Download failed (HTTP ${response.status}).`)
  const url = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  link.click()
  window.setTimeout(() => URL.revokeObjectURL(url), 0)
}

export function apiSignIn(username: string, password: string, apiKey: string): Promise<Response> {
  return request('/api/auth/login', {
    method: 'POST', headers: jsonHeaders, body: JSON.stringify({ username, password }),
  }, null, apiKey.trim())
}

export async function apiPost<T>(path: string, body: unknown): Promise<T> {
  const r = await apiPostRaw(path, body)
  if (!r.ok) throw new Error(`${path} ${r.status}`)
  return (await r.json()) as T
}

/** POST with optional JSON body — use when you need status / error payload without throwing. */
export async function apiPostRaw(path: string, body?: unknown): Promise<Response> {
  return apiFetch(path, {
    method: 'POST',
    headers: jsonHeaders,
    ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
  })
}

export async function apiPut(path: string, body: unknown): Promise<void> {
  const r = await apiFetch(path, { method: 'PUT', headers: jsonHeaders, body: JSON.stringify(body) })
  if (!r.ok) throw new Error(`${path} ${r.status}`)
}

export async function apiDelete(path: string): Promise<void> {
  const r = await apiFetch(path, { method: 'DELETE' })
  if (!r.ok) throw new Error(`${path} ${r.status}`)
}

export type BarePostOptions = { confirm?: boolean }

function bareUrl(path: string, options?: BarePostOptions): string {
  if (!options?.confirm) return path
  return path.includes('?') ? `${path}&confirm=true` : `${path}?confirm=true`
}

/** POST with no JSON body. Pass `confirm: true` for server-side destructive / state-changing actions. */
export async function apiPostBare(path: string, options?: BarePostOptions): Promise<Response> {
  return apiFetch(bareUrl(path, options), { method: 'POST' })
}

export async function apiPostBareOk(path: string, options?: BarePostOptions): Promise<void> {
  const r = await apiPostBare(path, options)
  if (!r.ok) throw new Error(`${path} ${r.status}`)
}

/** Expects 201 Created with JSON body (e.g. download retry). */
export async function apiPostBareCreated<T>(path: string, options?: BarePostOptions): Promise<T> {
  const r = await apiPostBare(path, options)
  if (!r.ok) throw new Error(`${path} ${r.status}`)
  return (await r.json()) as T
}
