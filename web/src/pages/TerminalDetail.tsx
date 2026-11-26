import { SpecialCallback } from './SpecialCallback'
import { RoutineCallInPanel } from './RoutineCallInPanel'
import { TerminalInventoryPanel } from './TerminalInventoryPanel'
import { PhoneDownloadReview, downloadSchedulePath } from '../components/DownloadReviewReminder'
import { rememberDownloadReview } from '../api/downloadReviews'
import { TerminalIdentityPanel } from './TerminalIdentityPanel'
import { TerminalCashboxPanel } from './TerminalCashboxPanel'
import { InmateAssignmentPanel } from './InmateAssignmentPanel'
import { TerminalLifecyclePanel } from './TerminalLifecyclePanel'
import { TerminalConfigurationWorksheet } from './TerminalConfigurationWorksheet'
import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiFetch, apiGet, apiPost, apiPostBare, apiPostRaw, apiPut } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
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
  deactivatedAtUtc?: string | null
  deletionReviewDate?: string | null
}

type Assign = {
  terminalId: string
  tableSetId: string
  previousTableSetId: string | null
  siteId: string | null
  customerId: string | null
  assignedAtUtc: string
  tableSetName?: string
  targetFirmwareVersionId?: string | null
  targetFirmware?: string | null
}

type SetRow = { id: string; name: string; status: number; targetFirmwareVersionId: string | null; targetFirmware: string | null; compatibilityIssue?: string | null }

function assignmentIssue(set: SetRow, firmwareId: string | null) {
  return set.targetFirmwareVersionId && set.targetFirmwareVersionId !== firmwareId
    ? `Requires ${set.targetFirmware ?? 'matching firmware'}`
    : set.compatibilityIssue ?? null
}

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
type GroupSourcePhone = { id: string; displayName: string; terminalIdHex: string }

