import { useState } from 'react'
import type { FormEvent } from 'react'

export function LoginPage() {
  const [username, setUsername] = useState('operator')
  const [password, setPassword] = useState('')
  const [apiKey, setApiKey] = useState(localStorage.getItem('coinline.apiKey') ?? '')
  const [err, setErr] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setErr(null)
    try {
      if (apiKey.trim()) localStorage.setItem('coinline.apiKey', apiKey.trim())
      const r = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password }),
      })
      if (r.status === 401) {
        setErr('That username or password is not recognized.')
        return
      }
      if (!r.ok) throw new Error(String(r.status))
      const body = (await r.json()) as { token: string; displayName?: string }
      localStorage.setItem('coinline.token', body.token)
      if (body.displayName) localStorage.setItem('coinline.displayName', body.displayName)
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
            <input value={apiKey} onChange={(e) => setApiKey(e.target.value)} autoComplete="off" />
          </label>
        </details>
        <button type="submit" disabled={busy}>
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </div>
  )
}
