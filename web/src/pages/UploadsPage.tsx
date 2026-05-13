import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Row = {
  id: string
  terminalId: string | null
  status: number
  idempotencyKey: string
  rawPayloadHex: string
  recordCount: number
}

const statusLabel = (n: number) =>
  (
    {
      0: 'Received',
      1: 'Processing',
      2: 'Completed',
      3: 'Failed',
      4: 'Quarantined',
    } as Record<number, string>
  )[n] ?? String(n)

export function UploadsPage() {
  const [rows, setRows] = useState<Row[] | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    setErr(null)
    fetch('/api/uploads', { headers })
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Row[]>
      })
      .then(setRows)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
  }, [])

  return (
    <div>
      <h1>Upload batches</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Host-ingested payloads (CDR / diagnostic batches). Use ingest on the detail page to derive{' '}
        <strong>records</strong> from JSON arrays, a <code>records</code> envelope, or a single monolithic binary slice.
      </p>
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!rows && <p>Loading…</p>}
      {rows && rows.length === 0 && <p style={{ color: '#666' }}>No uploads yet.</p>}
      {rows && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>Id</th>
              <th>Status</th>
              <th>Records</th>
              <th>Terminal</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((b) => (
              <tr key={b.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/uploads/${b.id}`}>{b.id.slice(0, 8)}…</Link>
                </td>
                <td>{statusLabel(b.status)}</td>
                <td>{b.recordCount}</td>
                <td>{b.terminalId ? <code>{b.terminalId.slice(0, 8)}…</code> : '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
