import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { apiGet, apiPost } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { ErrorBanner } from '../components/AppStates'
import './TableSetConfiguration.css'

type Settings = {
  startDate: string | null; startTime: string | null
  intervalDays: number; intervalHours: number; intervalMinutes: number
  retryMinutes: number; retrySeconds: number; recordThreshold: number
  adjustmentAt: string | null; adjustmentScheduled: boolean; adjustmentSeconds: number; findings: string[]
}
type RecordReview = {
  status: string; tableSetId?: string | null; tableSetName?: string | null; tableRevision?: number | null
  sha256?: string | null; settings?: Settings | null; downloadBatchId?: string | null; acknowledgedAtUtc?: string | null
}
type Review = {
  assigned: RecordReview; acknowledged: RecordReview; assignmentMatchesAcknowledgement: boolean | null
  laterUncertainAttempt: boolean; pendingBatches: number | null
  contact: { status: string; sessionId?: string | null; receivedAtUtc?: string | null }
}

const statusText: Record<string, string> = {
  unassigned: 'No assigned configuration', missing: 'No MT35 record available', ambiguous: 'Multiple competing records',
  invalid: 'Unsupported or damaged record', restricted: 'Not available with your permissions',
  assigned: 'Manager assignment', acknowledged: 'Application acknowledgement retained',
}
const showTime = (value?: string | null) => value ? new Date(value).toLocaleString() : 'Not recorded'
const repeat = (value: Settings) => value.intervalDays === 0 && value.intervalHours === 0 && value.intervalMinutes === 0
  ? 'Daily (zero interval)' : `${value.intervalDays} days, ${value.intervalHours} h, ${value.intervalMinutes} min`
const displayAdjustment = (value: Settings) => !value.adjustmentScheduled ? 'Disabled'
  : value.adjustmentAt ? `${value.adjustmentAt.replace('T', ' ')} (${value.adjustmentSeconds} seconds)` : 'Invalid stored adjustment'
const rows: { label: string; render: (value: Settings) => string }[] = [
  { label: 'Start date (two-digit year)', render: value => value.startDate ?? 'Not set or invalid' },
  { label: 'Routine time (phone clock)', render: value => value.startTime ?? 'Invalid time' },
  { label: 'Repeat interval', render: repeat },
  { label: 'Retry delay', render: value => `${value.retryMinutes} min, ${value.retrySeconds} sec` },
  { label: 'Call-record upload threshold', render: value => String(value.recordThreshold) },
  { label: 'Scheduled clock adjustment', render: displayAdjustment },
]

