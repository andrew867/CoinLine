export async function apiGet<T>(path: string): Promise<T> {
  const r = await fetch(path, { headers: { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' } })
  if (!r.ok) throw new Error(`${path} ${r.status}`)
  return (await r.json()) as T
}

const jsonHeaders = {
  'Content-Type': 'application/json',
  'X-Operator-Id': 'ui@local',
  'X-Operator-Role': 'Admin',
} as const

const opHeaders = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' } as const

export async function apiPost<T>(path: string, body: unknown): Promise<T> {
  const r = await fetch(path, {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify(body),
  })
  if (!r.ok) throw new Error(`${path} ${r.status}`)
  return (await r.json()) as T
}

/** POST with optional JSON body — use when you need status / error payload without throwing. */
export async function apiPostRaw(path: string, body?: unknown): Promise<Response> {
  return fetch(path, {
    method: 'POST',
    headers: jsonHeaders,
    ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
  })
}

export async function apiPut(path: string, body: unknown): Promise<void> {
  const r = await fetch(path, { method: 'PUT', headers: jsonHeaders, body: JSON.stringify(body) })
  if (!r.ok) throw new Error(`${path} ${r.status}`)
}

export async function apiDelete(path: string): Promise<void> {
  const r = await fetch(path, { method: 'DELETE', headers: opHeaders })
  if (!r.ok) throw new Error(`${path} ${r.status}`)
}

export type BarePostOptions = { confirm?: boolean }

function bareUrl(path: string, options?: BarePostOptions): string {
  if (!options?.confirm) return path
  return path.includes('?') ? `${path}&confirm=true` : `${path}?confirm=true`
}

/** POST with no JSON body. Pass `confirm: true` for server-side destructive / state-changing actions. */
export async function apiPostBare(path: string, options?: BarePostOptions): Promise<Response> {
  return fetch(bareUrl(path, options), { method: 'POST', headers: opHeaders })
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
