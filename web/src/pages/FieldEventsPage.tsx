import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Item = {
  id: string
  terminalId: string | null
  terminalName: string | null
  title: string
  summary: string | null
  capturedAtUtc: string
  sessionCorrelationId: string | null
  byteCount: number
}

type List = { total: number; note: string; items: Item[] }

export function FieldEventsPage() {
  const [data, setData] = useState<List | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    void fetch('/api/field-events', { headers })
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<List>
      })
      .then(setData)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
  }, [])

  return (
    <div>
      <h1>Alarms and cash</h1>
      <p className="page-intro">
        A phone that reports an alarm, a cash box, attention, or its status shows up here. Documented fields appear in
        the summary when the body is long enough; raw bytes stay authoritative.
      </p>
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!data && !err && <p>Loading…</p>}
      {data && data.items.length === 0 && <p>No alarms or cash reports yet.</p>}
      {data && data.items.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>When</th>
              <th>Phone</th>
              <th>What</th>
              <th>Summary</th>
              <th>Bytes</th>
            </tr>
          </thead>
          <tbody>
            {data.items.map((row) => (
              <tr key={row.id}>
                <td>{new Date(row.capturedAtUtc).toLocaleString()}</td>
                <td>
                  {row.terminalId ? (
                    <Link to={`/terminals/${row.terminalId}`}>{row.terminalName ?? 'Phone'}</Link>
                  ) : (
                    'Unknown phone'
                  )}
                </td>
                <td>
                  <Link to={`/dlog/${row.id}`}>{row.title}</Link>
                </td>
                <td>{row.summary ?? '—'}</td>
                <td>{row.byteCount}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
