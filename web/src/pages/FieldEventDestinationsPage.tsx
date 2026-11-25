import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api/client'
import './FieldEventsPage.css'

type Kind = 'alarm' | 'maintenance'
type Rule = { id: string; kind: Kind; code: number | null; regionCode: string | null; groupId: string | null; terminalId: string | null; destination: string; revision: number }
type Data = { rules: Rule[]; canCreate: boolean; canEdit: boolean; canDelete: boolean }
type Option = { id: string; name: string }
type Options = { phones: Option[]; groups: Option[]; regions: string[] }
type Decision = { status: string; destination: string | null; ruleIds: string[] }
type QueueRow = Decision & { transactionId: string; kind: Kind; code: number; capturedAtUtc: string; terminalName: string | null; summary: string | null }
type Draft = { id: string | null; kind: Kind; code: string; region: string; group: string; phone: string; destination: string; revision: number }
const endpoint = '/api/field-event-destinations'
const blank = (): Draft => ({ id: null, kind: 'alarm', code: '', region: '', group: '', phone: '', destination: '', revision: 0 })
const validCode = (kind: Kind, value: string) => /^\d+$/.test(value) && Number.isSafeInteger(Number(value)) && Number(value) <= (kind === 'alarm' ? 255 : 65535)
const statuses: Record<string, string> = { routed: 'Routed', ambiguous: 'Ambiguous rules', unassigned: 'Unassigned', unmatched: 'Unmatched phone' }
const statusLabel = (status: string) => statuses[status] ?? status
async function get<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await apiFetch(path, init)
  if (!response.ok) {
    const body = await response.json().catch(() => null)
    throw new Error(body?.error ?? `Request failed (HTTP ${response.status}).`)
  }
  return response.status === 204 ? undefined as T : response.json() as Promise<T>
}

