import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api/client'


type Row = {
  id: string
  terminalId: string | null
  status: number
  idempotencyKey: string
  isTerminalCallDetail?: boolean
  isTerminalStatistics?: boolean
  statisticsMessageType?: number | null
  statisticsName?: string | null
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
  const [reportType, setReportType] = useState('')

  useEffect(() => {
    setErr(null)
    apiFetch('/api/uploads')
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Row[]>
      })
      .then(setRows)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
  }, [])

  const statisticsTypes = new Map<number, string>()
  for (const row of rows ?? []) {
    if (row.isTerminalStatistics && row.statisticsMessageType != null)
      statisticsTypes.set(row.statisticsMessageType, row.statisticsName ?? 'Statistics')
  }
  const visibleRows = rows?.filter((row) => (!source || sourceLabel(row) === source)
    && (!reportType || (row.isTerminalStatistics && String(row.statisticsMessageType) === reportType)))

  return (
    <div>
      <h1>Upload batches</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Review call details, statistics and manually imported diagnostic batches.
      </p>
      <label>
        Source{' '}
        <select value={source} onChange={(event) => {
          setSource(event.target.value as Source | '')
          setReportType('')
        }}>
          <option value="">All uploads</option>
          {sources.map((label) => <option key={label}>{label}</option>)}
        </select>
      </label>
      {(!source || source === 'Terminal statistics') && statisticsTypes.size > 0 && <label>
        {' '}Statistics report{' '}
        <select value={reportType} onChange={event => setReportType(event.target.value)}>
          <option value="">All report types</option>
          {[...statisticsTypes].sort(([a], [b]) => a - b).map(([type, name]) =>
            <option key={type} value={type}>{name} (MT{type})</option>)}
        </select>
      </label>}
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!rows && <p>Loading…</p>}
      {visibleRows?.length === 0 && <p style={{ color: '#666' }}>{source || reportType ? 'No matching uploads.' : 'No uploads yet.'}</p>}
      {visibleRows && visibleRows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>Id</th>
              <th>Status</th>
              <th>Source</th>
              <th>Report</th>
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
                <td>{b.isTerminalStatistics && b.statisticsMessageType != null
                  ? `${b.statisticsName ?? 'Statistics'} (MT${b.statisticsMessageType})` : '—'}</td>
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
