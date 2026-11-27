import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api/client'

type Review = { available: boolean; reason: string; scope?: 'full' | 'partial' | 'unavailable'; baselineId?: string | null;
  tableSetName?: string; records?: { number: number; name: string; revision: number; comparison: string; selectedForRefresh: boolean | null }[];
  previouslyAcknowledgedNotAssigned?: number[] }

const reasons: Record<string, string> = {
  no_assignment: 'No configuration is assigned to this phone.', unavailable_assignment: 'The assigned configuration could not be loaded.',
  unregistered_firmware: 'Register the firmware before the changed-record policy can be evaluated.',
  unqualified_target: 'The assigned target or its record layout requires review.',
  no_acknowledged_full_batch: 'No completed matched full download establishes an acknowledgement baseline.',
  uncertain_delivery_history: 'A later incomplete, failed or conflicting delivery makes the retained baseline uncertain.',
  ambiguous_delivery_history: 'The retained delivery history cannot establish a reliable sequence.',
  ambiguous_full_batch: 'The retained full-download sequence is ambiguous.', incomplete_full_batch: 'The baseline does not contain every record in its full set.',
  record_removed: 'At least one previously acknowledged record is not in the current assignment.',
  no_changed_records: 'All assigned bytes match the retained acknowledgements; the ordinary policy deliberately resends the full set.',
  all_records_selected: 'The changed records and their dependencies cover the full assigned set.',
  changed_records: 'Only changed records and their declared dependencies are selected.',
}
const comparisons: Record<string, string> = { same: 'Matching acknowledged bytes', different: 'Different from acknowledged bytes',
  'not-observed': 'No matching record previously acknowledged', uncertain: 'Acknowledgement comparison uncertain' }

export function TerminalDeliveryReview({ terminalId, assignmentKey }: { terminalId: string; assignmentKey: string }) {
  const [open, setOpen] = useState(false)
  const [refresh, setRefresh] = useState(0)
  const [review, setReview] = useState<Review | null>(null)
  const [error, setError] = useState('')
  useEffect(() => {
    setReview(null); setError('')
    if (!open) return
    let active = true
    const controller = new AbortController()
    void apiFetch(`/api/terminals/${terminalId}/configuration-delivery-review`, { signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error(`Delivery review failed (HTTP ${response.status}).`)
        return await response.json() as Review
      }).then(value => { if (active) setReview(value) })
      .catch(reason => { if (active) setError(reason instanceof Error ? reason.message : 'Could not load delivery review.') })
    return () => { active = false; controller.abort() }
  }, [open, terminalId, assignmentKey, refresh])

  return <details className="table-config-panel" onToggle={event => setOpen(event.currentTarget.open)}>
    <summary>Review acknowledged configuration and ordinary refresh</summary>
    <p>Compare the current assignment with completed, matched terminal acknowledgements. This is not a terminal memory readback. Scheduled or queued work, mandatory INSTALL and a change in connection history can alter the next actual transfer.</p>
    <button type="button" onClick={() => setRefresh(value => value + 1)}>Refresh delivery evidence</button>
    {error && <p role="alert">{error}</p>}
    {open && !review && !error && <p role="status">Loading delivery evidence…</p>}
    {review && <div aria-live="polite">
      <p>{reasons[review.reason] ?? review.reason}</p>
      {review.available && <>
        <p>Assigned set: {review.tableSetName}. {review.scope === 'partial' ? 'Ordinary refresh preview: selected records.' : review.scope === 'full' ? 'Ordinary refresh preview: full set.' : 'Ordinary refresh selection unavailable until reviewed.'}</p>
        {review.baselineId && <p>Retained full baseline: <Link to={`/downloads/${review.baselineId}`}>View download evidence</Link></p>}
        <table><caption>Assigned records and conditional refresh selection</caption><thead><tr><th>Record</th><th>Revision</th><th>Comparison</th><th>Ordinary refresh</th></tr></thead>
          <tbody>{review.records?.map((record, index) => <tr key={`${record.number}-${index}`}><td>MT{record.number} · {record.name}</td><td>{record.revision}</td>
            <td>{comparisons[record.comparison] ?? record.comparison}</td><td>{record.selectedForRefresh == null ? 'Review required' : record.selectedForRefresh ? 'Selected' : 'Not selected'}</td></tr>)}</tbody></table>
        {!!review.previouslyAcknowledgedNotAssigned?.length && <p>Previously acknowledged but not currently assigned: {review.previouslyAcknowledgedNotAssigned.map(number => `MT${number}`).join(', ')}. Their removal from terminal memory is not established.</p>}
      </>}
    </div>}
  </details>
}
