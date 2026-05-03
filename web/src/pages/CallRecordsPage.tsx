import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet } from '../api/client'

type Row = {
  id: string
  terminalId: string | null
  dialedDigits: string
  mode: number
  disposition: number
  startedAtUtc: string
  reconciliation: number
  appliedRatePlanVersionId: string | null
}

export function CallRecordsPage() {
  const [rows, setRows] = useState<Row[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    setLoading(true)
    setErr(null)
    apiGet<Row[]>('/api/call-records')
      .then(setRows)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setLoading(false))
  }, [])

  return (
    <div>
      <h1>Call records</h1>
      {loading && <p role="status">Loading…</p>}
      {!loading && err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!loading && !err && rows.length === 0 && (
        <p role="status">No call records yet — POST /api/call-records or complete a rated call flow.</p>
      )}
      {!loading && !err && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ borderBottom: '1px solid #ccc' }}>
              <th align="left">Dialed</th>
              <th align="left">Disposition</th>
              <th align="left">Reconciliation</th>
              <th align="left">Started</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/call-records/${r.id}`}>{r.dialedDigits}</Link>
                </td>
                <td>{r.disposition}</td>
                <td>{r.reconciliation}</td>
                <td>{r.startedAtUtc}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
