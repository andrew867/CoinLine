import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPostRaw } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import './TerminalConfigurationWorksheet.css'
import { FamilyOperatingModeNotice, type FamilyOperatingMode } from './FamilyOperatingModeNotice'

type RecordSummary = { configurationId: string | null; revision: number; length: number; sha256: string }
type AssignedRecord = RecordSummary & { id: string; setId: string; setName: string; validationPassed: boolean }
type RecordRow = { number: number; name: string; family: string; familyRequired: boolean; canChoose: boolean; canClear: boolean; ambiguous: boolean; assigned: AssignedRecord | null }
type FamilyReview = { familyLabel: string; documented: boolean; note?: string | null; tables: { name: string; required: boolean; status: string }[];
  operatingMode?: FamilyOperatingMode | null }
type Worksheet = { id: string; displayName: string; terminalIdHex: string; firmware: string | null; baseSetId: string | null; baseSetName: string | null;
  assignmentRevision: number | null; canUpdate: boolean; family: FamilyReview | null; records: RecordRow[] }
type Option = RecordSummary & { id: string; tableSetId: string; setName: string; targetFirmware: string | null; issue: string | null }
type Options = { records: Option[]; total: number; page: number; pageSize: number }
type Choice = { action: 'copy'; source: Option } | { action: 'clear' }
type Preview = { previewToken: string; canApply: boolean; initialAssignment: boolean; family: FamilyReview | null;
  changes: { tableNumber: number; action: string; required: boolean; personalized: boolean; warning?: string | null; current: AssignedRecord | null; result: RecordSummary | null }[] }
type Applied = { tableSetId: string; tableSetName: string; changedNumbers: number[]; removedNumbers: number[] }

function recordLabel(record: RecordSummary | null) { return record ? `${record.configurationId ?? 'Unnamed configuration'} · revision ${record.revision}` : 'Not assigned' }
const actionLabels: Record<string, string> = { add: 'Add', replace: 'Replace', remove: 'Clear assignment', keep: 'Unchanged', absent: 'Already absent' }

function FamilyReviewNotice({ review }: { review: FamilyReview | null }) {
  if (!review?.documented) return <p className="worksheet-note">No complete legacy family checklist is available for this registered terminal type.</p>
  const missing = review.tables.filter(row => row.required && row.status !== 'present')
  return <div className="worksheet-family-review">
    <strong>Legacy requirements: {review.familyLabel}</strong>
    <FamilyOperatingModeNotice mode={review.operatingMode} />
    {missing.length > 0 ? <p role="note">Required families needing review: {missing.map(row => `${row.name} (${row.status})`).join(', ')}.</p>
      : <p>All documented required families have at least one matching record in the catalogue review.</p>}
    <p>This is a Manager configuration review, not proof of complete firmware INSTALL or device behavior.</p>
  </div>
}

