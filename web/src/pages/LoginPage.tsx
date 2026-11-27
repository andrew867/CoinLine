import { useState } from 'react'
import type { FormEvent } from 'react'
import { useLocation } from 'react-router-dom'
import { apiSignIn } from '../api/client'
import { consoleSession, type ScreenPermissions } from '../api/consoleSession'

export function LoginPage() {
  const location = useLocation()
  const expired = location.state?.sessionExpired === true
  const [username, setUsername] = useState('operator')
  const [password, setPassword] = useState('')
  const [apiKey, setApiKey] = useState(() => consoleSession.apiKey() ?? '')
  const [err, setErr] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setErr(null)
    try {
      const r = await apiSignIn(username, password, apiKey)
      const body = (await r.json().catch(() => null)) as {
        token?: string; displayName?: string; role?: string; permissions?: ScreenPermissions | null; error?: string
      } | null
      if (!r.ok) throw new Error(body?.error ?? `Could not sign in (HTTP ${r.status}).`)
      if (!body?.token) throw new Error('The server did not return a sign-in token.')
      consoleSession.save(body.token, apiKey, body.displayName, body.role, body.permissions)
      window.location.assign('/')
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'Could not sign in')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login-wrap">
      <form className="login-card" onSubmit={(e) => void onSubmit(e)}>
        <h1>CoinLine</h1>
        <p className="page-intro">Sign in to manage the phones.</p>
        {expired && <p role="status">Your session expired. Sign in again.</p>}
        {err && <p style={{ color: 'crimson' }}>{err}</p>}
        <label>
          Username
          <input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" required />
        </label>
        <label>
          Password
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
            required
          />
        </label>
        <details>
          <summary>API key, when the server requires one</summary>
          <label>
            API key
            <input type="password" value={apiKey} onChange={(e) => setApiKey(e.target.value)} autoComplete="off" />
          </label>
        </details>
        <button type="submit" disabled={busy}>
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </div>
  )
}
