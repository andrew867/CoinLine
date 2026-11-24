import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPostBare } from '../api/client'

type Schedule = {
  id: string
  terminalId: string
  phone: string
  tableSetId: string
  tableSet: string
  availableAtUtc: string
  preparedAtUtc: string | null
  startedAtUtc: string | null
  completedAtUtc: string | null
  recordNumbers: number[] | null
  status: 'Scheduled' | 'Preparing' | 'Ready' | 'Delivering' | 'Retry pending' | 'Delivered' | 'Cancelled'
  lastBatchId: string | null
  lastError: string | null
}

export function TableDownloadScheduleList({ setId, terminalId, refresh = 0 }: {
  setId?: string
  terminalId?: string
  refresh?: number
}) {
  const [rows, setRows] = useState<Schedule[] | null>(null)
  const [revision, setRevision] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const endpoint = setId ? `/api/tables/sets/${setId}/schedules`
    : '/api/tables/schedules' + (terminalId ? `?terminalId=${encodeURIComponent(terminalId)}` : '')

  useEffect(() => {
    let current = true
    setError('')
    void apiGet<Schedule[]>(endpoint)
      .then((result) => { if (current) setRows(result) })
      .catch(() => { if (current) setError('Could not load scheduled downloads.') })
    return () => { current = false }
  }, [endpoint, refresh, revision])

  async function cancel(row: Schedule) {
    setBusy(true)
    setError('')
    try {
      const response = await apiPostBare(`/api/tables/sets/${row.tableSetId}/schedules/${row.id}/cancel`)
      if (!response.ok)
        throw new Error((await response.json().catch(() => null))?.error ?? 'Could not cancel the download.')
      setRevision((value) => value + 1)
    } catch (error) {
      setError(error instanceof Error ? error.message : 'Could not cancel the download.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <details>
      <summary>Scheduled downloads{rows ? ` (${rows.length})` : ''}</summary>
      <button type="button" disabled={busy} onClick={() => setRevision((value) => value + 1)}>Refresh schedules</button>
      {error && <p role="alert">{error}</p>}
      {rows?.length === 0 && <p>No scheduled downloads.</p>}
      {rows && rows.length > 0 && (
        <div style={{ overflowX: 'auto' }}>
          <table>
            <thead><tr><th>Phone</th>{!setId && <th>Table set</th>}<th>Records</th><th>Prepare at</th><th>Prepared</th><th>Status</th><th>Delivery</th></tr></thead>
            <tbody>{rows.map((row) => (
              <tr key={row.id}>
                <td><Link to={`/terminals/${row.terminalId}`}>{row.phone}</Link></td>
                {!setId && <td><Link to={`/table-sets/${row.tableSetId}`}>{row.tableSet}</Link></td>}
                <td>{row.recordNumbers?.map(number => `MT${number}`).join(', ') ?? 'All records'}</td>
                <td><time dateTime={row.availableAtUtc}>{new Date(row.availableAtUtc).toLocaleString()}</time></td>
                <td>{row.preparedAtUtc ? <time dateTime={row.preparedAtUtc}>{new Date(row.preparedAtUtc).toLocaleString()}</time> : 'Pending'}</td>
                <td>{row.status === 'Ready' ? 'Ready for pickup' : row.status}</td>
                <td>
                  {row.lastBatchId && <Link to={`/downloads/${row.lastBatchId}`}>{row.startedAtUtc ? 'Last attempt' : 'Prepared batch'}</Link>}
                  {row.startedAtUtc && <div>Started <time dateTime={row.startedAtUtc}>{new Date(row.startedAtUtc).toLocaleString()}</time></div>}
                  {row.lastError && <div>{row.lastError}</div>}
                  {row.completedAtUtc && <time dateTime={row.completedAtUtc}>{new Date(row.completedAtUtc).toLocaleString()}</time>}
                  {row.status !== 'Delivering' && row.status !== 'Delivered' && row.status !== 'Cancelled' &&
                    <button type="button" disabled={busy} onClick={() => void cancel(row)}>Cancel download</button>}
                </td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      )}
    </details>
  )
}
