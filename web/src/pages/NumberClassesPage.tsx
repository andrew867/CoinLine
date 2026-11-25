import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { apiFetch, apiGet } from '../api/client'

type Row = {
  id: string
  customerId: string | null
  className: string
  pattern: string
  matchKind: number
  isBlocked: boolean
  isFree: boolean
  isEmergency: boolean
  sortOrder: number
}

const headers = { 'Content-Type': 'application/json' }

export function NumberClassesPage() {
  const [rows, setRows] = useState<Row[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [className, setClassName] = useState('')
  const [pattern, setPattern] = useState('')
  const [matchKind, setMatchKind] = useState(0)
  const [isBlocked, setIsBlocked] = useState(false)
  const [isFree, setIsFree] = useState(false)
  const [isEmergency, setIsEmergency] = useState(false)
  const [saving, setSaving] = useState(false)

  async function load() {
    setRows(await apiGet<Row[]>('/api/number-classes'))
  }

  useEffect(() => {
    setLoading(true)
    setErr(null)
    load()
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setLoading(false))
  }, [])

  async function onCreate(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setErr(null)
    try {
      const r = await apiFetch('/api/number-classes', {
        method: 'POST',
        headers,
        body: JSON.stringify({
          customerId: null,
          className: className.trim(),
          pattern: pattern.trim(),
          matchKind,
          isBlocked,
          isFree,
          isEmergency,
          sortOrder: 100,
          confirm: isBlocked,
        }),
      })
      if (!r.ok) {
        const body = (await r.json().catch(() => null)) as { error?: string } | null
        throw new Error(body?.error ?? String(r.status))
      }
      setClassName('')
      setPattern('')
      setIsBlocked(false)
      setIsFree(false)
      setIsEmergency(false)
      await load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'Could not add that class')
    } finally {
      setSaving(false)
    }
  }

  const how = (kind: number) => (kind === 2 ? 'Exact' : kind === 1 ? 'Pattern' : 'Starts with')

  return (
    <div>
      <h1>Number classes</h1>
      <p className="page-intro">
        Blocked, free, and emergency numbers apply before the rate plan.
      </p>
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      <form onSubmit={(e) => void onCreate(e)} className="stack-form">
        <label>
          Name
          <input value={className} onChange={(e) => setClassName(e.target.value)} required />
        </label>
        <label>
          Digits
          <input value={pattern} onChange={(e) => setPattern(e.target.value)} required />
        </label>
        <label>
          Match
          <select value={matchKind} onChange={(e) => setMatchKind(Number(e.target.value))}>
            <option value={0}>Starts with</option>
            <option value={2}>Exact</option>
            <option value={1}>Pattern</option>
          </select>
        </label>
        <label>
          <input type="checkbox" checked={isBlocked} onChange={(e) => setIsBlocked(e.target.checked)} /> Blocked
        </label>
        <label>
          <input type="checkbox" checked={isFree} onChange={(e) => setIsFree(e.target.checked)} /> Free
        </label>
        <label>
          <input type="checkbox" checked={isEmergency} onChange={(e) => setIsEmergency(e.target.checked)} /> Emergency
        </label>
        <button type="submit" disabled={saving}>
          {saving ? 'Saving…' : 'Add class'}
        </button>
      </form>
      {loading && <p role="status">Loading…</p>}
      {!loading && rows.length === 0 && <p role="status">No number classes yet.</p>}
      {!loading && rows.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Class</th>
              <th>Digits</th>
              <th>Match</th>
              <th>Blocked</th>
              <th>Free</th>
              <th>Emergency</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id}>
                <td>{r.className}</td>
                <td>{r.pattern}</td>
                <td>{how(r.matchKind)}</td>
                <td>{r.isBlocked ? 'Yes' : ''}</td>
                <td>{r.isFree ? 'Yes' : ''}</td>
                <td>{r.isEmergency ? 'Yes' : ''}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
