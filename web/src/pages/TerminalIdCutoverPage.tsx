import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, apiGet } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import './TableSetConfiguration.css'

const scopes = {
  all: 'All phones', terminal: 'Individual phone', group: 'Phone group',
  site: 'Location', customer: 'Customer', region: 'Region', 'time-area': 'Time area',
  'group-pattern': 'Group ID pattern',
} as const
type Scope = keyof typeof scopes
type ScopeOption = { id: string; name?: string; displayName?: string; code?: string }
type Target = {
  id: string; displayName: string; currentId: string; replacementId: string | null
  tableSetId: string | null; tableSetName: string | null; tableSetRevision: number | null
  accessRevision: number | null; accessLength: number | null
  sourceHash: string | null; replacementHash: string | null
  eligible: boolean; issues: string[]
}
type Preview = { targets: Target[]; eligibleCount: number; blockedCount: number; scheduled: false; reviewToken?: string }
type JobItem = {
  id: string; jobId: string; terminalId: string; sourceWireId: string; targetWireId: string; dueAtUtc: string
  state: string; revision: number; stagedTableSetId: string | null; dispatchBatchId: string | null
  dispatchSessionId: string | null; applicationAcknowledgedAtUtc: string | null
  newIdentityObservedAtUtc: string | null; activationSessionId: string | null; activatedAtUtc: string | null
  transferError: string | null; activationError: string | null; canCancel: boolean
}
type Recovery = {
  revision: number; assignmentId: string | null; assignmentRevision: number | null
  currentSetId: string | null; currentSetName: string | null; currentIssue: string | null
  stagedSetId: string | null; stagedSetName: string | null; stagedIssue: string | null
}
const stateLabels: Record<string, string> = {
  scheduled: 'Scheduled, awaiting ordinary call-in',
  blocked: 'Blocked before delivery',
  dispatching: 'Preparing MT21 delivery',
  awaiting_application_ack: 'Awaiting table acknowledgement',
  awaiting_new_identity: 'MT21 acknowledged; awaiting new ID',
  uncertain: 'Delivery uncertain; replacement reserved',
  not_confirmed: 'Transfer ended before record delivery',
  activation_blocked: 'New ID observed; reconciliation required',
  activated: 'New identity confirmed',
  cancelled: 'Cancelled before possible application',
}
const timeLabel = (value: string) => new Date(value).toLocaleString()

export function canReviewTerminalIdCutover() {
  return canPerform('phones', op.read) && canPerform('table-assignments', op.read) && canPerform('table-records', op.read)
}

