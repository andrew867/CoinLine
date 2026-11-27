import { clearDownloadReviews } from './downloadReviews'

const keys = {
  token: 'coinline.token', apiKey: 'coinline.apiKey', displayName: 'coinline.displayName',
  role: 'coinline.role', permissions: 'coinline.permissions',
}
const sessionEvent = 'coinline:console-session'
let sessionRevision = 0
let expiredSession = false
type SessionCredentials = { token: string | null; apiKey: string | null; revision: number }

function notifySessionChanged() {
  sessionRevision += 1
  window.dispatchEvent(new Event(sessionEvent))
}

function writeApiKey(apiKey: string) {
  const value = apiKey.trim()
  if (value) localStorage.setItem(keys.apiKey, value)
  else localStorage.removeItem(keys.apiKey)
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
  credentials(): SessionCredentials {
    return { token: consoleSession.token(), apiKey: consoleSession.apiKey(), revision: sessionRevision }
  },
  wasExpired: () => expiredSession,
  subscribe(listener: () => void) {
    const syncTab = (event: StorageEvent) => {
      if (event.storageArea !== localStorage || (event.key !== keys.token && event.key !== keys.apiKey && event.key !== null)) return
      sessionRevision += 1
      if (event.key !== keys.apiKey) {
        expiredSession = false
        clearDownloadReviews()
      }
      listener()
    }
    window.addEventListener(sessionEvent, listener)
    window.addEventListener('storage', syncTab)
    return () => {
      window.removeEventListener(sessionEvent, listener)
      window.removeEventListener('storage', syncTab)
    }
  },
  permissions(): ScreenPermissions | null {
    const raw = localStorage.getItem(keys.permissions)
    if (raw === null) return null
    try {
      const value = JSON.parse(raw)
      return value && typeof value === 'object' && !Array.isArray(value) ? value as ScreenPermissions : {}
    } catch { return {} }
  },
  setApiKey(apiKey: string) {
    const previous = consoleSession.apiKey()
    writeApiKey(apiKey)
    if (consoleSession.apiKey() !== previous) notifySessionChanged()
  },
  save(token: string, apiKey: string, displayName?: string, role?: string, permissions?: ScreenPermissions | null) {
    if (localStorage.getItem(keys.token) !== token) clearDownloadReviews()
    localStorage.setItem(keys.token, token)
    writeApiKey(apiKey)
    if (displayName) localStorage.setItem(keys.displayName, displayName)
    else localStorage.removeItem(keys.displayName)
    if (role) localStorage.setItem(keys.role, role)
    else localStorage.removeItem(keys.role)
    if (permissions != null) localStorage.setItem(keys.permissions, JSON.stringify(permissions))
    else localStorage.removeItem(keys.permissions)
    expiredSession = false
    notifySessionChanged()
  },
  clear(reason?: 'expired') {
    for (const key of Object.values(keys)) localStorage.removeItem(key)
    clearDownloadReviews()
    expiredSession = reason === 'expired'
    notifySessionChanged()
  },
  expireIfCurrent(sent: SessionCredentials) {
    const current = consoleSession.credentials()
    // A late response from a previous session must not sign out a newly authenticated operator.
    if (!sent.token || sent.revision !== current.revision || sent.token !== current.token || sent.apiKey !== current.apiKey) return false
    consoleSession.clear('expired')
    return true
  },
}
