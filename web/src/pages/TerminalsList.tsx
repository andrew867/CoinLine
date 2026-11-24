import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { terminalStatusLabel } from '../components/StatusBadge'
import { apiFetch } from '../api/client'


type Row = {
  id: string
  siteId: string
  displayName: string
  terminalIdHex: string
  status: number
  groupName: string | null
  onCall: boolean
  lastSessionAtUtc: string | null
}

export function TerminalsList() {
  const [rows, setRows] = useState<Row[] | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    let c = false
    apiFetch('/api/terminals')
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Row[]>
      })
      .then((j) => {
        if (!c) setRows(j)
      })
      .catch((e) => {
        if (!c) setErr(e instanceof Error ? e.message : 'error')
      })
    return () => {
      c = true
    }
  }, [])

  return (
    <div>
      <h1>Phones</h1>
      <p>
        <Link to="/terminals/new" data-testid="terminals-add-link">
          Add a phone
        </Link>
      </p>
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!rows && <p>Loading…</p>}
      {rows && rows.length === 0 && <p style={{ color: '#666' }}>No terminals yet.</p>}
      {rows && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>Name</th>
              <th>Hex id</th>
              <th>Group</th>
              <th>Status</th>
              <th>Last call</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/terminals/${r.id}`}>{r.displayName}</Link>
                </td>
                <td>
                  <code>{r.terminalIdHex}</code>
                </td>
                <td>{r.groupName ?? '—'}</td>
                <td>{terminalStatusLabel(r.status)}</td>
                <td>{r.onCall ? 'On a call' : r.lastSessionAtUtc ? new Date(r.lastSessionAtUtc).toLocaleString() : '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
