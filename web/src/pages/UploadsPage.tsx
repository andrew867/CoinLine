import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Row = {
  id: string
  terminalId: string | null
  status: number
  idempotencyKey: string
  isTerminalCallDetail?: boolean
  isTerminalStatistics?: boolean
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

const sources = ['Terminal call detail', 'Terminal statistics', 'Imported batch'] as const
type Source = typeof sources[number]
const sourceLabel = (row: Row): Source => row.isTerminalCallDetail ? sources[0] : row.isTerminalStatistics ? sources[1] : sources[2]

export function UploadsPage() {
  const [rows, setRows] = useState<Row[] | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [source, setSource] = useState<Source | ''>('')

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

  const visibleRows = rows?.filter((row) => !source || sourceLabel(row) === source)

  return (
    <div>
      <h1>Upload batches</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Review call details, statistics and manually imported diagnostic batches.
      </p>
      <label>
        Source{' '}
        <select value={source} onChange={(event) => setSource(event.target.value as Source | '')}>
          <option value="">All uploads</option>
          {sources.map((label) => <option key={label}>{label}</option>)}
        </select>
      </label>
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!rows && <p>Loading…</p>}
      {visibleRows?.length === 0 && <p style={{ color: '#666' }}>{source ? 'No uploads from this source.' : 'No uploads yet.'}</p>}
      {visibleRows && visibleRows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>Id</th>
              <th>Status</th>
              <th>Source</th>
              <th>Records</th>
              <th>Terminal</th>
            </tr>
          </thead>
          <tbody>
            {visibleRows.map((b) => (
              <tr key={b.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/uploads/${b.id}`}>{b.id.slice(0, 8)}…</Link>
                </td>
                <td>{statusLabel(b.status)}</td>
                <td>{sourceLabel(b)}</td>
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
