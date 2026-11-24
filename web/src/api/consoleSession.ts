const keys = {
  token: 'coinline.token', apiKey: 'coinline.apiKey', displayName: 'coinline.displayName',
  role: 'coinline.role', permissions: 'coinline.permissions',
}
export type ScreenPermissions = Record<string, number>

export const permissionOperation = { read: 1, update: 2, add: 4, delete: 8 } as const

export function canPerform(screen: string, operation: number): boolean {
  const profile = consoleSession.permissions()
  return consoleSession.role() === 'Admin' || profile === null || ((profile[screen] ?? 0) & operation) === operation
}

export const consoleSession = {
  token: () => localStorage.getItem(keys.token),
  apiKey: () => localStorage.getItem(keys.apiKey),
  role: () => localStorage.getItem(keys.role),
  permissions(): ScreenPermissions | null {
    const raw = localStorage.getItem(keys.permissions)
    if (raw === null) return null
    try {
      const value = JSON.parse(raw)
      return value && typeof value === 'object' && !Array.isArray(value) ? value as ScreenPermissions : {}
    } catch { return {} }
  },
  setApiKey(apiKey: string) {
    const value = apiKey.trim()
    if (value) localStorage.setItem(keys.apiKey, value)
    else localStorage.removeItem(keys.apiKey)
  },
  save(token: string, apiKey: string, displayName?: string, role?: string, permissions?: ScreenPermissions | null) {
    localStorage.setItem(keys.token, token)
    consoleSession.setApiKey(apiKey)
    if (displayName) localStorage.setItem(keys.displayName, displayName)
    else localStorage.removeItem(keys.displayName)
    if (role) localStorage.setItem(keys.role, role)
    else localStorage.removeItem(keys.role)
    if (permissions != null) localStorage.setItem(keys.permissions, JSON.stringify(permissions))
    else localStorage.removeItem(keys.permissions)
  },
  clear() {
    for (const key of Object.values(keys)) localStorage.removeItem(key)
  },
}
