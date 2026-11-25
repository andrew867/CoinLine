import type { FormEvent } from 'react'
import { useCallback, useEffect, useState } from 'react'
import { ErrorBanner } from '../components/AppStates'
import { OperatorPermissionsPanel } from '../components/OperatorPermissionsPanel'
import { apiFetch, apiPostRaw } from '../api/client'

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
  const [profileId, setProfileId] = useState<string | null>(null)

  const load = useCallback(async () => {
    setErr(null)
    try {
      const r = await apiFetch('/api/auth/accounts')
      if (!r.ok) {
        setErr(r.status === 401 ? 'Sign in again to view People.'
          : r.status === 403 ? 'An administrator account is required to view People.'
          : `Could not load People (HTTP ${r.status}). Try again.`)
        return
      }
      setRows((await r.json()) as Account[])
    } catch {
      setErr('Could not read the People API response. Try again.')
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  async function onCreate(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setErr(null)
    try {
      const r = await apiPostRaw('/api/auth/accounts', { username, displayName, password, role })
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
    try {
      const r = await apiFetch(`/api/auth/accounts/${row.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ enabled }),
      })
      if (!r.ok) {
        const body = (await r.json().catch(() => null)) as { error?: string } | null
        setErr(body?.error ?? `Could not update that person (HTTP ${r.status}).`)
        return
      }
      await load()
    } catch {
      setErr('Could not update that person. Try again.')
    }
  }

  return (
    <div>
      <h1>People</h1>
      <p className="page-intro">People who can sign in. The lab account is operator.</p>
      {err && <ErrorBanner message={err} />}
      {err && !rows && <button type="button" onClick={() => void load()}>Retry loading People</button>}
      <form onSubmit={(e) => void onCreate(e)} className="row-actions">
        <label>
          Username
          <input data-field-kind="key" value={username} onChange={(e) => setUsername(e.target.value)} required minLength={2} />
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
        <button type="submit" disabled={saving || !rows}>
          {saving ? 'Saving…' : 'Add person'}
        </button>
      </form>
      {!rows && !err && <p>Loading…</p>}
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
                  <button type="button" className="quiet" onClick={() => setProfileId(row.id)}>Permissions</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {rows && profileId && rows.find((row) => row.id === profileId) && (
        <OperatorPermissionsPanel key={profileId} person={rows.find((row) => row.id === profileId)!}
          people={rows} onClose={() => setProfileId(null)} />
      )}
    </div>
  )
}
