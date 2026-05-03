import { useEffect, useState } from 'react'
import { apiGet } from '../api/client'

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

export function NumberClassesPage() {
  const [rows, setRows] = useState<Row[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    setLoading(true)
    setErr(null)
    apiGet<Row[]>('/api/number-classes')
      .then(setRows)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setLoading(false))
  }, [])

  return (
    <div>
      <h1>Number classes</h1>
      <p style={{ maxWidth: 720 }}>
        Global and per-customer overrides apply <strong>before</strong> rate-plan rules. Blocked / free / emergency flags
        are shown below. API: creating a <strong>blocked</strong> class requires <code>confirm: true</code>.
      </p>
      {loading && <p role="status">Loading…</p>}
      {!loading && err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!loading && !err && rows.length === 0 && (
        <p role="status">No number classes — seed data or POST /api/number-classes.</p>
      )}
      {!loading && !err && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ borderBottom: '1px solid #ccc' }}>
              <th align="left">Class</th>
              <th align="left">Pattern</th>
              <th align="left">Blocked</th>
              <th align="left">Free</th>
              <th align="left">Emergency</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>{r.className}</td>
                <td>{r.pattern}</td>
                <td>{r.isBlocked ? 'yes' : ''}</td>
                <td>{r.isFree ? 'yes' : ''}</td>
                <td>{r.isEmergency ? 'yes' : ''}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