export function TerminalIdCutoverPage() {
  const allowed = canReviewTerminalIdCutover()
  const canManage = allowed && canPerform('phones', op.update) &&
    canPerform('table-assignments', op.update) && canPerform('downloads', op.add)
  const [source, setSource] = useState('')
  const [replacement, setReplacement] = useState('')
  const [scope, setScope] = useState<Scope>('all')
  const [scopeValue, setScopeValue] = useState('')
  const [options, setOptions] = useState<ScopeOption[]>([])
  const [scopeError, setScopeError] = useState('')
  const [preview, setPreview] = useState<Preview | null>(null)
  const [error, setError] = useState('')
  const [selected, setSelected] = useState<string[]>([])
  const [due, setDue] = useState('')
  const [notice, setNotice] = useState('')
  const [jobsOpen, setJobsOpen] = useState(false)
  const [jobs, setJobs] = useState<JobItem[] | null>(null)
  const [jobsError, setJobsError] = useState('')
  const [jobsRefresh, setJobsRefresh] = useState(0)
  const [jobBusy, setJobBusy] = useState(false)
  const [recovery, setRecovery] = useState<{ item: JobItem; review: Recovery } | null>(null)
  const [recoveryChoice, setRecoveryChoice] = useState('')
  const [busy, setBusy] = useState(false)
  const needsOptions = scope !== 'all' && scope !== 'group-pattern'

  useEffect(() => {
    if (!allowed || !needsOptions) return
    let active = true
    void apiGet<ScopeOption[]>('/api/tables/scope-options?targetType=' + scope)
      .then(data => { if (active) setOptions(data) })
      .catch(() => { if (active) setScopeError('Could not load inventory scope options.') })
    return () => { active = false }
  }, [allowed, needsOptions, scope])

  useEffect(() => {
    if (!allowed || !jobsOpen) return
    let active = true
    setJobs(null)
    setJobsError('')
    void apiGet<JobItem[]>('/api/terminals/identity-cutover/jobs')
      .then(data => { if (active) setJobs(data) })
      .catch(() => { if (active) setJobsError('Could not load terminal ID cutover jobs.') })
    return () => { active = false }
  }, [allowed, jobsOpen, jobsRefresh])

  const valid = /^(?:[0-9]{6}|[0-9]{10})$/.test(source) &&
    /^[0-9]+$/.test(replacement) && replacement.length === source.length && replacement !== source &&
    (scope === 'all' || Boolean(scopeValue))

  function clearPreview() { setPreview(null); setSelected([]); setError(''); setNotice('') }

  async function review(event: FormEvent) {
    event.preventDefault()
    if (!valid || !allowed || busy) return
    clearPreview()
    setBusy(true)
    try {
      const query = new URLSearchParams({ source, replacement, scope })
      if (scope !== 'all') {
        const field = scope === 'region' ? 'regionCode' : scope === 'time-area' ? 'timeAreaCode'
          : scope === 'group-pattern' ? 'groupPattern' : 'targetId'
        query.set(field, scopeValue)
      }
      const response = await apiFetch('/api/terminals/identity-cutover/preview?' + query)
      if (!response.ok) {
        const detail = await response.json().catch(() => null)
        throw new Error(detail?.error || 'Could not review terminal IDs (HTTP ' + response.status + ').')
      }
      const data = await response.json() as Preview
      setPreview(data)
      setSelected(data.targets.filter(row => row.eligible).map(row => row.id))
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Could not review terminal IDs.')
    } finally {
      setBusy(false)
    }
  }

  async function schedule() {
    if (!canManage || !preview?.reviewToken || selected.length === 0 || busy) return
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const mapping = { source, replacement, scope,
        ...(scope === 'region' ? { regionCode: scopeValue } : scope === 'time-area' ? { timeAreaCode: scopeValue }
          : scope === 'group-pattern' ? { groupPattern: scopeValue } : scope === 'all' ? {} : { targetId: scopeValue }) }
      const response = await apiFetch('/api/terminals/identity-cutover/jobs', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ mapping, terminalIds: selected, reviewToken: preview.reviewToken,
          dueAtUtc: due ? new Date(due).toISOString() : null }),
      })
      if (!response.ok) {
        const detail = await response.json().catch(() => null)
        throw new Error(detail?.error || 'Could not schedule the cutover (HTTP ' + response.status + ').')
      }
      const result = await response.json() as { items: JobItem[] }
      setPreview(null); setSelected([]); setDue('')
      setNotice('Scheduled ' + result.items.length + ' phone(s). Existing identities remain registered until a new-ID contact is confirmed.')
      setJobsOpen(true); setJobsRefresh(value => value + 1)
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Could not schedule the cutover.')
    } finally { setBusy(false) }
  }

  async function cancel(item: JobItem) {
    if (!canManage || !item.canCancel || jobBusy) return
    setJobBusy(true); setJobsError('')
    try {
      const response = await apiFetch('/api/terminals/identity-cutover/jobs/' + item.jobId + '/items/' + item.id + '/cancel', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ revision: item.revision }),
      })
      if (!response.ok) {
        const detail = await response.json().catch(() => null)
        throw new Error(detail?.error || 'Could not cancel this cutover.')
      }
      setJobsRefresh(value => value + 1)
    } catch (failure) {
      setJobsError(failure instanceof Error ? failure.message : 'Could not cancel this cutover.')
    } finally { setJobBusy(false) }
  }

  const jobPath = (item: JobItem) => '/api/terminals/identity-cutover/jobs/' + item.jobId + '/items/' + item.id

  async function reviewRecovery(item: JobItem) {
    if (jobBusy || !canManage) return
    setJobBusy(true); setJobsError(''); setRecovery(null); setRecoveryChoice('')
    try {
      setRecovery({ item, review: await apiGet<Recovery>(jobPath(item) + '/recovery') })
    } catch {
      setJobsError('Could not load the current assignment and replacement recovery choices.')
    } finally { setJobBusy(false) }
  }

  async function reconcile() {
    if (!recovery || jobBusy || !canManage ||
        (recoveryChoice === 'staged' ? Boolean(recovery.review.stagedIssue) :
          recoveryChoice === 'keep-current' ? Boolean(recovery.review.currentIssue) : true)) return
    setJobBusy(true); setJobsError('')
    try {
      const response = await apiFetch(jobPath(recovery.item) + '/reconcile', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ choice: recoveryChoice, revision: recovery.review.revision,
          assignmentId: recovery.review.assignmentId, assignmentRevision: recovery.review.assignmentRevision }),
      })
      if (!response.ok) {
        const detail = await response.json().catch(() => null)
        throw new Error(detail?.error || 'Could not reconcile the cutover.')
      }
      setRecovery(null); setRecoveryChoice(''); setJobsRefresh(value => value + 1)
    } catch (failure) {
      setJobsError(failure instanceof Error ? failure.message : 'Could not reconcile the cutover.')
    } finally { setJobBusy(false) }
  }

  return <div>
    <p><Link to="/terminals">Back to phones</Link></p>
    <h1>Terminal ID cutover review</h1>
    <p className="page-intro">Review an exact change or an NPA-NXX prefix change across inventory. The last four digits remain unchanged for prefix changes.</p>
    {!allowed ? <p>Read permission for Phones, Table assignments and Table records is required.</p> : <>
      <section className="table-config-panel" aria-label="Cutover mapping">
        <h2>Proposed identity change</h2>
        <p>Previewing is read-only. A scheduled cutover reserves the proposed IDs and changes only MT21 on an eligible ordinary call-in.
          The registered identity is not changed until the phone reconnects using its replacement ID.</p>
        <form className="table-config-selectors" onSubmit={event => void review(event)}>
          <label>Existing ID or prefix<input value={source} inputMode="numeric" maxLength={10} disabled={busy}
            onChange={event => { setSource(event.target.value); clearPreview() }} /></label>
          <label>Replacement ID or prefix<input value={replacement} inputMode="numeric" maxLength={10} disabled={busy}
            onChange={event => { setReplacement(event.target.value); clearPreview() }} /></label>
          <label>Inventory scope<select aria-label="Inventory scope" value={scope} disabled={busy} onChange={event => {
            setScope(event.target.value as Scope); setScopeValue(''); setOptions([]); setScopeError(''); clearPreview()
          }}>{Object.entries(scopes).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
          {needsOptions && <label>{scopes[scope]}<select aria-label={scopes[scope]} value={scopeValue} disabled={busy}
            onChange={event => { setScopeValue(event.target.value); clearPreview() }}>
            <option value="">Choose {scopes[scope].toLowerCase()}</option>
            {options.map(option => <option key={option.id} value={option.id}>
              {option.name ?? option.displayName ?? option.id}{option.code ? ' (' + option.code + ')' : ''}
            </option>)}
          </select></label>}
          {scope === 'group-pattern' && <label>Group ID pattern<input value={scopeValue} maxLength={6} disabled={busy}
            onChange={event => { setScopeValue(event.target.value); clearPreview() }} /></label>}
          <button disabled={!valid || busy}>{busy ? 'Reviewing…' : 'Preview cutover'}</button>
        </form>
        {scopeError && <p role="alert">{scopeError}</p>}
        {error && <p role="alert">{error}</p>}
        {notice && <p role="status">{notice}</p>}
      </section>
      {preview && <section className="table-config-panel" aria-label="Cutover review results">
        <h2>Inventory and MT21 review</h2>
        <p role="status">{preview.eligibleCount} eligible, {preview.blockedCount} blocked. Nothing scheduled or changed.</p>
        {!preview.targets.length ? <p>No phones match this mapping and inventory scope.</p> : <div className="table-scroll"><table>
          <thead><tr>{canManage && preview.reviewToken && <th>Select</th>}<th>Phone</th><th>Current ID</th><th>Proposed ID</th><th>Published assignment / MT21</th><th>Review</th></tr></thead>
          <tbody>{preview.targets.map(row => <tr key={row.id}>
            {canManage && preview.reviewToken && <td><input type="checkbox" aria-label={'Select ' + row.displayName}
              checked={selected.includes(row.id)} disabled={busy || !row.eligible} onChange={event => setSelected(ids =>
                event.target.checked ? [...ids, row.id] : ids.filter(id => id !== row.id))} /></td>}
            <td><Link to={'/terminals/' + row.id}>{row.displayName}</Link></td>
            <td><code>{row.currentId}</code></td><td><code>{row.replacementId ?? 'Not mappable'}</code></td>
            <td>{row.tableSetId && row.tableSetName ? <Link to={'/table-sets/' + row.tableSetId}>{row.tableSetName}</Link> : 'Unassigned'}
              {row.accessRevision != null && <p>MT21 revision {row.accessRevision}, {row.accessLength} bytes</p>}
              {row.sourceHash && row.replacementHash && <details><summary>Record fingerprints</summary>
                <p>Current <code>{row.sourceHash}</code></p><p>Proposed <code>{row.replacementHash}</code></p>
              </details>}
            </td>
            <td>{row.eligible ? <strong>Eligible for planning</strong> : <><strong>Blocked</strong><ul>
              {row.issues.map(issue => <li key={issue}>{issue}</li>)}</ul></>}</td>
          </tr>)}</tbody>
        </table></div>}
        <p>Eligibility applies only to this inventory snapshot. The server repeats the checks before reserving IDs.</p>
        {canManage && preview.reviewToken && preview.eligibleCount > 0 && <div className="table-config-selectors">
          <label>Not before (local time)<input type="datetime-local" value={due} disabled={busy}
            onChange={event => setDue(event.target.value)} /><small>Leave blank for the next eligible management call.</small></label>
          <button type="button" disabled={busy || selected.length === 0} onClick={() => void schedule()}>
            Schedule cutover for {selected.length} {selected.length === 1 ? 'phone' : 'phones'}
          </button>
        </div>}
        {!canManage && <p>Scheduling requires Phones update, Table assignments update and Downloads add permissions.</p>}
      </section>}
      <section className="table-config-panel" aria-label="Scheduled terminal ID cutovers">
        <h2>Cutover jobs</h2>
        <p>Link receipt, application acknowledgement and new identity confirmation are separate states. If a transfer may have applied,
          its destination remains reserved even after a failed call.</p>
        <div className="table-config-selectors"><button type="button" onClick={() => setJobsOpen(open => !open)}>
          {jobsOpen ? 'Hide cutover jobs' : 'Show cutover jobs'}
        </button>
        {jobsOpen && <button type="button" className="quiet" disabled={jobBusy}
          onClick={() => setJobsRefresh(value => value + 1)}>Refresh jobs</button>}</div>
        {jobsOpen && <>
          {jobsError && <p role="alert">{jobsError}</p>}
          {!jobs && !jobsError && <p>Loading cutover jobs…</p>}
          {jobs && jobs.length === 0 && <p>No cutover jobs yet.</p>}
          {jobs && jobs.length > 0 && <div className="table-scroll"><table>
            <thead><tr><th>Phone</th><th>Replacement ID</th><th>Due</th><th>Delivery and confirmation</th><th>Actions</th></tr></thead>
            <tbody>{jobs.map(item => <tr key={item.id}>
              <td><Link to={'/terminals/' + item.terminalId}><code>{item.sourceWireId}</code></Link></td>
              <td><code>{item.targetWireId}</code></td>
              <td><time dateTime={item.dueAtUtc}>{timeLabel(item.dueAtUtc)}</time></td>
              <td><strong>{stateLabels[item.state] ?? item.state}</strong>
                {item.applicationAcknowledgedAtUtc && <p>MT14: {timeLabel(item.applicationAcknowledgedAtUtc)}</p>}
                {item.newIdentityObservedAtUtc && <p>New-ID contact: {timeLabel(item.newIdentityObservedAtUtc)}</p>}
                {item.transferError && <p>{item.transferError}</p>}
                {item.activationError && <p>{item.activationError}</p>}
              </td>
              <td>{item.dispatchBatchId && <p><Link to={'/downloads/' + item.dispatchBatchId}>Download batch</Link></p>}
                {item.stagedTableSetId && <p><Link to={'/table-sets/' + item.stagedTableSetId}>Replacement set</Link></p>}
                {canManage && item.canCancel && <button type="button" disabled={jobBusy}
                  onClick={() => void cancel(item)}>Cancel cutover</button>}
                {canManage && item.state === 'activation_blocked' && <button type="button" disabled={jobBusy}
                  onClick={() => void reviewRecovery(item)}>Review reconciliation</button>}
              </td>
            </tr>)}</tbody>
          </table></div>}
          {recovery && <section className="table-config-panel" aria-label="Reconcile observed terminal ID">
            <h3>Reconcile observed ID <code>{recovery.item.targetWireId}</code></h3>
            <p>The phone has contacted Manager with the reserved ID, but the inventory or assignment changed after review.
              Choose a compatible published assignment deliberately. No additional MT21 is sent by this action.</p>
            <fieldset disabled={jobBusy}>
              <legend>Choose which assignment to retain</legend>
              <p><label><input type="radio" name="cutover-recovery" value="keep-current" checked={recoveryChoice === 'keep-current'}
                disabled={Boolean(recovery.review.currentIssue)} onChange={() => setRecoveryChoice('keep-current')} /> Keep current assignment</label>
                {' · '}{recovery.review.currentSetName ?? 'Unassigned'}
                {recovery.review.currentIssue && <small> · {recovery.review.currentIssue}</small>}</p>
              <p><label><input type="radio" name="cutover-recovery" value="staged" checked={recoveryChoice === 'staged'}
                disabled={Boolean(recovery.review.stagedIssue)} onChange={() => setRecoveryChoice('staged')} /> Use the dispatched replacement set</label>
                {' · '}{recovery.review.stagedSetName ?? 'Unavailable'}
                {recovery.review.stagedIssue && <small> · {recovery.review.stagedIssue}</small>}</p>
            </fieldset>
            <div className="table-config-selectors">
              <button type="button" disabled={jobBusy || !recoveryChoice} onClick={() => void reconcile()}>Confirm reviewed replacement</button>
              <button type="button" className="quiet" disabled={jobBusy} onClick={() => { setRecovery(null); setRecoveryChoice('') }}>Close review</button>
            </div>
          </section>}
        </>}
      </section>
    </>}
  </div>
}
