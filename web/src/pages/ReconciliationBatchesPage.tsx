import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet } from '../api/client'

type Row = {
  id: string
  status: number
  createdAtUtc: string
  postedAtUtc: string | null
  closedAtUtc: string | null
}

export function ReconciliationBatchesPage() {
  const [rows, setRows] = useState<Row[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    setLoading(true)
    setErr(null)
    apiGet<Row[]>('/api/cards/reconciliation-batches')
      .then(setRows)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setLoading(false))
  }, [])

  return (
    <div>
      <h1>Card reconciliation batches</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Open → Posted → Closed transitions emit audit events. Close requires <code>confirm: true</code> on the API.
      </p>
      {loading && <p role="status">Loading…</p>}
      {!loading && err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!loading && !err && rows.length === 0 && (
        <p role="status">No batches — POST /api/cards/reconciliation-batches to create.</p>
      )}
      {!loading && !err && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ borderBottom: '1px solid #ccc' }}>
              <th align="left">Created</th>
              <th align="left">Status</th>
              <th align="left">Posted</th>
              <th align="left">Closed</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/card-reconciliation/${r.id}`}>{new Date(r.createdAtUtc).toLocaleString()}</Link>
                </td>
                <td>{r.status}</td>
                <td>{r.postedAtUtc ? new Date(r.postedAtUtc).toLocaleString() : '—'}</td>
                <td>{r.closedAtUtc ? new Date(r.closedAtUtc).toLocaleString() : '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