export function FieldEventDestinationsPage() {
  const [data, setData] = useState<Data | null>(null)
  const [options, setOptions] = useState<Options | null>(null)
  const [draft, setDraft] = useState<Draft | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const [previewPhone, setPreviewPhone] = useState('')
  const [previewKind, setPreviewKind] = useState<Kind>('alarm')
  const [previewCode, setPreviewCode] = useState('')
  const [decision, setDecision] = useState<Decision | null>(null)
  const [queue, setQueue] = useState<QueueRow[] | null>(null)
  const [queueInput, setQueueInput] = useState('')
  const [queueFilter, setQueueFilter] = useState('')
  const [queueRefresh, setQueueRefresh] = useState(0)
  const [queueError, setQueueError] = useState('')
  const [previewError, setPreviewError] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    Promise.all([get<Data>(endpoint, { signal: controller.signal }), get<Options>(`${endpoint}/options`, { signal: controller.signal })])
      .then(([rules, choices]) => { if (!controller.signal.aborted) { setData(rules); setOptions(choices); setError('') } })
      .catch(failure => { if (!controller.signal.aborted) setError(failure.message) })
    return () => controller.abort()
  }, [refresh])

  useEffect(() => {
    const controller = new AbortController()
    get<QueueRow[]>(`${endpoint}/queue?destination=${encodeURIComponent(queueFilter)}`, { signal: controller.signal })
      .then(rows => { if (!controller.signal.aborted) { setQueue(rows); setQueueError('') } })
      .catch(failure => { if (!controller.signal.aborted) setQueueError(failure.message) })
    return () => controller.abort()
  }, [queueFilter, queueRefresh])

  function edit(row: Rule) {
    setNotice(''); setError('')
    setDraft({ id: row.id, kind: row.kind, code: row.code?.toString() ?? '', region: row.regionCode ?? '',
      group: row.groupId ?? '', phone: row.terminalId ?? '', destination: row.destination, revision: row.revision })
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!draft || busy || (draft.code !== '' && !validCode(draft.kind, draft.code))) return
    setBusy(true); setError(''); setNotice('')
    try {
      await get<Rule>(draft.id ? `${endpoint}/${draft.id}` : endpoint, {
        method: draft.id ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ kind: draft.kind, code: draft.code === '' ? null : Number(draft.code), regionCode: draft.region || null,
          groupId: draft.group || null, terminalId: draft.phone || null, destination: draft.destination, revision: draft.revision }),
      })
      setDraft(null); setDecision(null); setNotice('Destination rule saved.'); setRefresh(value => value + 1)
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Could not save.') }
    finally { setBusy(false) }
  }

  async function remove(row: Rule) {
    if (busy || !window.confirm('Remove this rule? Recorded routing decisions will remain unchanged.')) return
    setBusy(true); setError(''); setNotice('')
    try {
      await get<void>(`${endpoint}/${row.id}?revision=${row.revision}`, { method: 'DELETE' })
      setDraft(null); setDecision(null); setNotice('Rule removed.'); setRefresh(value => value + 1)
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Could not remove rule.') }
    finally { setBusy(false) }
  }

  async function preview(event: FormEvent) {
    event.preventDefault()
    if (busy || !previewPhone || !validCode(previewKind, previewCode)) return
    setBusy(true); setDecision(null); setPreviewError('')
    try {
      const query = new URLSearchParams({ terminalId: previewPhone, kind: previewKind, code: previewCode })
      setDecision(await get<Decision>(`${endpoint}/preview?${query}`))
    } catch (failure) { setPreviewError(failure instanceof Error ? failure.message : 'Could not preview.') }
    finally { setBusy(false) }
  }

  const label = (rows: Option[] | undefined, id: string | null) => id ? rows?.find(row => row.id === id)?.name ?? 'Removed selection' : 'All'
  const selector = (name: string, selected: string, rows: Option[] | undefined, onChange: (value: string) => void, empty = 'All') =>
    <label>{name}<select value={selected} onChange={event => onChange(event.target.value)}>
      <option value="">{empty}</option>
      {selected && !rows?.some(row => row.id === selected) && <option value={selected}>Removed selection</option>}
      {rows?.map(row => <option key={row.id} value={row.id}>{row.name}</option>)}
    </select></label>

  return <div className="terminal-status-view">
    <header className="terminal-status-hero"><div>
      <h1>Event destinations</h1><p>Route future phone alarms and maintenance reports to internal work queues.</p>
    </div></header>
    <p><Link to="/field-events">Terminal status</Link></p>
    <section className="terminal-status-panel" aria-label="Destination rules">
      <header className="terminal-status-panel-heading"><h2>Destination rules</h2>
        {data?.canCreate && <button type="button" disabled={busy} onClick={() => { setDraft(blank()); setError(''); setNotice('') }}>New rule</button>}
      </header>
      <div className="field-event-reference">
        <p>All selected scope fields must match. A rule with more matching scope fields wins; at equal scope specificity, a code-specific rule wins over a default. Conflicting equally ranked destinations are reported without assigning a queue.</p>
        {error && <p role="alert">{error}</p>}{notice && <p role="status">{notice}</p>}
        {!data && !error && <p role="status">Loading rules…</p>}
        <button type="button" disabled={busy} onClick={() => { setDraft(null); setDecision(null); setRefresh(value => value + 1) }}>Reload rules</button>
        {draft && <form className="field-event-code-editor" onSubmit={save} aria-label="Edit destination rule">
          <h3>{draft.id ? 'Edit rule' : 'New rule'}</h3>
          <div className="field-event-code-picker">
            <label>Category<select value={draft.kind} onChange={event => setDraft({ ...draft, kind: event.target.value as Kind, code: '' })}><option value="alarm">Alarm</option><option value="maintenance">Maintenance</option></select></label>
            <label>Code (blank for default)<input inputMode="numeric" value={draft.code} onChange={event => setDraft({ ...draft, code: event.target.value })} /></label>
            {selector('Region', draft.region, options?.regions.map(region => ({ id: region, name: region })), region => setDraft({ ...draft, region }))}
            {selector('Group', draft.group, options?.groups, group => setDraft({ ...draft, group }))}
            {selector('Phone', draft.phone, options?.phones, phone => setDraft({ ...draft, phone }))}
          </div>
          <label>Internal destination name<input required maxLength={80} value={draft.destination} onChange={event => setDraft({ ...draft, destination: event.target.value })} /></label>
          <div className="field-event-code-actions">
            <button type="submit" disabled={busy || !draft.destination.trim() || (draft.code !== '' && !validCode(draft.kind, draft.code))}>Save rule</button>
            <button type="button" disabled={busy} onClick={() => setDraft(null)}>Cancel</button>
          </div>
        </form>}
        {data && <section aria-label="Configured rules">
          {data.rules.length === 0 ? <p>No destination rules configured.</p> :
            <table><thead><tr><th>Event</th><th>Region</th><th>Group</th><th>Phone</th><th>Destination</th><th>Actions</th></tr></thead><tbody>
              {data.rules.map(row => <tr key={row.id}><td>{row.kind} {row.code ?? 'default'}</td><td>{row.regionCode ?? 'All'}</td><td>{label(options?.groups, row.groupId)}</td><td>{label(options?.phones, row.terminalId)}</td><td>{row.destination}</td><td>
                {data.canEdit && <button type="button" disabled={busy} onClick={() => edit(row)}>Edit</button>} {data.canDelete && <button type="button" disabled={busy} onClick={() => void remove(row)}>Remove</button>}
              </td></tr>)}
            </tbody></table>}
        </section>}
      </div>
    </section>
    <section className="terminal-status-panel" aria-label="Route preview">
      <header className="terminal-status-panel-heading"><h2>Preview a phone and code</h2></header>
      <div className="field-event-reference">
        <form className="field-event-code-picker" onSubmit={preview}>
          {selector('Preview phone', previewPhone, options?.phones, value => { setPreviewPhone(value); setDecision(null) }, 'Choose a phone')}
          <label>Preview category<select value={previewKind} onChange={event => { setPreviewKind(event.target.value as Kind); setDecision(null) }}><option value="alarm">Alarm</option><option value="maintenance">Maintenance</option></select></label>
          <label>Preview code<input inputMode="numeric" value={previewCode} onChange={event => { setPreviewCode(event.target.value); setDecision(null) }} /></label>
          <button type="submit" disabled={busy || !previewPhone || !validCode(previewKind, previewCode)}>Preview route</button>
        </form>
        {previewError && <p role="alert">{previewError}</p>}
        {decision && <div role="status"><strong>{statusLabel(decision.status)}</strong>{decision.destination && <>: {decision.destination}</>}
          {decision.ruleIds.length > 0 && <ul>{decision.ruleIds.map(id => <li key={id}>{data?.rules.find(rule => rule.id === id)?.destination ?? id}</li>)}</ul>}</div>}
      </div>
    </section>
    <section className="terminal-status-panel" aria-label="Recorded destinations">
      <header className="terminal-status-panel-heading"><h2>Recorded routing decisions</h2></header>
      <div className="field-event-reference">
        <p>Only reports ingested after routing was introduced appear here. Editing rules does not reassign existing reports.</p>
        <form className="field-event-code-picker" onSubmit={event => { event.preventDefault(); setQueueFilter(queueInput.trim()); setQueueRefresh(value => value + 1) }}>
          <label>Filter destination<input maxLength={80} value={queueInput} onChange={event => setQueueInput(event.target.value)} /></label>
          <button type="submit">Refresh queue</button>
        </form>
        {queueError && <p role="alert">{queueError}</p>}{!queue && !queueError && <p role="status">Loading decisions…</p>}
        {queue?.length === 0 && <p>No recorded routing decisions.</p>}
        {queue && queue.length > 0 && <section aria-label="Routing history"><table><thead><tr><th>Report</th><th>Phone</th><th>Outcome</th><th>Destination</th><th>Recorded</th></tr></thead><tbody>
          {queue.map(row => <tr key={row.transactionId}><td><Link to={`/dlog/${row.transactionId}`}>{row.summary ?? `${row.kind} ${row.code}`}</Link></td><td>{row.terminalName ?? 'Unmatched'}</td><td>{statusLabel(row.status)}</td><td>{row.destination ?? 'None'}</td><td>{new Date(row.capturedAtUtc).toLocaleString()}</td></tr>)}
        </tbody></table></section>}
      </div>
    </section>
  </div>
}
