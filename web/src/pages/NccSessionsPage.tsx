import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPostBareOk } from '../api/client'

type NccSessionRow = {
  id: string
  terminalId: string | null
  correlationId: string
  status: number
  startedAtUtc: string
  endedAtUtc: string | null
  lastFrameSampleHex: string | null
}

function formatUtc(iso: string) {
  try {
    const d = new Date(iso)
    return Number.isNaN(d.getTime()) ? iso : d.toISOString().replace('T', ' ').replace(/\.\d{3}Z$/, ' Z')
  } catch {
    return iso
  }
}

function truncateHex(hex: string | null, max = 24) {
  if (hex == null || hex.length === 0) return '—'
  const u = hex.toUpperCase()
  return u.length <= max ? u : `${u.slice(0, max)}…`
}

function statusLabel(code: number): string {
  switch (code) {
    case 0:
      return 'Active'
    case 1:
      return 'Closed'
    case 2:
      return 'Archived'
    default:
      return String(code)
  }
}

export function NccSessionsPage() {
  const [rows, setRows] = useState<NccSessionRow[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [includeArchived, setIncludeArchived] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setErr(null)
    setLoading(true)
    try {
      const q = includeArchived ? '?includeArchived=true' : ''
      const data = await apiGet<NccSessionRow[]>(`/api/ncc/sessions${q}`)
      setRows(data)
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'error')
    } finally {
      setLoading(false)
    }
  }, [includeArchived])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const openCount = useMemo(() => rows.filter((r) => r.status === 0).length, [rows])

  async function closeSession(id: string) {
    setBusyId(id)
    setErr(null)
    try {
      await apiPostBareOk(`/api/ncc/sessions/${id}/close`)
      await refresh()
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'error')
    } finally {
      setBusyId(null)
    }
  }

  async function archiveSession(id: string) {
    setBusyId(id)
    setErr(null)
    try {
      await apiPostBareOk(`/api/ncc/sessions/${id}/archive`)
      await refresh()
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'error')
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div>
      <h1>NCC sessions</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Modem / NCC session records for DLOG correlation and captures. Status follows host persistence:{' '}
        <strong>Active</strong> sessions count toward the operator dashboard; <strong>Closed</strong> ended normally;{' '}
        <strong>Archived</strong> rows stay in history but are hidden from this list unless included below.
      </p>

      <label style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 12 }}>
        <input
          type="checkbox"
          checked={includeArchived}
          onChange={(e) => setIncludeArchived(e.target.checked)}
          data-testid="ncc-sessions-include-archived"
        />
        Include archived sessions
      </label>

      {loading && <p role="status">Loading…</p>}
      {!loading && err && (
        <p role="alert" style={{ color: 'crimson' }} data-testid="ncc-sessions-error">
          {err}
        </p>
      )}
      {!loading && !err && (
        <p style={{ fontSize: 14 }} data-testid="ncc-sessions-summary">
          {`${rows.length} session(s), ${openCount} active`}
        </p>
      )}

      {!loading && !err && rows.length === 0 && <p role="status">No NCC sessions in this view.</p>}

      {!loading && !err && rows.length > 0 && (
        <table data-testid="ncc-sessions-table" cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ borderBottom: '1px solid #ccc' }}>
              <th align="left">Status</th>
              <th align="left">Started (UTC)</th>
              <th align="left">Ended (UTC)</th>
              <th align="left">Correlation</th>
              <th align="left">Terminal</th>
              <th align="left">Last frame sample</th>
              <th align="left">Actions</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>{statusLabel(r.status)}</td>
                <td>{formatUtc(r.startedAtUtc)}</td>
                <td>{r.endedAtUtc ? formatUtc(r.endedAtUtc) : <em>—</em>}</td>
                <td>
                  <code style={{ fontSize: 12 }}>{r.correlationId || '—'}</code>
                </td>
                <td>
                  {r.terminalId ? (
                    <Link to={`/terminals/${r.terminalId}`} data-testid="ncc-session-terminal-link">
                      {r.terminalId}
                    </Link>
                  ) : (
                    '—'
                  )}
                </td>
                <td>
                  <code style={{ fontSize: 11 }} title={r.lastFrameSampleHex ?? undefined}>
                    {truncateHex(r.lastFrameSampleHex)}
                  </code>
                </td>
                <td style={{ whiteSpace: 'nowrap' }}>
                  {r.status === 0 && (
                    <button
                      type="button"
                      disabled={busyId === r.id}
                      onClick={() => void closeSession(r.id)}
                      data-testid={`ncc-session-close-${r.id}`}
                    >
                      Close
                    </button>
                  )}{' '}
                  {r.status !== 2 && (
                    <button
                      type="button"
                      disabled={busyId === r.id}
                      onClick={() => void archiveSession(r.id)}
                      data-testid={`ncc-session-archive-${r.id}`}
                    >
                      Archive
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
