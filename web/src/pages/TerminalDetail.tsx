import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiGet, apiPost, apiPostBareOk, apiPut } from '../api/client'
import { FirmwareVersionSelect } from '../components/FirmwareVersionSelect'
import { TerminalFamilySelect } from '../components/TerminalFamilySelect'
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

function sessionWords(status: number): string {
  return ({ 0: 'Active', 1: 'Closed', 2: 'Archived' } as Record<number, string>)[status] ?? 'Unknown'
}

function dispositionWords(status: number): string {
  return (
    ({
      0: 'Unknown',
      1: 'Completed',
      2: 'Blocked',
      3: 'Free call',
      4: 'Emergency',
      5: 'Failed',
    } as Record<number, string>)[status] ?? 'Unknown'
  )
}

function batchWords(status: number): string {
  return (
    ({
      0: 'Pending',
      1: 'Running',
      2: 'Completed',
      3: 'Failed',
      4: 'Cancelled',
      5: 'Preparing',
    } as Record<number, string>)[status] ?? 'Unknown'
  )
}

function phaseWords(name: string): string {
  const known: Record<string, string> = {
    Queued: 'Queued',
    WaitingForAck: 'Waiting for acknowledgement',
    SendingHeader: 'Sending header',
    SendingPayload: 'Sending payload',
    Completed: 'Completed',
    Failed: 'Failed',
    Retrying: 'Retrying',
    Cancelled: 'Cancelled',
  }
  return known[name] ?? name
}

type Term = {
  id: string
  siteId: string
  displayName: string
  terminalIdHex: string
  status: number
  firmwareVersionId: string | null
  terminalFamilyId: string | null
}

type Assign = {
  terminalId: string
  tableSetId: string
  previousTableSetId: string | null
  siteId: string | null
  customerId: string | null
  assignedAtUtc: string
}

type SetRow = { id: string; name: string; status: number; targetFirmwareVersionId: string | null; targetFirmware: string | null }

type SessionRow = {
  id: string
  terminalId: string | null
  correlationId: string
  status: number
  startedAtUtc: string
  endedAtUtc: string | null
}

type CallRow = {
  id: string
  terminalId: string | null
  dialedDigits: string
  disposition: number
  startedAtUtc: string
}

type BatchView = {
  id: string
  status: number
  lastError: string | null
  phases: string[]
}

type TimelineResp = {
  terminalId: string
  items: { kind: string; atUtc: string; title: string; refKey: string; detail: Record<string, unknown> }[]
}

type GroupMembership = {
  groupIds: string[]
  revision: number
  limit: number
  available: { id: string; name: string }[]
}

