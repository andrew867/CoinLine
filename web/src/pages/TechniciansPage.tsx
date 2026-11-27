import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, apiGet } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'

type Technician = { id: string; displayName: string; employeeReference: string | null; enabled: boolean; revision: number;
  hasIdentifier: boolean; matchingAvailable: boolean }
type Directory = { identifierMatchingConfigured: boolean; items: Technician[] }
type Activity = { id: string; terminalId: string | null; terminalName: string | null; capturedAtUtc: string; matchStatus: string;
  technicianId: string | null; technicianName: string | null; employeeReference: string | null; opcode: number | null; summary: string | null; operatorDescription?: string | null }
type Alarm = { id: string; capturedAtUtc: string; alarmNumber: number | null; summary: string | null }
type Edit = { id: string | null; displayName: string; employeeReference: string; enabled: boolean; revision: number; reportedIdentifier: string; clearIdentifier: boolean }
const blank = (): Edit => ({ id: null, displayName: '', employeeReference: '', enabled: true, revision: 0, reportedIdentifier: '', clearIdentifier: false })
const matchLabels: Record<string, string> = { matched: 'Reported identifier matched', unmatched: 'No directory match', malformed: 'Unsupported identifier format',
  unconfigured: 'Matching not configured', unavailable: 'Association unavailable', 'not-recorded': 'No association recorded' }

