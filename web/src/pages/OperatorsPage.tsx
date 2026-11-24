import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { ErrorBanner } from '../components/AppStates'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Account = {
  id: string
  username: string
  displayName: string
  role: string
  enabled: boolean
}

const roles = ['Viewer', 'Operator', 'Technician', 'Admin']

export function OperatorsPage() {
  const [rows, setRows] = useState<Account[] | null>(null)
  const [username, setUsername] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState('Operator')
  const [err, setErr] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  async function load() {
    const r = await fetch('/api/auth/accounts', { headers })
    if (!r.ok) throw new Error(String(r.status))
    setRows((await r.json()) as Account[])
  }

  useEffect(() => {
    void load().catch((e) => setErr(e instanceof Error ? e.message : 'error'))
  }, [])

  async function onCreate(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setErr(null)
    try {
      const r = await fetch('/api/auth/accounts', {
        method: 'POST',
        headers: { ...headers, 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, displayName, password, role }),
      })
      if (!r.ok) {
        const body = (await r.json().catch(() => null)) as { error?: string } | null
        throw new Error(body?.error ?? String(r.status))
      }
      setUsername('')
      setDisplayName('')
      setPassword('')
      await load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'Could not add that person')
    } finally {
      setSaving(false)
    }
  }

  async function setEnabled(row: Account, enabled: boolean) {
    setErr(null)
    const r = await fetch(`/api/auth/accounts/${row.id}`, {
      method: 'PUT',
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify({ enabled }),
    })
    if (!r.ok) {
      const body = (await r.json().catch(() => null)) as { error?: string } | null
      setErr(body?.error ?? 'Could not update that person')
      return
    }
    await load()
  }

  return (
    <div>
      <h1>People</h1>
      <p className="page-intro">People who can sign in. The lab account is operator.</p>
      {err && <ErrorBanner message={err} />}
      <form onSubmit={(e) => void onCreate(e)} className="row-actions">
        <label>
          Username
          <input value={username} onChange={(e) => setUsername(e.target.value)} required minLength={2} />
        </label>
        <label>
          Name
          <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} required minLength={2} />
        </label>
        <label>
          Password
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            minLength={8}
            autoComplete="new-password"
          />
        </label>
        <label>
          Role
          <select value={role} onChange={(e) => setRole(e.target.value)}>
            {roles.map((item) => (
              <option key={item}>{item}</option>
            ))}
          </select>
        </label>
        <button type="submit" disabled={saving}>
          {saving ? 'Saving…' : 'Add person'}
        </button>
      </form>
      {!rows && <p>Loading…</p>}
      {rows && (
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Username</th>
              <th>Role</th>
              <th>Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id}>
                <td>{row.displayName}</td>
                <td>{row.username}</td>
                <td>{row.role}</td>
                <td>{row.enabled ? 'Enabled' : 'Disabled'}</td>
                <td>
                  <button type="button" className="quiet" onClick={() => void setEnabled(row, !row.enabled)}>
                    {row.enabled ? 'Disable' : 'Enable'}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