function PhoneGroups({ terminalId }: { terminalId: string }) {
  const [groups, setGroups] = useState<GroupMembership | null>(null)
  const [selected, setSelected] = useState<string[]>([])
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState('')
  const [error, setError] = useState('')
  const endpoint = `/api/terminals/${terminalId}/groups`

  useEffect(() => {
    let current = true
    void apiGet<GroupMembership>(endpoint).then((data) => {
      if (current) {
        setGroups(data)
        setSelected(data.groupIds)
      }
    }).catch(() => { if (current) setError('Could not load phone groups.') })
    return () => { current = false }
  }, [endpoint])

  async function save() {
    if (!groups) return
    setBusy(true)
    setNotice('')
    setError('')
    try {
      const response = await fetch(endpoint, {
        method: 'PUT', headers: { ...headers, 'Content-Type': 'application/json' },
        body: JSON.stringify({ groupIds: selected, revision: groups.revision }),
      })
      if (!response.ok)
        throw new Error((await response.json().catch(() => null))?.error ?? 'Could not save phone groups.')
      const saved = await response.json() as Pick<GroupMembership, 'groupIds' | 'revision'>
      setGroups({ ...groups, ...saved })
      setSelected(saved.groupIds)
      setNotice('Phone groups saved.')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save phone groups.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <details>
      <summary>Phone groups{groups ? ` (${groups.groupIds.length})` : ''}</summary>
      {error && <ErrorBanner message={error} />}
      {notice && <p role="status">{notice}</p>}
      {groups && groups.available.length > 0 && (
        <form onSubmit={(event) => { event.preventDefault(); void save() }}>
          <fieldset disabled={busy}>
            <legend>Participating groups</legend>
            {groups.available.map((group) => (
              <label key={group.id} style={{ display: 'block' }}>
                <input type="checkbox" checked={selected.includes(group.id)}
                  disabled={selected.length >= groups.limit && !selected.includes(group.id)}
                  onChange={(event) => {
                    setSelected(event.target.checked ? [...selected, group.id] : selected.filter((id) => id !== group.id))
                    setNotice('')
                  }} /> {group.name}
              </label>
            ))}
            {selected.length > 1 && (
              <label>Primary group
                <select value={selected[0]} onChange={(event) => {
                  const group = event.target.value
                  setSelected([group, ...selected.filter((id) => id !== group)])
                  setNotice('')
                }}>
                  {groups.available.filter((group) => selected.includes(group.id)).map((group) => (
                    <option key={group.id} value={group.id}>{group.name}</option>
                  ))}
                </select>
              </label>
            )}
          </fieldset>
          <button type="submit" disabled={busy || selected.join(',') === groups.groupIds.join(',')}>Save phone groups</button>
        </form>
      )}
      {groups?.available.length === 0 && <p>No matching groups. <Link to="/terminal-groups">Manage phone groups</Link>.</p>}
    </details>
  )
}

export function TerminalDetail() {
  const { id } = useParams()
  const [term, setTerm] = useState<Term | null>(null)
  const [firmwareEdit, setFirmwareEdit] = useState<string | null>(null)
  const [familyEdit, setFamilyEdit] = useState<string | null>(null)
  const [savingFamily, setSavingFamily] = useState(false)
  const [familyNotice, setFamilyNotice] = useState('')
  const [savingFirmware, setSavingFirmware] = useState(false)
  const [firmwareNotice, setFirmwareNotice] = useState('')
  const [assign, setAssign] = useState<Assign | null | undefined>(undefined)
  const [sets, setSets] = useState<SetRow[]>([])
  const [pick, setPick] = useState('')
  const [err, setErr] = useState<string | null>(null)
  const [diag, setDiag] = useState<DiagnosticsPayload | null>(null)
  const [diagErr, setDiagErr] = useState<string | null>(null)
  const [snapJson, setSnapJson] = useState('{}')
  const [timeline, setTimeline] = useState<TimelineResp | null>(null)
  const [tlErr, setTlErr] = useState<string | null>(null)
  const [sessions, setSessions] = useState<SessionRow[] | null>(null)
  const [lastCall, setLastCall] = useState<CallRow | null>(null)
  const [batches, setBatches] = useState<BatchView[]>([])

  useEffect(() => {
    if (!id) return
    let c = false
    setErr(null)
    setFirmwareEdit(null)
    setFirmwareNotice('')
    ;(async () => {
      try {
        const t = await fetch(`/api/terminals/${id}`, { headers })
        if (!t.ok) throw new Error(String(t.status))
        const terminal = await t.json() as Term
        if (!c) setTerm(terminal)

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
            const pub = list.find((x) => x.status === 1 &&
              (!x.targetFirmwareVersionId || x.targetFirmwareVersionId === terminal.firmwareVersionId))
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

  useEffect(() => {
    if (!id) return
    let cancel = false
    ;(async () => {
      const sessionsResp = await fetch('/api/ncc/sessions?includeArchived=true', { headers })
      const callsResp = await fetch('/api/call-records', { headers })
      const batchesResp = await fetch('/api/downloads', { headers })
      if (cancel) return
      const sessionRows = sessionsResp.ok ? ((await sessionsResp.json()) as SessionRow[]) : []
      const mine = sessionRows.filter((s) => s.terminalId === id)
      setSessions(mine)
      const callRows = callsResp.ok ? ((await callsResp.json()) as CallRow[]) : []
      setLastCall(callRows.find((c) => c.terminalId === id) ?? null)
      const batchRows = batchesResp.ok
        ? ((await batchesResp.json()) as { id: string; terminalId: string; status: number; lastError: string | null }[])
        : []
      const views: BatchView[] = []
      for (const b of batchRows.filter((x) => x.terminalId === id).slice(0, 5)) {
        const detail = await fetch('/api/downloads/' + b.id, { headers })
        if (!detail.ok) {
          views.push({ id: b.id, status: b.status, lastError: b.lastError, phases: [] })
          continue
        }
        const body = (await detail.json()) as {
          timeline?: { hostDownloadPhaseName?: string }[]
        }
        views.push({
          id: b.id,
          status: b.status,
          lastError: b.lastError,
          phases: (body.timeline ?? []).map((t) => phaseWords(t.hostDownloadPhaseName ?? '')),
        })
      }
      if (!cancel) setBatches(views)
    })().catch(() => {
      if (!cancel) setSessions([])
    })
    return () => {
      cancel = true
    }
  }, [id])

  if (!id) return <p>Missing id.</p>
  if (err && !term) return <p style={{ color: 'crimson' }}>{err}</p>
  if (!term) return <p>Loading…</p>
  const firmwareSelection = firmwareEdit ?? term.firmwareVersionId ?? ''
  const familySelection = familyEdit ?? term.terminalFamilyId ?? ''
  const pickedSet = sets.find((set) => set.id === pick)
  const assignedSet = sets.find((set) => set.id === assign?.tableSetId)

  async function saveInstalledFirmware(event: React.FormEvent) {
    event.preventDefault()
    if (!term || firmwareEdit == null) return
    setSavingFirmware(true)
    setFirmwareNotice('')
    try {
      await apiPut(`/api/terminals/${term.id}/firmware`, { firmwareVersionId: firmwareEdit || null })
      setTerm((current) => current?.id === term.id ? { ...current, firmwareVersionId: firmwareEdit || null } : current)
      setFirmwareEdit(null)
      setFirmwareNotice('Installed firmware saved.')
    } catch (error) {
      setErr(error instanceof Error ? error.message : 'Could not save installed firmware.')
    } finally {
      setSavingFirmware(false)
    }
  }

  async function saveTerminalFamily(event: React.FormEvent) {
    event.preventDefault()
    if (!term || familyEdit == null) return
    setSavingFamily(true)
    setFamilyNotice('')
    try {
      await apiPut(`/api/terminals/${term.id}/family`, { terminalFamilyId: familyEdit || null })
      setTerm((current) => current?.id === term.id ? { ...current, terminalFamilyId: familyEdit || null } : current)
      setFamilyEdit(null)
      setFamilyNotice('Terminal family saved.')
    } catch (error) {
      setErr(error instanceof Error ? error.message : 'Could not save terminal family.')
    } finally {
      setSavingFamily(false)
    }
  }

  return (
    <div>
      <p>
        <Link to="/terminals">← Phones</Link> · <Link to={`/field-events?terminalId=${term.id}`}>Terminal status</Link>
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
      <form onSubmit={(event) => void saveInstalledFirmware(event)}
        style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'end', marginBottom: 12 }}>
        <FirmwareVersionSelect label="Installed firmware" value={firmwareSelection} onChange={setFirmwareEdit}
          emptyLabel="Unknown firmware" disabled={savingFirmware} />
        <button type="submit" disabled={savingFirmware || firmwareSelection === (term.firmwareVersionId ?? '')}>Save firmware</button>
        <Link to="/firmware/versions">Manage versions</Link>
      </form>
      {firmwareNotice && <p role="status">{firmwareNotice}</p>}
      <form onSubmit={(event) => void saveTerminalFamily(event)}
        style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'end', marginBottom: 12 }}>
        <TerminalFamilySelect value={familySelection} onChange={setFamilyEdit} disabled={savingFamily} />
        <button type="submit" disabled={savingFamily || familySelection === (term.terminalFamilyId ?? '')}>Save family</button>
      </form>
      {familyNotice && <p role="status">{familyNotice}</p>}
      <PhoneGroups key={term.id} terminalId={term.id} />

      <h2>Phone session</h2>
      <div data-testid="ncc-sessions">
        {sessions === null && <p>Loading sessions…</p>}
        {sessions && sessions.length === 0 && <p>No session yet.</p>}
        {sessions && sessions.length > 0 && (
          <ul>
            {sessions.slice(0, 5).map((s) => (
              <li key={s.id}>
                {sessionWords(s.status)} · {s.correlationId} · {new Date(s.startedAtUtc).toLocaleString()}
              </li>
            ))}
          </ul>
        )}
      </div>

      <h2>Last rate</h2>
      <div data-testid="last-rate">
        {lastCall == null && <p>No rated call yet.</p>}
        {lastCall && (
          <p>
            {lastCall.dialedDigits || 'empty number'} · {dispositionWords(lastCall.disposition)} ·{' '}
            {new Date(lastCall.startedAtUtc).toLocaleString()}
          </p>
        )}
      </div>

      <h2>Downloads</h2>
      <div data-testid="download-status">
        {batches.length === 0 && <p>No download batch yet.</p>}
        {batches.map((b) => (
          <p key={b.id}>
            <Link to={`/downloads/${b.id}`}>{batchWords(b.status)}</Link>
            {b.phases.length > 0 ? ` · ${b.phases.join(', ')}` : ''}
            {b.lastError ? ` · ${b.lastError}` : ''}
          </p>
        ))}
      </div>
      {err && <p style={{ color: 'crimson' }}>{err}</p>}

      <h2>Table assignment</h2>
      {assign === undefined && <p>Loading assignment…</p>}
      {assign === null && <p>No assignment yet.</p>}
      {assign && (
        <div style={{ background: '#f6f8fa', padding: 12, marginBottom: 12 }}>
          <div>
            Active set <Link to={`/table-sets/${assign.tableSetId}`}>{assignedSet?.name ?? assign.tableSetId}</Link>
          </div>
          <div><Link to={`/table-sets/${assign.tableSetId}?record=35#config-heading`}>Review routine call-in settings</Link></div>
          {assignedSet?.targetFirmwareVersionId && assignedSet.targetFirmwareVersionId !== term.firmwareVersionId &&
            <p role="status">This set requires {assignedSet.targetFirmware}. Register matching firmware or assign a compatible set.</p>}
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
            <option value="">Choose a table set</option>
            {sets.map((s) => {
              const mismatch = !!s.targetFirmwareVersionId && s.targetFirmwareVersionId !== term.firmwareVersionId
              return <option key={s.id} value={s.id} disabled={mismatch}>
                {s.name} {mismatch ? `(requires ${s.targetFirmware})` : s.status === 1 ? '(published)' : '(draft)'}
              </option>
            })}
          </select>
        </label>
        <button type="submit" disabled={!pickedSet || (!!pickedSet.targetFirmwareVersionId && pickedSet.targetFirmwareVersionId !== term.firmwareVersionId)}>Assign set</button>
        <button type="button" onClick={() => void rollback()}>
          Rollback to previous set
        </button>
      </form>

      <h2 style={{ marginTop: 24 }}>Field diagnostics</h2>
      <p style={{ color: '#555' }}>
        Snapshots, CDR-upload intents, and table-reload intents are stored here. They are not sent on the NCC socket.
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
        <Link to={`/dlog?terminal=${encodeURIComponent(id)}`}>Messages from this phone</Link>
      </p>
    </div>
  )
}