function PhoneGroups({ terminalId }: { terminalId: string }) {
  const canAdd = canPerform('memberships', op.add)
  const canDelete = canPerform('memberships', op.delete)
  const [groups, setGroups] = useState<GroupMembership | null>(null)
  const [selected, setSelected] = useState<string[]>([])
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState('')
  const [error, setError] = useState('')
  const [lookup, setLookup] = useState('')
  const [matches, setMatches] = useState<GroupSourcePhone[] | null>(null)
  const [source, setSource] = useState('')
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
    if (!groups || (!canAdd && !canDelete)) return
    setBusy(true)
    setNotice('')
    setError('')
    try {
      const response = await apiFetch(endpoint, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' },
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

  async function findSource() {
    setBusy(true)
    setError('')
    setNotice('')
    setMatches(null)
    setSource('')
    try {
      const search = new URLSearchParams({ q: lookup.trim(), pageSize: '50', sortDir: 'asc' })
      const { items } = await apiGet<{ items: GroupSourcePhone[] }>(`/api/terminals?${search}`)
      setMatches(items.filter(({ id }) => id !== terminalId))
    } catch {
      setError('Could not look up the source phone.')
    } finally {
      setBusy(false)
    }
  }

  async function copyGroups() {
    if (!groups || !source || !canAdd) return
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const { groupIds } = await apiGet<GroupMembership>(`/api/terminals/${source}/groups`)
      const available = new Set(groups.available.map(group => group.id))
      const missing = groupIds.filter(id => !selected.includes(id))
      const additions = missing.filter(id => available.has(id))
      setSelected([...selected, ...additions])
      setNotice(`${additions.length} groups added to the selection. ${missing.length - additions.length} unavailable for this phone.`)
    } catch {
      setError('Could not copy groups from the selected phone.')
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
        <>
        {canAdd && <form className="row-actions" onSubmit={(event) => { event.preventDefault(); void findSource() }}>
          <label>Find a phone to copy groups from
            <input type="search" value={lookup} disabled={busy} onChange={event => {
              setLookup(event.target.value)
              setMatches(null)
              setSource('')
            }} />
          </label>
          <button type="submit" disabled={busy || !lookup.trim()}>Find source phone</button>
          {matches && (matches.length === 0 ? <p>No matching phones.</p> : <>
            <label>Source phone
              <select value={source} disabled={busy} onChange={event => setSource(event.target.value)}>
                <option value="">Choose a source phone</option>
                {matches.map(phone => <option key={phone.id} value={phone.id}>{phone.displayName} · {phone.terminalIdHex}</option>)}
              </select>
            </label>
            <button type="button" disabled={busy || !source} onClick={() => void copyGroups()}>Review copied groups</button>
          </>)}
        </form>}
        <form onSubmit={(event) => { event.preventDefault(); void save() }}>
          <fieldset disabled={busy}>
            <legend>Participating groups</legend>
            {selected.length > groups.limit && <p role="status">
              {selected.length} selected. Uncheck groups to stay within the {groups.limit}-group limit.
            </p>}
            {groups.available.map((group) => (
              <label key={group.id} style={{ display: 'block' }}>
                <input type="checkbox" checked={selected.includes(group.id)}
                  disabled={(groups.groupIds.includes(group.id) ? !canDelete : !canAdd)
                    || (selected.length >= groups.limit && !selected.includes(group.id))}
                  onChange={(event) => {
                    setSelected(event.target.checked ? [...selected, group.id] : selected.filter((id) => id !== group.id))
                    setNotice('')
                  }} /> {group.name}
              </label>
            ))}
            {selected.length > 1 && (
              <label>Primary group
                <select value={selected[0]} disabled={!canAdd || !canDelete} onChange={(event) => {
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
          <button type="submit" disabled={busy || (!canAdd && !canDelete) || selected.length > groups.limit || selected.join(',') === groups.groupIds.join(',')}>
            Save phone groups
          </button>
        </form>
        </>
      )}
      {groups?.available.length === 0 && <p>No matching groups. <Link to="/terminal-groups">Manage phone groups</Link>.</p>}
    </details>
  )
}

export function TerminalDetail() {
  const { id } = useParams()
  const canReadPhone = canPerform('phones', op.read)
  const canUpdatePhone = canPerform('phones', op.update)
  const canReadAssignment = canPerform('table-assignments', op.read)
  const canAssign = canReadAssignment && canPerform('table-assignments', op.update)
  const canReadDiagnostics = canPerform('diagnostics', op.read)
  const canSaveSnapshot = canPerform('diagnostics', op.add)
  const canRequestCdr = canPerform('uploads', op.add)
  const canRequestReload = canPerform('downloads', op.add)
  const canReadStatus = canPerform('phone-status', op.read)
  const canReadSessions = canPerform('ncc-sessions', op.read)
  const canReadCalls = canPerform('calls', op.read)
  const canReadDownloads = canPerform('downloads', op.read)
  const canReadTableSets = canPerform('table-sets', op.read)
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
  const [diagNotice, setDiagNotice] = useState('')
  const [snapJson, setSnapJson] = useState('{}')
  const [timeline, setTimeline] = useState<TimelineResp | null>(null)
  const [tlErr, setTlErr] = useState<string | null>(null)
  const [sessions, setSessions] = useState<SessionRow[] | null>(null)
  const [lastCall, setLastCall] = useState<CallRow | null | undefined>(undefined)
  const [batches, setBatches] = useState<BatchView[] | null>(null)
  const [sessionErr, setSessionErr] = useState('')
  const [callErr, setCallErr] = useState('')
  const [batchErr, setBatchErr] = useState('')

  useEffect(() => {
    if (!id || !canReadPhone) return
    let c = false
    setErr(null)
    setTerm(null)
    setAssign(undefined)
    setSets([])
    setPick('')
    setDiag(null)
    setDiagErr(null)
    setDiagNotice('')
    setTimeline(null)
    setTlErr(null)
    setFirmwareEdit(null)
    setFirmwareNotice('')
    setFamilyEdit(null)
    setFamilyNotice('')
    ;(async () => {
      try {
        const t = await apiFetch(`/api/terminals/${id}`)
        if (!t.ok) throw new Error(String(t.status))
        const terminal = await t.json() as Term
        if (!c) setTerm(terminal)

        if (c) return
        if (canReadAssignment) void apiFetch(`/api/terminals/${id}/table-assignment`).then(async response => {
          if (response.status === 404) { if (!c) setAssign(null); return }
          if (!response.ok) throw new Error(`Could not read assignment (HTTP ${response.status}).`)
          const value = await response.json() as Assign
          if (!c) setAssign(value)
        }).catch(error => { if (!c) setErr(error.message) })

        if (canAssign) void apiGet<SetRow[]>(`/api/terminals/${id}/table-assignment-options`).then(list => {
          if (c) return
          setSets(list)
          const published = list.find(set => set.status === 1 && !assignmentIssue(set, terminal.firmwareVersionId))
          setPick(published?.id ?? '')
        }).catch(() => { if (!c) setErr('Could not load table assignment choices.') })

        if (canReadDiagnostics) void apiGet<DiagnosticsPayload>(`/api/terminals/${id}/diagnostics`)
          .then(value => { if (!c) setDiag(value) }).catch(() => { if (!c) setDiagErr('Could not read phone diagnostics.') })
        if (canReadStatus) void apiGet<TimelineResp>(`/api/operator/terminals/${id}/timeline?take=60`)
          .then(value => { if (!c) setTimeline(value) }).catch(() => { if (!c) setTlErr('Could not read operator timeline.') })
      } catch (e) {
        if (!c) setErr(e instanceof Error ? e.message : 'error')
      }
    })()
    return () => {
      c = true
    }
  }, [id, canReadPhone, canReadAssignment, canAssign, canReadDiagnostics, canReadStatus])

  async function applyAssignment(e: React.FormEvent) {
    e.preventDefault()
    if (!id || !pick || !canAssign || term?.deactivatedAtUtc) return
    setErr(null)
    try {
      const response = await apiPostRaw('/api/terminals/' + id + '/table-assignment', {
        tableSetId: pick,
        customerId: null,
        siteId: null,
      })
      if (!response.ok) {
        const body = await response.json().catch(() => null)
        throw new Error(body?.error ?? `Could not assign the set (HTTP ${response.status}).`)
      }
      if (assign?.tableSetId !== pick) rememberDownloadReview({ terminalId: id, tableSetId: pick, reason: 'assignment' })
      const a = await apiFetch(`/api/terminals/${id}/table-assignment`)
      if (a.ok) setAssign((await a.json()) as Assign)
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function pushSnapshot(e: React.FormEvent) {
    e.preventDefault()
    if (!id || !canSaveSnapshot) return
    setDiagErr(null)
    setDiagNotice('')
    try {
      await apiPost(`/api/terminals/${id}/diagnostics/snapshots`, {
        snapshotJson: snapJson,
        source: 'operator_ui',
      })
      setDiagNotice('Diagnostic snapshot saved.')
      if (canReadDiagnostics) setDiag(await apiGet<DiagnosticsPayload>(`/api/terminals/${id}/diagnostics`))
    } catch (ex) {
      setDiagErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function requestCdr() {
    if (!id || !canRequestCdr || term?.deactivatedAtUtc) return
    setDiagErr(null)
    setDiagNotice('')
    try {
      await apiPost(`/api/terminals/${id}/request-cdr-upload`, {
        detailJson: '{"intent":"cdr_upload"}',
        simulationMode: true,
      })
      setDiagNotice('CDR upload intent recorded.')
      if (canReadDiagnostics) setDiag(await apiGet<DiagnosticsPayload>(`/api/terminals/${id}/diagnostics`))
    } catch (ex) {
      setDiagErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function requestTableReload() {
    if (!id || !canRequestReload || term?.deactivatedAtUtc) return
    setDiagErr(null)
    setDiagNotice('')
    try {
      await apiPost(`/api/terminals/${id}/request-table-reload`, {
        detailJson: '{"intent":"table_reload"}',
        simulationMode: true,
      })
      setDiagNotice('Table reload intent recorded.')
      if (canReadDiagnostics) setDiag(await apiGet<DiagnosticsPayload>(`/api/terminals/${id}/diagnostics`))
    } catch (ex) {
      setDiagErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function rollback() {
    if (!id || !canAssign || term?.deactivatedAtUtc) return
    if (
      !window.confirm(
        'Rollback terminal table assignment to the previous set? Host routing only — HARDWARE_VALIDATION_REQUIRED on device.'
      )
    )
      return
    setErr(null)
    try {
      const response = await apiPostBare(`/api/terminals/${id}/table-assignment/rollback`, { confirm: true })
      if (!response.ok) {
        const body = await response.json().catch(() => null)
        throw new Error(body?.error ?? `Could not roll back the assignment (HTTP ${response.status}).`)
      }
      rememberDownloadReview({ terminalId: id, tableSetId: assign?.previousTableSetId ?? null, reason: 'assignment' })
      const a = await apiFetch(`/api/terminals/${id}/table-assignment`)
      if (a.ok) {
        const updated = await a.json() as Assign
        setAssign(updated)
        if (assign?.tableSetId !== updated.tableSetId)
          rememberDownloadReview({ terminalId: id, tableSetId: updated.tableSetId, reason: 'assignment' })
      }
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  useEffect(() => {
    if (!id || !canReadPhone) return
    let cancel = false
    setSessions(null)
    setLastCall(undefined)
    setBatches(null)
    setSessionErr('')
    setCallErr('')
    setBatchErr('')
    if (canReadSessions) void apiGet<SessionRow[]>('/api/ncc/sessions?includeArchived=true')
      .then(rows => { if (!cancel) setSessions(rows.filter(row => row.terminalId === id)) })
      .catch(() => { if (!cancel) setSessionErr('Could not read phone sessions.') })
    if (canReadCalls) void apiGet<CallRow[]>('/api/call-records')
      .then(rows => { if (!cancel) setLastCall(rows.find(row => row.terminalId === id) ?? null) })
      .catch(() => { if (!cancel) setCallErr('Could not read the last call.') })
    if (canReadDownloads) void (async () => {
      const batchRows = await apiGet<{ id: string; terminalId: string; status: number; lastError: string | null }[]>('/api/downloads')
      const views: BatchView[] = []
      for (const b of batchRows.filter((x) => x.terminalId === id).slice(0, 5)) {
        const detail = await apiFetch('/api/downloads/' + b.id)
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
      if (!cancel) setBatchErr('Could not read phone downloads.')
    })
    return () => {
      cancel = true
    }
  }, [id, canReadPhone, canReadSessions, canReadCalls, canReadDownloads])

  if (!id) return <p>Missing id.</p>
  if (!canReadPhone) return <p role="alert">Read permission for Phones is required to open a phone record.</p>
  if (err && !term) return <p style={{ color: 'crimson' }}>{err}</p>
  if (!term) return <p>Loading…</p>
  const firmwareSelection = firmwareEdit ?? term.firmwareVersionId ?? ''
  const familySelection = familyEdit ?? term.terminalFamilyId ?? ''
  const pickedSet = sets.find((set) => set.id === pick)
  const assignedSet = sets.find((set) => set.id === assign?.tableSetId)
  const terminalActive = !term.deactivatedAtUtc
  const assignedTargetId = assign?.targetFirmwareVersionId ?? assignedSet?.targetFirmwareVersionId
  const assignedTargetName = assign?.targetFirmware ?? assignedSet?.targetFirmware

  async function saveInstalledFirmware(event: React.FormEvent) {
    event.preventDefault()
    if (!term || firmwareEdit == null || !canUpdatePhone) return
    setSavingFirmware(true)
    setFirmwareNotice('')
    try {
      await apiPut(`/api/terminals/${term.id}/firmware`, { firmwareVersionId: firmwareEdit || null })
      if (term.firmwareVersionId !== (firmwareEdit || null))
        rememberDownloadReview({ terminalId: term.id, tableSetId: assign?.tableSetId ?? null, reason: 'firmware' })
      setTerm((current) => current?.id === term.id ? { ...current, firmwareVersionId: firmwareEdit || null } : current)
      setFirmwareEdit(null)
      setFirmwareNotice('Installed firmware saved.')
      if (canAssign) {
        setSets([]); setPick('')
        try {
          const list = await apiGet<SetRow[]>(`/api/terminals/${term.id}/table-assignment-options`)
          setSets(list)
          setPick(list.find(set => set.status === 1 && !assignmentIssue(set, firmwareEdit || null))?.id ?? '')
        } catch {
          setErr('Installed firmware saved, but the table assignment choices could not be refreshed.')
        }
      }
    } catch (error) {
      setErr(error instanceof Error ? error.message : 'Could not save installed firmware.')
    } finally {
      setSavingFirmware(false)
    }
  }

  async function saveTerminalFamily(event: React.FormEvent) {
    event.preventDefault()
    if (!term || familyEdit == null || !canUpdatePhone) return
    setSavingFamily(true)
    setFamilyNotice('')
    try {
      await apiPut(`/api/terminals/${term.id}/family`, { terminalFamilyId: familyEdit || null })
      if (term.terminalFamilyId !== (familyEdit || null))
        rememberDownloadReview({ terminalId: term.id, tableSetId: assign?.tableSetId ?? null, reason: 'family' })
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
        <Link to="/terminals">← Phones</Link>{canReadStatus && <> · <Link to={`/field-events?terminalId=${term.id}`}>Terminal status</Link></>}
      </p>
      <h1>{term.displayName}</h1>
      <PhoneDownloadReview terminalId={term.id} tableSetId={assign?.tableSetId ?? null}
        canSchedule={terminalActive && canReadTableSets && canRequestReload} canReadAssignment={canReadAssignment} />
      <p>
        Id: <code>{term.id}</code>
      </p>
      <p style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        {term.deactivatedAtUtc ? 'Inactive in Manager. Last reported status:' : 'Status:'}{' '}
        <StatusBadge
          label={terminalStatusLabel(term.status)}
          variant={terminalStatusVariant(term.status)}
          title={`Operational status enum value ${term.status}`}
        />
      </p>
      <form onSubmit={(event) => void saveInstalledFirmware(event)}
        style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'end', marginBottom: 12 }}>
        <FirmwareVersionSelect label="Installed firmware" value={firmwareSelection} onChange={setFirmwareEdit}
          emptyLabel="Unknown firmware" optionsPath="/api/terminals/firmware-options" disabled={savingFirmware || !canUpdatePhone} />
        <button type="submit" disabled={savingFirmware || !canUpdatePhone || firmwareSelection === (term.firmwareVersionId ?? '')}>Save firmware</button>
        {canPerform('firmware-registry', op.read) && <Link to="/firmware/versions">Manage versions</Link>}
      </form>
      {firmwareNotice && <p role="status">{firmwareNotice}</p>}
      <form onSubmit={(event) => void saveTerminalFamily(event)}
        style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'end', marginBottom: 12 }}>
        <TerminalFamilySelect value={familySelection} onChange={setFamilyEdit} disabled={savingFamily || !canUpdatePhone} />
        <button type="submit" disabled={savingFamily || !canUpdatePhone || familySelection === (term.terminalFamilyId ?? '')}>Save family</button>
      </form>
      {familyNotice && <p role="status">{familyNotice}</p>}
      {canPerform('phone-lifecycle', op.read) && <TerminalLifecyclePanel key={`lifecycle-${term.id}`} terminalId={term.id}
        onChanged={value => setTerm(current => current?.id === term.id ? { ...current,
          deactivatedAtUtc: value.deactivatedAtUtc, deletionReviewDate: value.deletionReviewDate } : current)} />}
      <TerminalIdentityPanel key={term.id} terminalId={term.id} wireId={term.terminalIdHex} family={term.terminalFamilyId} />
      {canPerform('inventory', op.read) && <TerminalInventoryPanel key={`inventory-${term.id}`} terminalId={term.id}
        onSaved={() => rememberDownloadReview({ terminalId: term.id, tableSetId: assign?.tableSetId ?? null, reason: 'inventory' })} />}
      {canPerform('inmate-assignments', op.read) && <InmateAssignmentPanel key={`inmate-${term.id}-${term.terminalFamilyId}`}
        terminalId={term.id} family={term.terminalFamilyId} />}
      {canPerform('memberships', op.read) && <PhoneGroups key={`groups-${term.id}`} terminalId={term.id} />}

      {canReadSessions && <>
      <h2>Phone session</h2>
      <div data-testid="ncc-sessions">
        {sessionErr && <ErrorBanner message={sessionErr} />}
        {sessions === null && !sessionErr && <p>Loading sessions…</p>}
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
      </>}

      {canReadCalls && <>
      <h2>Last rate</h2>
      <div data-testid="last-rate">
        {callErr && <ErrorBanner message={callErr} />}
        {lastCall === undefined && !callErr && <p>Loading last call…</p>}
        {lastCall === null && !callErr && <p>No rated call yet.</p>}
        {lastCall && (
          <p>
            {lastCall.dialedDigits || 'empty number'} · {dispositionWords(lastCall.disposition)} ·{' '}
            {new Date(lastCall.startedAtUtc).toLocaleString()}
          </p>
        )}
      </div>
      </>}

      {canPerform('cashbox', op.read) && <TerminalCashboxPanel key={`cashbox-${id}`} terminalId={id} />}

      {canReadDownloads && <>
      <h2>Downloads</h2>
      <div data-testid="download-status">
        {batchErr && <ErrorBanner message={batchErr} />}
        {batches === null && !batchErr && <p>Loading downloads…</p>}
        {batches?.length === 0 && <p>No download batch yet.</p>}
        {batches?.map((b) => (
          <p key={b.id}>
            <Link to={`/downloads/${b.id}`}>{batchWords(b.status)}</Link>
            {b.phases.length > 0 ? ` · ${b.phases.join(', ')}` : ''}
            {b.lastError ? ` · ${b.lastError}` : ''}
          </p>
        ))}
      </div>
      </>}
      {err && <p role="alert" style={{ color: 'crimson' }}>{err}</p>}

      {canPerform('callbacks', op.read) && <SpecialCallback key={`callback-${id}`} terminalId={id} terminalActive={terminalActive} />}
      {canReadAssignment && <>
      <h2>Table assignment</h2>
      {assign === undefined && !err && <p>Loading assignment…</p>}
      {assign === null && <p>No assignment yet.</p>}
      {assign && (
        <div style={{ background: '#f6f8fa', padding: 12, marginBottom: 12 }}>
          <div>
            Active set {canReadTableSets
              ? <Link to={`/table-sets/${assign.tableSetId}`}>{assign.tableSetName ?? assignedSet?.name ?? assign.tableSetId}</Link>
              : <span>{assign.tableSetName ?? assignedSet?.name ?? assign.tableSetId}</span>}
          </div>
          {terminalActive && canReadTableSets && canRequestReload && <div><Link to={downloadSchedulePath(assign.tableSetId, term.id)}>Schedule assigned configuration</Link></div>}
          {canReadTableSets && term.terminalFamilyId && <div><Link to={`/table-sets/${assign.tableSetId}?family=${encodeURIComponent(term.terminalFamilyId)}#family-review-heading`}>
            Review table requirements for this family</Link></div>}
          {assignedTargetId && assignedTargetId !== term.firmwareVersionId &&
            <p role="status">This set requires {assignedTargetName}. Register matching firmware or assign a compatible set.</p>}
          {assignedSet?.compatibilityIssue && <p role="status">The current assignment is incompatible with the registered firmware: {assignedSet.compatibilityIssue}</p>}
          {assign.previousTableSetId && (
            <div>
              Previous set: <code>{assign.previousTableSetId}</code>
            </div>
          )}
          <div>Assigned: {new Date(assign.assignedAtUtc).toLocaleString()}</div>
        </div>
      )}
      {!terminalActive && <p>Reactivate the phone before assigning or rolling back a table set.</p>}
      {canAssign && <form onSubmit={applyAssignment} style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
        <label>
          Table set
          <select value={pick} disabled={!terminalActive} onChange={(e) => setPick(e.target.value)} style={{ display: 'block', minWidth: 220 }}>
            <option value="">Choose a table set</option>
            {sets.map((s) => {
              const issue = assignmentIssue(s, term.firmwareVersionId)
              return <option key={s.id} value={s.id} disabled={!!issue}>
                {s.name} {issue ? `(${issue})` : s.status === 1 ? '(published)' : '(draft)'}
              </option>
            })}
          </select>
        </label>
        <button type="submit" disabled={!terminalActive || !pickedSet || !!assignmentIssue(pickedSet, term.firmwareVersionId)}>Assign set</button>
        <button type="button" disabled={!terminalActive || !assign?.previousTableSetId} onClick={() => void rollback()}>
          Rollback to previous set
        </button>
      </form>}
      {assign !== undefined && <TerminalConfigurationWorksheet key={`worksheet-${id}`} terminalId={id}
        assignmentKey={`${assign?.tableSetId ?? ''}:${term.firmwareVersionId ?? ''}:${term.terminalFamilyId ?? ''}`}
        terminalActive={terminalActive} onApplied={result => {
          rememberDownloadReview({ terminalId: id, tableSetId: result.tableSetId, reason: 'assignment' })
          void apiFetch(`/api/terminals/${id}/table-assignment`).then(async response => {
            if (response.ok) setAssign(await response.json() as Assign)
            else setErr('The assignment was saved, but its phone summary could not be refreshed.')
          }).catch(() => setErr('The assignment was saved, but its phone summary could not be refreshed.'))
        }} />}
      </>}

      {canReadAssignment && canPerform('table-records', op.read) && assign !== undefined &&
        <RoutineCallInPanel key={`call-in-${id}`} terminalId={id} assignmentKey={assign?.tableSetId ?? ''} terminalActive={terminalActive} />}

      {(canReadDiagnostics || canSaveSnapshot || canRequestCdr || canRequestReload) && <>
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
      {canReadDiagnostics && !diag && !diagErr && <p>Loading diagnostics…</p>}
      {diagNotice && <p role="status">{diagNotice}</p>}
      {canSaveSnapshot && <form onSubmit={(e) => void pushSnapshot(e)} style={{ marginBottom: 12 }}>
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
      </form>}
      {(canRequestCdr || canRequestReload) && <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 16 }}>
        {canRequestCdr && <button type="button" disabled={!terminalActive} onClick={() => void requestCdr()}>
          Request CDR upload (intent)
        </button>}
        {canRequestReload && <button type="button" disabled={!terminalActive} onClick={() => void requestTableReload()}>
          Request table reload (intent)
        </button>}
      </div>}
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
      </>}

      {canReadStatus && <>
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
                  {it.kind === 'dlog' && canPerform('message-log', op.read) ? (
                    <Link to={`/dlog/${it.refKey}`}>DLOG</Link>
                  ) : it.kind === 'downloadBatch' && canReadDownloads ? (
                    <Link to={`/downloads/${it.refKey}`}>Batch</Link>
                  ) : it.kind === 'firmwareJob' && canPerform('firmware-jobs', op.read) ? (
                    <Link to={`/firmware/jobs/${it.refKey}`}>Job</Link>
                  ) : it.kind === 'craftSession' && canPerform('craft', op.read) ? (
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
      </>}

      {(canReadDownloads || canPerform('message-log', op.read)) && <>
      <h2 style={{ marginTop: 24 }}>Downloads</h2>
      {canReadDownloads && <p>
        <Link to={`/downloads?terminal=${encodeURIComponent(id)}`}>Open downloads for this terminal</Link>
      </p>}
      {canPerform('message-log', op.read) && <p>
        <Link to={`/dlog?terminal=${encodeURIComponent(id)}`}>Messages from this phone</Link>
      </p>}
      </>}
    </div>
  )
}
