import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { apiFetch, apiSignIn } from './client'
import { consoleSession } from './consoleSession'
import { readDownloadReviews, rememberDownloadReview } from './downloadReviews'

describe('console API credentials', () => {
  const fetchStub = vi.fn<typeof fetch>()

  beforeEach(() => {
    fetchStub.mockReset().mockResolvedValue(new Response('{}'))
    vi.stubGlobal('fetch', fetchStub)
    consoleSession.save('operator-token', 'deployment-key', 'Signed-in operator', 'Operator', { phones: 1 })
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
    expect(consoleSession.role()).toBeNull()
    expect(consoleSession.permissions()).toBeNull()
    await apiFetch('/api/terminals')
    const signedOut = new Headers(fetchStub.mock.calls[2][1]?.headers)
    expect(signedOut.has('Authorization')).toBe(false)
    expect(signedOut.has('X-API-Key')).toBe(false)
  })

  it('invalidates the current authenticated session once when simultaneous API requests receive 401', async () => {
    rememberDownloadReview({terminalId:'fixture',tableSetId:null,reason:'registration'})
    const changed = vi.fn(), unsubscribe = consoleSession.subscribe(changed)
    let answerFirst!: (response: Response) => void, answerSecond!: (response: Response) => void
    fetchStub.mockImplementationOnce(() => new Promise(resolve => { answerFirst = resolve }))
      .mockImplementationOnce(() => new Promise(resolve => { answerSecond = resolve }))
    const first = apiFetch('/api/terminals'), second = apiFetch('/api/downloads')
    answerFirst(new Response(null,{status:401})); expect((await first).status).toBe(401)
    answerSecond(new Response(null,{status:401})); expect((await second).status).toBe(401)
    expect(changed).toHaveBeenCalledTimes(1)
    expect(consoleSession.token()).toBeNull()
    expect(consoleSession.apiKey()).toBeNull()
    expect(consoleSession.role()).toBeNull()
    expect(consoleSession.permissions()).toBeNull()
    expect(localStorage.getItem('coinline.displayName')).toBeNull()
    expect(readDownloadReviews()).toEqual([])
    expect(consoleSession.wasExpired()).toBe(true)
    unsubscribe()
  })

  it('does not let a late 401 from a prior session or prior API key sign out a newly authenticated operator', async () => {
    let answer!: (response: Response) => void
    fetchStub.mockImplementationOnce(() => new Promise(resolve => { answer = resolve }))
    const pending = apiFetch('/api/terminals')
    consoleSession.save('operator-token','replacement-key','New session','Operator')
    answer(new Response(null,{status:401})); await pending
    expect(consoleSession.token()).toBe('operator-token')
    expect(consoleSession.apiKey()).toBe('replacement-key')
    expect(consoleSession.wasExpired()).toBe(false)
    fetchStub.mockImplementationOnce(() => new Promise(resolve => { answer = resolve }))
    const pendingKey = apiFetch('/api/terminals')
    consoleSession.setApiKey('changed-key'); consoleSession.setApiKey('replacement-key')
    answer(new Response(null,{status:401})); await pendingKey
    expect(consoleSession.token()).toBe('operator-token')
  })

  it('does not log out on permissions errors, failed login, transport errors or anonymous API requests', async () => {
    fetchStub.mockResolvedValueOnce(new Response(null,{status:403}))
      .mockResolvedValueOnce(new Response(null,{status:401}))
      .mockRejectedValueOnce(new TypeError('Network unavailable'))
    expect((await apiFetch('/api/terminals')).status).toBe(403)
    expect((await apiSignIn('desk','wrong','')).status).toBe(401)
    await expect(apiFetch('/api/terminals')).rejects.toThrow('Network unavailable')
    expect(consoleSession.token()).toBe('operator-token')
    expect(consoleSession.wasExpired()).toBe(false)
    consoleSession.clear()
    fetchStub.mockResolvedValueOnce(new Response(null,{status:401}))
    expect((await apiFetch('/api/terminals')).status).toBe(401)
    expect(consoleSession.wasExpired()).toBe(false)
  })
})
