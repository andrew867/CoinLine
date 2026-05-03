import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ErrorBanner, LoadingBlock } from '../components/AppStates'
import { JsonPanel } from '../components/JsonPanel'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Dash = {
  terminalsByStatus: { status: number; count: number }[]
  activeCraftSessions: number
  openNccSessions: number
  failedDownloads: number
  uploadAlerts: { failed: number; quarantined: number }
  recentUploadBatches: {
    id: string
    terminalId: string | null
    status: number
    createdAtUtc: string
    updatedAtUtc: string
    previewBytes: number
  }[]
  recentFirmwareJobs: {
    id: string
    terminalId: string
    status: number
    simulationMode: boolean
    createdAtUtc: string
    firmwarePackageId: string
  }[]
  recentAuditEvents: {
    id: string
    category: string
    action: string
    actor: string
    resource: string
    terminalId: string | null
    createdAtUtc: string
  }[]
  hardwareValidationNotice?: string
}

export function Dashboard() {
  const [dash, setDash] = useState<Dash | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    let c = false
    fetch('/api/operator/dashboard', { headers })
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Dash>
      })
      .then((j) => {
        if (!c) setDash(j)
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
      <h1>CoinLine — Dashboard</h1>
      <p style={{ color: '#555' }}>
        Operator snapshot for CoinLine Server — links jump into terminal, download, firmware, DLOG, or audit views.
      </p>
      {dash?.hardwareValidationNotice && (
        <p role="note" style={{ background: '#fff9e6', border: '1px solid #e6d69a', padding: 10, maxWidth: 900 }}>
          {dash.hardwareValidationNotice}
        </p>
      )}
      {err && <ErrorBanner message={err} />}
      {!dash && !err && <LoadingBlock label="Loading operator dashboard…" />}
      {dash && (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: 16 }}>
          <section style={{ border: '1px solid #ddd', borderRadius: 6, padding: 12 }}>
            <h2 style={{ marginTop: 0, fontSize: 16 }}>Terminals by status</h2>
            <ul style={{ margin: 0, paddingLeft: 18 }}>
              {dash.terminalsByStatus.map((x) => (
                <li key={x.status}>
                  Status {x.status}: <strong>{x.count}</strong>{' '}
                  <Link to="/terminals" style={{ fontSize: 13 }}>
                    open list
                  </Link>
                </li>
              ))}
            </ul>
          </section>
          <section style={{ border: '1px solid #ddd', borderRadius: 6, padding: 12 }}>
            <h2 style={{ marginTop: 0, fontSize: 16 }}>Sessions & transport</h2>
            <p style={{ margin: '4px 0' }}>
              Active craft sessions: <strong>{dash.activeCraftSessions}</strong> ·{' '}
              <Link to="/craft">Craft</Link>
            </p>
            <p style={{ margin: '4px 0' }}>
              Open NCC sessions: <strong>{dash.openNccSessions}</strong> ·{' '}
              <Link to="/ncc-sessions">NCC</Link>
            </p>
          </section>
          <section style={{ border: '1px solid #ddd', borderRadius: 6, padding: 12 }}>
            <h2 style={{ marginTop: 0, fontSize: 16 }}>Downloads & uploads</h2>
            <p style={{ margin: '4px 0' }}>
              Failed download batches:{' '}
              <strong style={{ color: dash.failedDownloads ? '#a30' : undefined }}>{dash.failedDownloads}</strong> ·{' '}
              <Link to="/downloads">Downloads</Link>
            </p>
            <p style={{ margin: '4px 0', fontSize: 13 }}>
              Upload alerts — failed {dash.uploadAlerts.failed}, quarantined {dash.uploadAlerts.quarantined} ·{' '}
              <Link to="/uploads">Uploads</Link>
            </p>
          </section>
        </div>
      )}
      {dash && (
        <>
          <h2 style={{ marginTop: 24 }}>Recent firmware jobs</h2>
          {dash.recentFirmwareJobs.length === 0 ? (
            <p style={{ color: '#666' }}>None.</p>
          ) : (
            <table cellPadding={8} style={{ borderCollapse: 'collapse', fontSize: 14 }}>
              <thead>
                <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
                  <th>Job</th>
                  <th>Terminal</th>
                  <th>Status</th>
                  <th>Sim</th>
                </tr>
              </thead>
              <tbody>
                {dash.recentFirmwareJobs.map((j) => (
                  <tr key={j.id} style={{ borderBottom: '1px solid #eee' }}>
                    <td>
                      <Link to={`/firmware/jobs/${j.id}`}>{j.id.slice(0, 8)}…</Link>
                    </td>
                    <td>
                      <Link to={`/terminals/${j.terminalId}`}>{j.terminalId.slice(0, 8)}…</Link>
                    </td>
                    <td>{j.status}</td>
                    <td>{j.simulationMode ? 'yes' : 'no'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <h2>Recent uploads</h2>
          {dash.recentUploadBatches.length === 0 ? (
            <p style={{ color: '#666' }}>None.</p>
          ) : (
            <ul>
              {dash.recentUploadBatches.map((u) => (
                <li key={u.id}>
                  {u.terminalId ? (
                    <Link to={`/terminals/${u.terminalId}`}>Terminal</Link>
                  ) : (
                    '—'
                  )}{' '}
                  · status {u.status} · {u.previewBytes} bytes · {new Date(u.createdAtUtc).toLocaleString()}
                </li>
              ))}
            </ul>
          )}
          <h2>Audit (recent)</h2>
          <p style={{ fontSize: 13, color: '#555' }}>
            Operator actions across categories — full search on <Link to="/audit">Audit events</Link>.
          </p>
          {dash.recentAuditEvents.length === 0 ? (
            <p style={{ color: '#666' }}>None.</p>
          ) : (
            <table cellPadding={8} style={{ borderCollapse: 'collapse', fontSize: 13 }}>
              <thead>
                <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
                  <th>When</th>
                  <th>Category / action</th>
                  <th>Actor</th>
                  <th>Terminal</th>
                </tr>
              </thead>
              <tbody>
                {dash.recentAuditEvents.map((a) => (
                  <tr key={a.id} style={{ borderBottom: '1px solid #eee' }}>
                    <td>{new Date(a.createdAtUtc).toLocaleString()}</td>
                    <td>
                      {a.category}/{a.action}
                    </td>
                    <td>{a.actor}</td>
                    <td>
                      {a.terminalId ? <Link to={`/terminals/${a.terminalId}`}>open</Link> : '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
      <h2 style={{ marginTop: 24 }}>Health</h2>
      <JsonPanel title="Health" url="/health" />
    </div>
  )
}
