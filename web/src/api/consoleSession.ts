const keys = { token: 'coinline.token', apiKey: 'coinline.apiKey', displayName: 'coinline.displayName' }

export const consoleSession = {
  token: () => localStorage.getItem(keys.token),
  apiKey: () => localStorage.getItem(keys.apiKey),
  setApiKey(apiKey: string) {
    const value = apiKey.trim()
    if (value) localStorage.setItem(keys.apiKey, value)
    else localStorage.removeItem(keys.apiKey)
  },
  save(token: string, apiKey: string, displayName?: string) {
    localStorage.setItem(keys.token, token)
    consoleSession.setApiKey(apiKey)
    if (displayName) localStorage.setItem(keys.displayName, displayName)
    else localStorage.removeItem(keys.displayName)
  },
  clear() {
    for (const key of Object.values(keys)) localStorage.removeItem(key)
  },
}
