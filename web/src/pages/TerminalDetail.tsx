import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiPost, apiPostBareOk } from '../api/client'
import { EmptyHint, ErrorBanner, LoadingBlock } from '../components/AppStates'
import { StatusBadge, terminalStatusLabel, terminalStatusVariant } from '../components/StatusBadge'

type DiagnosticsPayload = {
  hardwareValidationNotice?: string
  snapshots: { id: string; source: string; snapshotJson: string; createdAtUtc: string }[]
  craftDiagnostics: { id: string; category: string; payloadJson: string; recordedAtUtc: string; craftSessionId: string | null }[]
  tableReloadRequests: {
    id: string
    status: number
    simulationMode: boolean
    detailJson: string
    craftSessionId: string | null
    createdAtUtc: string
  }[]
  cdrUploadRequests: {
    id: string
    status: number
    simulationMode: boolean
    detailJson: string
    craftSessionId: string | null
    createdAtUtc: string
  }[]
}

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Term = {
  id: string
  siteId: string
  displayName: string
  terminalIdHex: string
  status: number
}

type Assign = {
  terminalId: string
  tableSetId: string
  previousTableSetId: string | null
  siteId: string | null
  customerId: string | null
  assignedAtUtc: string
}

type SetRow = { id: string; name: string; status: number }

type TimelineResp = {
  terminalId: string
  items: { kind: string; atUtc: string; title: string; refKey: string; detail: Record<string, unknown> }[]
}

