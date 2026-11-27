import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPost } from '../api/client'
import { RatePlanPackagePanel } from './RatePlanPackagePanel'

type Row = {
  id: string
  name: string
  mode: number
  customerId: string | null
  publishedVersionId: string | null
}

export function RatePlansPage() {
  const [rows, setRows] = useState<Row[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [name, setName] = useState('')

  function load() {
    setLoading(true)
    setErr(null)
    apiGet<Row[]>('/api/rate-plans')
      .then(setRows)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setLoading(false))
  }

  useEffect(() => {
    load()
  }, [])

  async function createPlan(e: FormEvent) {
    e.preventDefault()
    const trimmed = name.trim()
    if (!trimmed) return
    setErr(null)
    try {
      await apiPost<{ id: string }>('/api/rate-plans', { name: trimmed, customerId: null, mode: 1 })
      setName('')
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  return (
    <div>
      <h1>Rate plans</h1>
      <p className="page-intro">
        Plans used when a phone asks for a price. Publish a version before phones can use it in the field.
      </p>
      <RatePlanPackagePanel onImported={load} sources={rows} />
      {loading && <p role="status">Loading rate plans…</p>}
      {!loading && err && <p style={{ color: 'crimson' }}>{err}</p>}
      <form onSubmit={(e) => void createPlan(e)} style={{ display: 'flex', gap: 8, alignItems: 'flex-end', marginBottom: 16 }}>
        <label>
          New plan name
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            style={{ display: 'block' }}
          />
        </label>
        <button type="submit">Create rate plan</button>
      </form>
      {!loading && !err && rows.length === 0 && <p role="status">No rate plans yet.</p>}
      {!loading && !err && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ borderBottom: '1px solid #ccc' }}>
              <th align="left">Name</th>
              <th align="left">Mode</th>
              <th align="left">Published version</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/rate-plans/${r.id}`}>{r.name}</Link>
                </td>
                <td>{r.mode}</td>
                <td>{r.publishedVersionId ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