export function TerminalConfigurationWorksheet({ terminalId, assignmentKey, terminalActive, onApplied }: {
  terminalId: string; assignmentKey: string; terminalActive: boolean; onApplied: (result: Applied) => void
}) {
  const endpoint = `/api/terminals/${terminalId}/configuration-worksheet`
  const [open, setOpen] = useState(false)
  const [worksheet, setWorksheet] = useState<Worksheet | null>(null)
  const [reload, setReload] = useState(0)
  const [family, setFamily] = useState('all')
  const [filter, setFilter] = useState('')
  const [staged, setStaged] = useState<Record<number, Choice>>({})
  const [chooser, setChooser] = useState<RecordRow | null>(null)
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)
  const [optionReload, setOptionReload] = useState(0)
  const [options, setOptions] = useState<Options | null>(null)
  const [optionError, setOptionError] = useState('')
  const [personalize, setPersonalize] = useState(false)
  const [preview, setPreview] = useState<Preview | null>(null)
  const [confirmed, setConfirmed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const canOpenRecords = canPerform('table-sets', op.read) && canPerform('table-records', op.read)

  useEffect(() => {
    if (!open) return
    let active = true
    setWorksheet(null); setError(''); setStaged({}); setChooser(null); setPersonalize(false); setPreview(null); setConfirmed(false)
    void apiGet<Worksheet>(endpoint).then(value => { if (active) setWorksheet(value) })
      .catch(() => { if (active) setError('Could not read this phone’s configuration worksheet.') })
    return () => { active = false }
  }, [open, endpoint, assignmentKey, reload])

  useEffect(() => {
    if (!open || chooser == null) return
    let active = true; setOptions(null); setOptionError('')
    void apiGet<Options>(`${endpoint}/options?number=${chooser.number}&query=${encodeURIComponent(query)}&page=${page}`)
      .then(value => { if (active) setOptions(value) }).catch(() => { if (active) setOptionError('Could not look up published configurations.') })
    return () => { active = false }
  }, [open, endpoint, chooser, query, page, optionReload])

  function selectRow(row: RecordRow) { setChooser(row); setSearch(''); setQuery(''); setPage(1); setOptionReload(value => value + 1) }
  function clearPreview() { setPreview(null); setConfirmed(false); setError(''); setNotice('') }
  function stage(number: number, choice?: Choice) {
    setStaged(current => {
      const next = { ...current }
      if (choice) next[number] = choice
      else delete next[number]
      return next
    })
    if (number === 21 && choice?.action !== 'copy') setPersonalize(false)
    clearPreview()
  }

  async function execute(apply: boolean) {
    if (!worksheet || !worksheet.canUpdate || !terminalActive || busy || (apply && (!preview?.canApply || !confirmed))) return
    setBusy(true); setError(''); setNotice('')
    if (!apply) { setPreview(null); setConfirmed(false) }
    try {
      const choices = Object.entries(staged).map(([number, choice]) => choice.action === 'copy'
        ? { number: Number(number), action: 'copy', sourceSetId: choice.source.tableSetId, sourceVersionId: choice.source.id }
        : { number: Number(number), action: 'clear' })
      const response = await apiPostRaw(`${endpoint}${apply ? '' : '/preview'}`, {
        baseSetId: worksheet.baseSetId, assignmentRevision: worksheet.assignmentRevision, choices,
        personalizeAccessIdentity: personalize, previewToken: apply ? preview?.previewToken : undefined,
      })
      const data = await response.json().catch(() => null)
      if (!response.ok) {
        if (response.status === 409) { setPreview(null); setConfirmed(false) }
        throw new Error(data?.error ?? `Could not ${apply ? 'apply' : 'preview'} the assignment.`)
      }
      if (apply) {
        const result = data as Applied
        setStaged({}); setChooser(null); setPersonalize(false); setPreview(null); setConfirmed(false)
        setNotice(`Host assignment updated to ${result.tableSetName}. No download has been queued.`)
        onApplied(result); setReload(value => value + 1)
      } else setPreview(data as Preview)
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not complete the configuration assignment.') }
    finally { setBusy(false) }
  }

  const editable = !!worksheet?.canUpdate && terminalActive
  const families = [...new Set(worksheet?.records.map(row => row.family) ?? [])].sort()
  const needle = filter.trim().toLowerCase()
  const rows = (worksheet?.records ?? []).filter(row => (family === 'all' || row.family === family) && (!needle ||
    `${row.number} MT${row.number} ${row.name} ${row.family} ${row.assigned?.configurationId ?? ''}`.toLowerCase().includes(needle)))
  const stagedCount = Object.keys(staged).length

  return <details className="phone-configuration-worksheet" onToggle={event => setOpen(event.currentTarget.open)}>
    <summary>Individual configuration assignments</summary>
    {open && <>
      <p>Look up saved configurations by ID and change selected records without replacing unrelated phone settings. This changes the Manager assignment only. A download is a separate action.</p>
      {notice && <p role="status" className="worksheet-notice">{notice}</p>}
      {error && <p role="alert">{error}</p>}
      {!worksheet && !error && <p role="status">Loading configuration worksheet…</p>}
      <button type="button" className="quiet" disabled={busy} onClick={() => setReload(value => value + 1)}>Reload worksheet</button>
      {worksheet && <>
        {!worksheet.baseSetId && <p role="note">No configuration is assigned. Select published records to build the initial host assignment.</p>}
        <FamilyReviewNotice review={worksheet.family} />
        <div className="worksheet-controls"><label>Configuration family<select value={family} onChange={event => setFamily(event.target.value)}>
          <option value="all">All families</option>{families.map(name => <option key={name}>{name}</option>)}</select></label>
          <label>Find configuration row<input type="search" value={filter} onChange={event => setFilter(event.target.value)} /></label>
          {editable && <span role="status">{stagedCount} staged changes</span>}
        </div>
        <div className="worksheet-table-scroll"><table><caption>Assigned configuration records</caption>
          <thead><tr><th>Family / record</th><th>Assigned configuration</th><th>Pending action</th><th>Lookup / edit</th></tr></thead>
          <tbody>{rows.map(row => {
            const selection = staged[row.number]
            return <tr key={row.number}>
              <td><small>{row.family}{row.familyRequired ? ' · required family' : ''}</small><strong>MT{row.number}: {row.name}</strong></td>
              <td>{row.ambiguous ? <span>Multiple records, resolve in the set</span> : row.assigned ? <>
                {canOpenRecords ? <Link to={`/table-sets/${row.assigned.setId}?record=${row.number}`}>{recordLabel(row.assigned)}</Link> : <span>{recordLabel(row.assigned)}</span>}
                <small>{row.assigned.setName} · {row.assigned.length} bytes</small>
              </> : <span>Not assigned</span>}</td>
              <td>{selection?.action === 'copy' ? <><strong>Assign {recordLabel(selection.source)}</strong><small>{selection.source.setName}</small></>
                : selection?.action === 'clear' ? <strong>Clear optional assignment</strong> : 'Leave unchanged'}</td>
              <td><div className="worksheet-actions">
                {row.canChoose && !row.ambiguous && <button type="button" className="quiet" disabled={busy} onClick={() => selectRow(row)}>Look up MT{row.number}</button>}
                {editable && row.canClear && <button type="button" className="quiet" disabled={busy || selection?.action === 'clear'} onClick={() => stage(row.number, { action: 'clear' })}>Clear MT{row.number}</button>}
                {editable && selection && <button type="button" className="quiet" disabled={busy} onClick={() => stage(row.number)}>Keep MT{row.number}</button>}
              </div></td>
            </tr>
          })}</tbody>
        </table></div>
        {rows.length === 0 && <p>No matching configuration rows.</p>}

        {chooser && <section aria-label={`Configuration lookup for MT${chooser.number}`} className="worksheet-lookup">
          <h3>Published configurations for MT{chooser.number}</h3>
          <div className="worksheet-controls">
            <label>Configuration ID or set name<input type="search" value={search} maxLength={120} disabled={busy} onChange={event => setSearch(event.target.value)}
              onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); setQuery(search.trim()); setPage(1); setOptionReload(value => value + 1) } }} /></label>
            <button type="button" disabled={busy} onClick={() => { setQuery(search.trim()); setPage(1); setOptionReload(value => value + 1) }}>Find configurations</button>
            <button type="button" className="quiet" onClick={() => setChooser(null)}>Close lookup</button>
          </div>
          {optionError && <p role="alert">{optionError}</p>}
          {!options && !optionError && <p role="status">Finding compatible published configurations…</p>}
          {options && <>
            <div className="worksheet-table-scroll"><table><caption>Published record choices</caption>
              <thead><tr><th>Configuration</th><th>Source set / firmware</th><th>Compatibility</th><th>Action</th></tr></thead>
              <tbody>{options.records.map(option => <tr key={option.id}>
                <td>{recordLabel(option)}<small>{option.length} bytes</small></td>
                <td>{canOpenRecords ? <Link to={`/table-sets/${option.tableSetId}?record=${chooser.number}`}>{option.setName}</Link> : option.setName}<small>{option.targetFirmware ?? 'Untargeted'}</small></td>
                <td>{option.issue ?? 'Eligible for preview'}</td><td>{editable && <button type="button" disabled={!!option.issue || busy} onClick={() => stage(chooser.number, { action: 'copy', source: option })}>Choose {option.configurationId ?? option.setName}</button>}</td>
              </tr>)}</tbody>
            </table></div>
            {options.records.length === 0 && <p>No matching published configurations.</p>}
            <div className="worksheet-actions"><button type="button" disabled={page <= 1 || busy} onClick={() => setPage(value => value - 1)}>Previous choices</button>
              <span>{options.total} matches, page {options.page}</span><button type="button" disabled={page * options.pageSize >= options.total || busy} onClick={() => setPage(value => value + 1)}>Next choices</button></div>
          </>}
        </section>}

        {editable && <div className="worksheet-review-actions">
          {staged[21]?.action === 'copy' && <label><input type="checkbox" checked={personalize} disabled={busy} onChange={event => { setPersonalize(event.target.checked); clearPreview() }} /> Use this phone’s registered wire ID in the copied MT21</label>}
          <button type="button" disabled={busy || stagedCount === 0} onClick={() => void execute(false)}>Preview configuration assignment</button>
          {stagedCount > 0 && <button type="button" className="quiet" disabled={busy} onClick={() => { setStaged({}); setPersonalize(false); clearPreview() }}>Discard staged changes</button>}
        </div>}
        {preview && <section className="worksheet-lookup" aria-label="Configuration assignment preview">
          <h3>Review the resulting assignment</h3>
          <p>Unselected records are retained. Required source dependencies appear even when not explicitly selected.</p>
          <div className="worksheet-table-scroll"><table><caption>Proposed record changes</caption>
            <thead><tr><th>Record</th><th>Action</th><th>Current</th><th>Result</th></tr></thead>
            <tbody>{preview.changes.map(change => <tr key={change.tableNumber}>
              <td>MT{change.tableNumber}</td><td>{actionLabels[change.action] ?? change.action}{change.required && <small>Required dependency</small>}{change.personalized && <small>Registered wire ID applied</small>}{change.warning && <p role="alert">{change.warning}</p>}</td>
              <td>{recordLabel(change.current)}</td><td>{recordLabel(change.result)}</td>
            </tr>)}</tbody>
          </table></div>
          <FamilyReviewNotice review={preview.family} />
          {preview.canApply ? <div className="worksheet-review-actions">
            <label><input type="checkbox" checked={confirmed} disabled={busy} onChange={event => setConfirmed(event.target.checked)} /> I reviewed the resulting host assignment.</label>
            <button type="button" disabled={busy || !confirmed} onClick={() => void execute(true)}>Apply configuration assignment</button>
          </div> : <p>No changes to apply.</p>}
        </section>}
      </>}
    </>}
  </details>
}