export function TerminalDetail() {
  const { id } = useParams()
  const [term, setTerm] = useState<Term | null>(null)
  const [assign, setAssign] = useState<Assign | null | undefined>(undefined)
  const [sets, setSets] = useState<SetRow[]>([])
  const [pick, setPick] = useState('')
  const [err, setErr] = useState<string | null>(null)
  const [diag, setDiag] = useState<DiagnosticsPayload | null>(null)
  const [diagErr, setDiagErr] = useState<string | null>(null)
  const [snapJson, setSnapJson] = useState('{}')
  const [timeline, setTimeline] = useState<TimelineResp | null>(null)
  const [tlErr, setTlErr] = useState<string | null>(null)

  useEffect(() => {
    if (!id) return
    let c = false
    setErr(null)
    ;(async () => {
      try {
        const t = await fetch(`/api/terminals/${id}`, { headers })
        if (!t.ok) throw new Error(String(t.status))
        if (!c) setTerm((await t.json()) as Term)

        const a = await fetch(`/api/terminals/${id}/table-assignment`, { headers })
        if (a.status === 404) {
          if (!c) setAssign(null)
        } else if (a.ok) {
          if (!c) setAssign((await a.json()) as Assign)
        } else if (!c) {
          setErr(`Assignment ${a.status}`)
        }

        const s = await fetch('/api/tables/sets', { headers })
        if (s.ok) {
          const list = (await s.json()) as SetRow[]
          if (!c) {
            setSets(list)
            const pub = list.find((x) => x.status === 1)
            if (pub) setPick(pub.id)
          }
        }

        const dg = await fetch(`/api/terminals/${id}/diagnostics`, { headers })
        if (dg.ok && !c) setDiag((await dg.json()) as DiagnosticsPayload)
        else if (!dg.ok && !c) setDiagErr(`diagnostics ${dg.status}`)

        const tl = await fetch(`/api/operator/terminals/${id}/timeline?take=60`, { headers })
        if (tl.ok && !c) setTimeline((await tl.json()) as TimelineResp)
        else if (!tl.ok && !c) setTlErr(`timeline ${tl.status}`)
      } catch (e) {
        if (!c) setErr(e instanceof Error ? e.message : 'error')
      }
    })()
    return () => {
      c = true
    }
  }, [id])

  async function applyAssignment(e: React.FormEvent) {
    e.preventDefault()
    if (!id || !pick) return
    setErr(null)
    try {
      await apiPost<unknown>('/api/terminals/' + id + '/table-assignment', {
        tableSetId: pick,
        customerId: null,
        siteId: null,
      })
      const a = await fetch(`/api/terminals/${id}/table-assignment`, { headers })
      if (a.ok) setAssign((await a.json()) as Assign)
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function pushSnapshot(e: React.FormEvent) {
    e.preventDefault()
    if (!id) return
    setDiagErr(null)
    try {
      await apiPost(`/api/terminals/${id}/diagnostics/snapshots`, {
        snapshotJson: snapJson,
        source: 'operator_ui',
      })
      const dg = await fetch(`/api/terminals/${id}/diagnostics`, { headers })
      if (dg.ok) setDiag((await dg.json()) as DiagnosticsPayload)
    } catch (ex) {
      setDiagErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function requestCdr() {
    if (!id) return
    setDiagErr(null)
    try {
      await apiPost(`/api/terminals/${id}/request-cdr-upload`, {
        detailJson: '{"intent":"cdr_upload"}',
        simulationMode: true,
      })
      const dg = await fetch(`/api/terminals/${id}/diagnostics`, { headers })
      if (dg.ok) setDiag((await dg.json()) as DiagnosticsPayload)
    } catch (ex) {
      setDiagErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function requestTableReload() {
    if (!id) return
    setDiagErr(null)
    try {
      await apiPost(`/api/terminals/${id}/request-table-reload`, {
        detailJson: '{"intent":"table_reload"}',
        simulationMode: true,
      })
      const dg = await fetch(`/api/terminals/${id}/diagnostics`, { headers })
      if (dg.ok) setDiag((await dg.json()) as DiagnosticsPayload)
    } catch (ex) {
      setDiagErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function rollback() {
    if (!id) return
    if (
      !window.confirm(
        'Rollback terminal table assignment to the previous set? Host routing only — HARDWARE_VALIDATION_REQUIRED on device.'
      )
    )
      return
    setErr(null)
    try {
      await apiPostBareOk(`/api/terminals/${id}/table-assignment/rollback`, { confirm: true })
      const a = await fetch(`/api/terminals/${id}/table-assignment`, { headers })
      if (a.ok) setAssign((await a.json()) as Assign)
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  if (!id) return <p>Missing id.</p>
  if (err && !term) return <p style={{ color: 'crimson' }}>{err}</p>
  if (!term) return <p>Loading…</p>

  return (
    <div>
      <p>
        <Link to="/terminals">← Terminals</Link>
      </p>
      <h1>{term.displayName}</h1>
      <p>
        Id: <code>{term.id}</code>
      </p>
      <p style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        Status:{' '}
        <StatusBadge
          label={terminalStatusLabel(term.status)}
          variant={terminalStatusVariant(term.status)}
          title={`Operational status enum value ${term.status}`}
        />
      </p>
      <p>Terminal id (hex): {term.terminalIdHex}</p>
      {err && <p style={{ color: 'crimson' }}>{err}</p>}

      <h2>Table assignment</h2>
      {assign === undefined && <p>Loading assignment…</p>}
      {assign === null && <p>No assignment yet.</p>}
      {assign && (
        <div style={{ background: '#f6f8fa', padding: 12, marginBottom: 12 }}>
          <div>
            Active set: <code>{assign.tableSetId}</code>
          </div>
          {assign.previousTableSetId && (
            <div>
              Previous set: <code>{assign.previousTableSetId}</code>
            </div>
          )}
          <div>Assigned: {new Date(assign.assignedAtUtc).toLocaleString()}</div>
        </div>
      )}
      <form onSubmit={applyAssignment} style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
        <label>
          Table set
          <select value={pick} onChange={(e) => setPick(e.target.value)} style={{ display: 'block', minWidth: 220 }}>
            {sets.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name} {s.status === 1 ? '(published)' : '(draft)'}
              </option>
            ))}
          </select>
        </label>
        <button type="submit">Assign set</button>
        <button type="button" onClick={() => void rollback()}>
          Rollback to previous set
        </button>
      </form>

      <h2 style={{ marginTop: 24 }}>Field diagnostics</h2>
      <p style={{ color: '#555' }}>
        Snapshots and host intents are persisted for technician workflows; modem/NCC execution stays simulation-first until
        certified.
      </p>
      {diag?.hardwareValidationNotice && (
        <p style={{ background: '#fff9e6', border: '1px solid #e6d69a', padding: 10, maxWidth: 720 }} role="note">
          {diag.hardwareValidationNotice}
        </p>
      )}
      {diagErr && <p role="alert" style={{ color: 'crimson' }}>{diagErr}</p>}
      <form onSubmit={(e) => void pushSnapshot(e)} style={{ marginBottom: 12 }}>
        <label>
          Snapshot JSON
          <textarea
            value={snapJson}
            onChange={(e) => setSnapJson(e.target.value)}
            rows={3}
            style={{ display: 'block', width: '100%', maxWidth: 560, fontFamily: 'monospace' }}
          />
        </label>
        <button type="submit" style={{ display: 'block', marginTop: 8 }}>
          Save diagnostic snapshot
        </button>
      </form>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 16 }}>
        <button type="button" onClick={() => void requestCdr()}>
          Request CDR upload (intent)
        </button>
        <button type="button" onClick={() => void requestTableReload()}>
          Request table reload (intent)
        </button>
      </div>
      {diag && (
        <div style={{ background: '#f6f8fa', padding: 12 }}>
          <div>
            <strong>Snapshots</strong> ({diag.snapshots.length})
          </div>
          <ul>
            {diag.snapshots.length === 0 && (
              <li>
                <span role="status">None</span>
              </li>
            )}
            {diag.snapshots.slice(0, 5).map((s) => (
              <li key={s.id}>
                {s.source} · {new Date(s.createdAtUtc).toLocaleString()}
                <pre style={{ fontSize: 11 }}>{s.snapshotJson.slice(0, 200)}</pre>
              </li>
            ))}
          </ul>
          <div>
            <strong>CDR requests</strong> ({diag.cdrUploadRequests.length})
          </div>
          <ul>
            {diag.cdrUploadRequests.slice(0, 5).map((r) => (
              <li key={r.id}>
                {r.status} · sim {String(r.simulationMode)} · {new Date(r.createdAtUtc).toLocaleString()}
              </li>
            ))}
          </ul>
          <div>
            <strong>Table reload requests</strong> ({diag.tableReloadRequests.length})
          </div>
          <ul>
            {diag.tableReloadRequests.slice(0, 5).map((r) => (
              <li key={r.id}>
                {r.status} · sim {String(r.simulationMode)} · {new Date(r.createdAtUtc).toLocaleString()}
              </li>
            ))}
          </ul>
        </div>
      )}

      <h2 style={{ marginTop: 24 }}>Operator timeline</h2>
      <p style={{ color: '#555', fontSize: 14 }}>
        Merged host-side signals — use links below for DLOG / downloads / firmware detail pages.
      </p>
      {tlErr && <ErrorBanner message={tlErr} />}
      {!timeline && !tlErr && <LoadingBlock label="Loading timeline…" />}
      {timeline && timeline.items.length === 0 && !tlErr && (
        <EmptyHint>No timeline rows yet (events, DLOG, batches, craft, firmware, audit).</EmptyHint>
      )}
      {timeline && timeline.items.length > 0 && (
        <table cellPadding={6} style={{ borderCollapse: 'collapse', fontSize: 13, maxWidth: 960 }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>When</th>
              <th>Kind</th>
              <th>Summary</th>
              <th>Open</th>
            </tr>
          </thead>
          <tbody>
            {timeline.items.map((it: TimelineResp['items'][number], idx: number) => (
              <tr key={`${it.kind}-${it.refKey}-${idx}`} style={{ borderBottom: '1px solid #eee', verticalAlign: 'top' }}>
                <td>{new Date(it.atUtc).toLocaleString()}</td>
                <td>{it.kind}</td>
                <td>{it.title}</td>
                <td>
                  {it.kind === 'dlog' ? (
                    <Link to={`/dlog/${it.refKey}`}>DLOG</Link>
                  ) : it.kind === 'downloadBatch' ? (
                    <Link to={`/downloads/${it.refKey}`}>Batch</Link>
                  ) : it.kind === 'firmwareJob' ? (
                    <Link to={`/firmware/jobs/${it.refKey}`}>Job</Link>
                  ) : it.kind === 'craftSession' ? (
                    <Link to={`/craft/${it.refKey}`}>Craft</Link>
                  ) : (
                    '—'
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h2 style={{ marginTop: 24 }}>Downloads</h2>
      <p>
        <Link to={`/downloads?terminal=${encodeURIComponent(id)}`}>Open downloads for this terminal</Link>
      </p>
      <p>
        <Link to={`/dlog?terminal=${encodeURIComponent(id)}`}>DLOG transactions (filter)</Link>
      </p>
    </div>
  )
}