export function RoutineCallInPanel({ terminalId, assignmentKey, terminalActive }: {
  terminalId: string; assignmentKey: string; terminalActive: boolean
}) {
  const navigate = useNavigate()
  const [review, setReview] = useState<Review | null>(null)
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)
  const [copyName, setCopyName] = useState('')
  const [copying, setCopying] = useState(false)
  const canOpenSet = canPerform('table-sets', op.read)
  const canCopy = canOpenSet && canPerform('table-sets', op.add) && canPerform('table-records', op.update)

  useEffect(() => {
    let active = true
    setReview(null)
    setError('')
    void apiGet<Review>(`/api/terminals/${terminalId}/routine-call-in`).then(value => {
      if (!active) return
      setReview(value)
      setCopyName(value.assigned.tableSetName ? `${value.assigned.tableSetName} - routine call-in` : '')
    }).catch(reason => { if (active) setError(reason instanceof Error ? reason.message : 'Could not read routine call-in settings.') })
    return () => { active = false }
  }, [terminalId, assignmentKey, reload])

  async function makeCopy(event: React.FormEvent) {
    event.preventDefault()
    if (!review?.assigned.tableSetId || !copyName.trim()) return
    setCopying(true)
    setError('')
    try {
      const copy = await apiPost<{ id: string }>(`/api/tables/sets/${review.assigned.tableSetId}/clone`, { name: copyName.trim() })
      navigate(`/table-sets/${copy.id}?record=35&schedulePhone=${encodeURIComponent(terminalId)}&scheduleRecord=35#config-heading`)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not create a configuration copy.')
    } finally { setCopying(false) }
  }

  return <section className="table-config-panel" aria-labelledby="routine-call-in-heading">
    <h3 id="routine-call-in-heading">Routine call-in details</h3>
    <p>MT35 controls routine contact, retry timing and clock adjustment. The assigned record is the Manager's intended configuration; an application ACK records delivery, not the observed behavior of the phone clock.</p>
    {error && <ErrorBanner message={error} />}
    {!review && !error && <p role="status">Loading routine call-in settings…</p>}
    {!review && error && <button type="button" onClick={() => setReload(value => value + 1)}>Retry call-in review</button>}
    {review && <>
      <div className="row-actions">
        <button type="button" onClick={() => setReload(value => value + 1)}>Refresh call-in review</button>
        {review.assigned.tableSetId && canOpenSet && <Link to={`/table-sets/${review.assigned.tableSetId}?record=35#config-heading`}>Open assigned MT35 editor</Link>}
      </div>
      <dl className="native-table-summary">
        <div><dt>Assigned MT35</dt><dd>{statusText[review.assigned.status] ?? review.assigned.status}</dd>
          {review.assigned.tableSetName && <dd>{review.assigned.tableSetName}{review.assigned.tableRevision != null && `, revision ${review.assigned.tableRevision}`}</dd>}</div>
        <div><dt>Last acknowledged MT35</dt><dd>{statusText[review.acknowledged.status] ?? review.acknowledged.status}</dd>
          {review.acknowledged.tableRevision != null && <dd>Revision {review.acknowledged.tableRevision}; {showTime(review.acknowledged.acknowledgedAtUtc)}</dd>}
          {review.acknowledged.downloadBatchId && <dd><Link to={`/downloads/${review.acknowledged.downloadBatchId}`}>Open acknowledging download</Link></dd>}</div>
        <div><dt>Last retained routine MT8 contact</dt><dd>{review.contact.status === 'observed' ? showTime(review.contact.receivedAtUtc)
          : review.contact.status === 'restricted' ? 'Not available with your permissions' : 'Not observed in retained current-ID frames'}</dd>
          {review.contact.sessionId && <dd><Link to={`/ncc-sessions?sessionId=${encodeURIComponent(review.contact.sessionId)}`}>Open contact</Link></dd>}</div>
      </dl>
      {review.assignmentMatchesAcknowledgement === false && <p role="status">The assigned and last acknowledged MT35 bytes differ.</p>}
      {review.assignmentMatchesAcknowledgement === true && <p role="status">The assigned MT35 bytes match the retained acknowledgement.</p>}
      {review.laterUncertainAttempt && <p role="alert">A later MT35 attempt has no confirmed acknowledgement. Installed settings may have changed.</p>}
      {review.pendingBatches != null && <p>{review.pendingBatches} prepared or running batch{review.pendingBatches === 1 ? '' : 'es'} with an unacknowledged MT35.</p>}
      {(review.assigned.settings || review.acknowledged.settings) && <div className="table-set-versions" role="region" aria-label="Call-in settings comparison" tabIndex={0}>
        <table><thead><tr><th>Setting</th><th>Manager assignment</th><th>Last acknowledged record</th></tr></thead>
          <tbody>{rows.map(row => <tr key={row.label}><th scope="row">{row.label}</th>
            <td>{review.assigned.settings ? row.render(review.assigned.settings) : 'Unavailable'}</td>
            <td>{review.acknowledged.settings ? row.render(review.acknowledged.settings) : 'Unavailable'}</td></tr>)}</tbody></table>
      </div>}
      {(['assigned', 'acknowledged'] as const).flatMap(source => (review[source].settings?.findings ?? []).map(finding =>
        <p role="alert" key={`${source}-${finding}`}>{source === 'assigned' ? 'Assigned record' : 'Acknowledged record'}: {finding}</p>))}
      <p>Calendar values are shown as stored, in the phone's clock. No next-contact UTC time is inferred.</p>
      {canCopy && terminalActive && review.assigned.tableSetId && <details>
        <summary>Create a separate draft for call-in changes</summary>
        <p>Copy the assigned set to edit MT35 without changing the shared configuration. The copy is not assigned or downloaded. Publish it and explicitly review this phone as the target when ready.</p>
        <form className="row-actions" onSubmit={event => void makeCopy(event)}>
          <label>Draft name<input required value={copyName} onChange={event => setCopyName(event.target.value)} /></label>
          <button type="submit" disabled={copying || !copyName.trim()}>{copying ? 'Copying…' : 'Create MT35 draft'}</button>
        </form>
      </details>}
    </>}
  </section>
}