export function TechniciansPage() {
  const read = canPerform('technicians', op.read)
  const add = canPerform('technicians', op.add)
  const update = canPerform('technicians', op.update)
  const [directory, setDirectory] = useState<Directory | null>(null)
  const [edit, setEdit] = useState<Edit | null>(null)
  const [activities, setActivities] = useState<{ total: number; items: Activity[] } | null>(null)
  const [technician, setTechnician] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [loadedFilter, setLoadedFilter] = useState('')
  const [alarms, setAlarms] = useState<{ activity: Activity; items: Alarm[] } | null>(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)

  async function loadDirectory() {
    try { setDirectory(await apiGet<Directory>('/api/technicians')) }
    catch (err) { setError(err instanceof Error ? err.message : 'Could not load technicians.') }
  }
  async function loadActivity(query = loadedFilter) {
    try { setActivities(await apiGet<{ total: number; items: Activity[] }>(`/api/technicians/activity${query}`)) }
    catch (err) { setError(err instanceof Error ? err.message : 'Could not load technician activity.') }
  }
  useEffect(() => { if (read) { void loadDirectory(); void loadActivity('') } }, [read])

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!edit || busy || edit.id && !update || !edit.id && !add) return
    setError(''); setNotice(''); setBusy(true)
    try {
      const body = { displayName: edit.displayName.trim(), employeeReference: edit.employeeReference.trim() || null, enabled: edit.enabled,
        revision: edit.revision, reportedIdentifier: edit.reportedIdentifier || null, clearIdentifier: edit.clearIdentifier }
      const response = await apiFetch(edit.id ? `/api/technicians/${edit.id}` : '/api/technicians', {
        method: edit.id ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
      })
      const data = await response.json().catch(() => null)
      if (!response.ok) throw new Error(data?.error ?? `Technician save failed (${response.status}).`)
      setEdit(null); setNotice('Technician profile saved. No terminal or operator login changed.')
      await loadDirectory()
    } catch (err) { setError(err instanceof Error ? err.message : 'Could not save technician profile.') }
    finally { setBusy(false) }
  }

  function filter(event: FormEvent) {
    event.preventDefault(); setError(''); setAlarms(null)
    const query = new URLSearchParams()
    if (technician) query.set('technicianId', technician)
    if (from) query.set('fromUtc', new Date(from).toISOString())
    if (to) query.set('toUtc', new Date(to).toISOString())
    const value = query.size ? `?${query}` : ''
    setLoadedFilter(value); void loadActivity(value)
  }

  async function related(activity: Activity) {
    setError(''); setAlarms(null)
    try { const result = await apiGet<{ items: Alarm[] }>(`/api/technicians/activity/${activity.id}/nearby-alarms`); setAlarms({ activity, items: result.items }) }
    catch (err) { setError(err instanceof Error ? err.message : 'Could not load nearby alarms.') }
  }

  if (!read) return <p role="alert">Read permission for technician activity is required.</p>
  return <div>
    <h1>Technicians and activity</h1>
    <p className="page-intro">Associate a technician profile with the identifier reported by the phone. It is not a CoinLine password or proof of personal identity. Reports retain the association that was recorded at reception.</p>
    {error && <p role="alert">{error}</p>}{notice && <p role="status">{notice}</p>}
    <section className="table-config-panel" aria-label="Technician directory">
      <h2>Technician directory</h2>
      {directory && !directory.identifierMatchingConfigured && <p role="note">Identifier matching is disabled until a separate server key is configured. Profiles and unassociated activity remain available.</p>}
      <div className="row-actions"><button type="button" className="quiet" onClick={() => void loadDirectory()}>Refresh directory</button>
        {add && <button type="button" onClick={() => { setEdit(blank()); setError('') }}>Add technician</button>}</div>
      {!directory ? <p role="status">Loading directory…</p> : directory.items.length === 0 ? <p>No technicians registered.</p> :
        <table><thead><tr><th>Technician</th><th>Employee reference</th><th>Status</th><th>Reported identifier</th>{update && <th>Actions</th>}</tr></thead>
          <tbody>{directory.items.map(row => <tr key={row.id}><td>{row.displayName}</td><td>{row.employeeReference || 'Not set'}</td>
            <td>{row.enabled ? 'Active' : 'Inactive'}</td><td>{row.matchingAvailable ? 'Associated' : row.hasIdentifier ? 'Association unavailable' : 'Not associated'}</td>
            {update && <td><button type="button" onClick={() => { setEdit({ ...row, employeeReference: row.employeeReference ?? '', reportedIdentifier: '', clearIdentifier: false }); setError('') }}>Edit {row.displayName}</button></td>}</tr>)}</tbody></table>}
      {edit && <form onSubmit={event => void save(event)} aria-label="Technician profile">
        <h3>{edit.id ? 'Edit technician' : 'New technician'}</h3>
        <fieldset disabled={busy}>
          <label>Display name<input required maxLength={120} value={edit.displayName} onChange={event => setEdit({ ...edit, displayName: event.target.value })} /></label>
          <label>Employee reference<input maxLength={60} value={edit.employeeReference} onChange={event => setEdit({ ...edit, employeeReference: event.target.value })} /></label>
          <label><input type="checkbox" checked={edit.enabled} onChange={event => setEdit({ ...edit, enabled: event.target.checked })} /> Active technician</label>
          {directory?.identifierMatchingConfigured && <label>Reported Craft identifier (write only)
            <input type="password" autoComplete="new-password" inputMode="numeric" pattern="[0-9]{5}" maxLength={5} disabled={edit.clearIdentifier}
              value={edit.reportedIdentifier} onChange={event => setEdit({ ...edit, reportedIdentifier: event.target.value })} />
          </label>}
          {edit.id && <><p>Leave the identifier blank to keep the existing association.</p><label><input type="checkbox" checked={edit.clearIdentifier}
            onChange={event => setEdit({ ...edit, clearIdentifier: event.target.checked, reportedIdentifier: '' })} /> Clear association</label></>}
          <button type="submit" disabled={busy}>Save technician</button>{' '}<button type="button" className="quiet" onClick={() => setEdit(null)}>Cancel</button>
        </fieldset>
      </form>}
    </section>
    <section className="table-config-panel" aria-label="Reported maintenance activity">
      <h2>Reported maintenance activity</h2>
      <form onSubmit={filter} className="table-config-selectors">
        <label>Technician<select value={technician} onChange={event => setTechnician(event.target.value)}><option value="">All reports, including unmatched</option>
          {directory?.items.map(row => <option value={row.id} key={row.id}>{row.displayName}</option>)}</select></label>
        <label>From<input type="datetime-local" value={from} onChange={event => setFrom(event.target.value)} /></label>
        <label>To<input type="datetime-local" value={to} onChange={event => setTo(event.target.value)} /></label>
        <button type="submit">Filter activity</button>
      </form>
      {activities && <><p>{activities.items.length} of {activities.total} reported events</p>
        {activities.items.length === 0 ? <p>No matching maintenance reports.</p> : <table><thead><tr><th>Received</th><th>Phone</th><th>Technician association</th><th>Reported operation</th><th>Alarm context</th></tr></thead>
          <tbody>{activities.items.map(row => <tr key={row.id}><td>{new Date(row.capturedAtUtc).toLocaleString()}</td><td>{row.terminalName ?? 'Unknown phone'}</td>
            <td>{row.technicianName && <strong>{row.technicianName} </strong>}{matchLabels[row.matchStatus] ?? 'Unknown status'}</td>
            <td>{row.operatorDescription && <><strong>{row.operatorDescription}{row.opcode !== null ? ` (opcode ${row.opcode})` : ''}</strong><br /></>}
              {row.summary ?? (row.opcode !== null ? `Operation ${row.opcode}` : 'Unknown operation')}{row.operatorDescription && <small> Current operator classification above.</small>}</td>
            <td><button type="button" onClick={() => void related(row)}>Nearby alarms</button></td></tr>)}</tbody></table>}</>}
      {alarms && <section aria-label="Nearby alarm reports"><h3>Nearby alarm reports for {alarms.activity.terminalName ?? 'this phone'}</h3>
        <p>Up to 20 alarms reported by the same phone in the preceding 24 hours. These are possible context, not confirmed responses or resolved faults.</p>
        {alarms.items.length === 0 ? <p>No nearby alarm reports.</p> : <ul>{alarms.items.map(alarm => <li key={alarm.id}>{new Date(alarm.capturedAtUtc).toLocaleString()}: {' '}
          {canPerform('message-log', op.read) ? <Link to={`/dlog/${alarm.id}`}>{alarm.summary ?? `Alarm ${alarm.alarmNumber ?? 'unknown'}`}</Link> : alarm.summary ?? `Alarm ${alarm.alarmNumber ?? 'unknown'}`}</li>)}</ul>}
      </section>}
    </section>
  </div>
}
