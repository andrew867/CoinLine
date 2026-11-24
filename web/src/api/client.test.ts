import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { apiFetch, apiSignIn } from './client'
import { consoleSession } from './consoleSession'

describe('console API credentials', () => {
  const fetchStub = vi.fn<typeof fetch>()

  beforeEach(() => {
    fetchStub.mockReset().mockResolvedValue(new Response('{}'))
    vi.stubGlobal('fetch', fetchStub)
    consoleSession.save('operator-token', 'deployment-key', 'Signed-in operator')
  })

  afterEach(() => {
    consoleSession.clear()
    vi.unstubAllGlobals()
  })

  it('sends an operator token and optional API key with same-origin API requests and blocks redirects', async () => {
    const url = `${window.location.origin}/api/auth/accounts`
    await apiFetch(url, { redirect: 'follow', mode: 'cors', credentials: 'include' })
    expect(fetchStub).toHaveBeenCalledWith(url, expect.objectContaining({
      redirect: 'error', mode: 'same-origin', credentials: 'omit',
    }))
    const headers = new Headers(fetchStub.mock.calls[0][1]?.headers)
    expect(headers.get('Authorization')).toBe('Bearer operator-token')
    expect(headers.get('X-API-Key')).toBe('deployment-key')
  })

  it('rejects unrelated URLs through the client and leaves ordinary browser requests alone', async () => {
    for (const path of ['https://outside.example/api/auth/accounts', '//outside.example/api', '/health', '/apix', '/api/../health', '/api/%2Fhealth']) {
      await expect(apiFetch(path)).rejects.toThrow('same-origin /api URL')
    }
    expect(fetchStub).not.toHaveBeenCalled()
    await window.fetch('/health')
    await window.fetch('https://outside.example/resource')
    expect(fetchStub.mock.calls).toEqual([['/health'], ['https://outside.example/resource']])
  })

  it('uses only the supplied API key at login and removes credentials and cached identity on sign-out', async () => {
    await apiSignIn('desk', 'fixture-password', ' new-key ')
    const loginHeaders = new Headers(fetchStub.mock.calls[0][1]?.headers)
    expect(loginHeaders.has('Authorization')).toBe(false)
    expect(loginHeaders.get('X-API-Key')).toBe('new-key')
    await apiSignIn('desk', 'fixture-password', '')
    expect(new Headers(fetchStub.mock.calls[1][1]?.headers).has('X-API-Key')).toBe(false)

    consoleSession.clear()
    expect(consoleSession.token()).toBeNull()
    expect(consoleSession.apiKey()).toBeNull()
    expect(localStorage.getItem('coinline.displayName')).toBeNull()
    await apiFetch('/api/terminals')
    const signedOut = new Headers(fetchStub.mock.calls[2][1]?.headers)
    expect(signedOut.has('Authorization')).toBe(false)
    expect(signedOut.has('X-API-Key')).toBe(false)
  })
})
