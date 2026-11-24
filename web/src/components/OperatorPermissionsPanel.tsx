import { useEffect, useState } from 'react'
import { apiFetch } from '../api/client'
import { ErrorBanner } from './AppStates'

type Person = { id: string; displayName: string; role: string }
type Permissions = Record<string, number>
type Profile = { role: string; revision: number; permissions: Permissions | null }
type Screen = { key: string; label: string; operations: number }

const operations = [
  ['Read', 1], ['Update', 2], ['Add', 4], ['Delete', 8],
] as const

async function readResponse<T>(response: Response): Promise<T> {
  const body = await response.json().catch(() => null)
  if (!response.ok) throw new Error(body?.error ?? `Request failed (HTTP ${response.status}).`)
  return body as T
}

export function OperatorPermissionsPanel({ person, people, onClose }: {
  person: Person; people: Person[]; onClose: () => void
}) {
  const [catalog, setCatalog] = useState<Screen[]>([])
  const [profile, setProfile] = useState<Profile | null>(null)
  const [custom, setCustom] = useState(false)
  const [grants, setGrants] = useState<Permissions>({})
  const [copyId, setCopyId] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const path = `/api/auth/accounts/${person.id}/permissions`

  useEffect(() => {
    let active = true
    setProfile(null)
    setError('')
    setNotice('')
    void Promise.all([
      apiFetch('/api/auth/permission-catalog').then(readResponse<Screen[]>),
      apiFetch(path).then(readResponse<Profile>),
    ]).then(([screens, current]) => {
      if (!active) return
      setCatalog(screens)
      setProfile(current)
      setCustom(current.permissions !== null)
      setGrants(current.permissions ?? {})
    }).catch((ex: unknown) => {
      if (active) setError(ex instanceof Error ? ex.message : 'Could not load permissions.')
    })
    return () => { active = false }
  }, [path])

  function toggle(key: string, operation: number, checked: boolean) {
    setGrants((current) => ({
      ...current,
      [key]: checked ? (current[key] ?? 0) | operation : (current[key] ?? 0) & ~operation,
    }))
    setNotice('')
  }

  async function copy() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const source = await readResponse<Profile>(await apiFetch(`/api/auth/accounts/${copyId}/permissions`))
      setCustom(source.permissions !== null)
      setGrants(source.permissions ?? {})
      setNotice('Copied to this draft. Save to apply.')
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : 'Could not copy permissions.')
    } finally {
      setBusy(false)
    }
  }

  async function save() {
    if (!profile) return
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const current = await readResponse<Profile>(await apiFetch(path, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ revision: profile.revision, permissions: custom ? grants : null }),
      }))
      setProfile(current)
      setCustom(current.permissions !== null)
      setGrants(current.permissions ?? {})
      setNotice('Permissions saved. Earlier sessions for this person are no longer valid if the profile changed.')
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : 'Could not save permissions.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="panel" aria-label="Permission profile">
      <div className="row-actions">
        <h2>Permissions for {person.displayName}</h2>
        <button type="button" className="quiet" onClick={onClose}>Close profile</button>
      </div>
      {error && <ErrorBanner message={error} />}
      {notice && <p role="status">{notice}</p>}
      {!profile && !error && <p>Loading permissions…</p>}
      {profile?.role === 'Admin' && <p>Administrators have their full role privileges. Change the role before restricting a profile.</p>}
      {profile && profile.role !== 'Admin' && (
        <>
          <p>Profiles restrict the operations permitted by the account's role. Unchecked operations are denied.</p>
          <div className="row-actions">
            <label>
              Profile
              <select value={custom ? 'custom' : 'default'} disabled={busy}
                onChange={(e) => { setCustom(e.target.value === 'custom'); setNotice('') }}>
                <option value="default">Role defaults</option>
                <option value="custom">Custom permissions</option>
              </select>
            </label>
            <label>
              Copy from person
              <select value={copyId} onChange={(e) => setCopyId(e.target.value)} disabled={busy}>
                <option value="">Choose a person</option>
                {people.filter((other) => other.id !== person.id && other.role !== 'Admin')
                  .map((other) => <option key={other.id} value={other.id}>{other.displayName}</option>)}
              </select>
            </label>
            <button type="button" disabled={busy || !copyId} onClick={() => void copy()}>Copy permissions</button>
          </div>
          {custom && (
            <table>
              <thead><tr><th>Screen</th>{operations.map(([label]) => <th key={label}>{label}</th>)}</tr></thead>
              <tbody>
                {catalog.map((screen) => (
                  <tr key={screen.key}>
                    <th scope="row">{screen.label}</th>
                    {operations.map(([label, bit]) => (
                      <td key={bit}>
                        {(screen.operations & bit) !== 0
                          ? <input type="checkbox" aria-label={`${screen.label}: ${label}`} disabled={busy}
                              checked={((grants[screen.key] ?? 0) & bit) !== 0}
                              onChange={(e) => toggle(screen.key, bit, e.target.checked)} />
                          : <span aria-label="Not supported">·</span>}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <button type="button" disabled={busy} onClick={() => void save()}>{busy ? 'Working…' : 'Save permissions'}</button>
        </>
      )}
    </section>
  )
}
