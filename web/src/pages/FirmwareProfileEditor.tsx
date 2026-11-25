import { useState } from 'react'
import { apiFetch } from '../api/client'

type Choice = { id: string; label: string }
type Finding = { scope: string; tableNumber: number | null; blocking: boolean; message: string; count: number }
type Review = {
  firmwareVersionId: string; revision: number; currentProfileId: string | null; proposedProfileId: string | null
  phoneCount: number; targetedSetCount: number; assignedSetCount: number; pendingBatchCount: number
  scheduledDownloadCount: number; activeSessionCount: number; activeBatchCount: number
  changed: boolean; canApply: boolean; previewToken: string; findings: Finding[]
}

export function FirmwareProfileEditor({ id, currentProfileId, profiles, onApplied, onCancel }: {
  id: string; currentProfileId: string | null; profiles: Choice[]
  onApplied: () => void; onCancel: () => void
}) {
  const [selection, setSelection] = useState(currentProfileId ?? '')
  const [review, setReview] = useState<Review | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const path = `/api/firmware/versions/${id}/table-profile`

  async function submit(apply: boolean) {
    if (apply && (!review?.canApply || review.proposedProfileId !== (selection || null))) return
    setBusy(true)
    setError(null)
    if (!apply) setReview(null)
    try {
      const response = await apiFetch(apply ? path : `${path}/preview`, {
        method: apply ? 'PUT' : 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tableProfileId: selection || null, ...(apply ? { previewToken: review!.previewToken } : {}) }),
      })
      if (!response.ok) {
        const body = await response.json().catch(() => null)
        throw new Error(body?.error ?? `Request failed (HTTP ${response.status}).`)
      }
      if (apply) onApplied()
      else setReview(await response.json() as Review)
    } catch (e) {
      setReview(null)
      setError(e instanceof Error ? e.message : 'The profile change could not be reviewed.')
    } finally {
      setBusy(false)
    }
  }

  return <section aria-label="Review firmware table profile" className="panel" style={{ padding: 12 }}>
    <p>Change the layout reference for this registered build. Phone assignments, stored table bytes and download selections are not rewritten.</p>
    <label>Proposed table layout
      <select disabled={busy} value={selection} onChange={event => {
        setSelection(event.target.value); setReview(null); setError(null)
      }}>
        <option value="">Unverified layout</option>
        {profiles.map(profile => <option key={profile.id} value={profile.id}>{profile.label}</option>)}
      </select>
    </label>
    <div className="row-actions">
      <button type="button" disabled={busy || selection === (currentProfileId ?? '')} onClick={() => void submit(false)}>Preview impact</button>
      <button type="button" disabled={busy} onClick={onCancel}>Cancel</button>
    </div>
    {error && <p role="alert">{error}</p>}
    {review && <>
      <h3>Profile impact</h3>
      <dl>
        <dt>Registered phones</dt><dd>{review.phoneCount}</dd>
        <dt>Targeted table sets</dt><dd>{review.targetedSetCount}</dd>
        <dt>Assigned table sets</dt><dd>{review.assignedSetCount}</dd>
        <dt>Queued downloads</dt><dd>{review.pendingBatchCount}</dd>
        <dt>Scheduled downloads</dt><dd>{review.scheduledDownloadCount}</dd>
        <dt>Active NCC sessions</dt><dd>{review.activeSessionCount}</dd>
        <dt>Active downloads</dt><dd>{review.activeBatchCount}</dd>
      </dl>
      {review.findings.length === 0 ? <p>No known incompatibilities found. This is not terminal behavior certification.</p> :
        <table>
          <thead><tr><th>Scope</th><th>Record</th><th>Count</th><th>Result</th><th>Finding</th></tr></thead>
          <tbody>{review.findings.map(finding => <tr key={JSON.stringify(finding)}>
            <td>{finding.scope}</td><td>{finding.tableNumber == null ? 'Not applicable' : `MT${finding.tableNumber}`}</td>
            <td>{finding.count}</td><td>{finding.blocking ? 'Blocking' : 'Advisory'}</td><td>{finding.message}</td>
          </tr>)}</tbody>
        </table>}
      {!review.canApply && <p role="alert">The profile change cannot be applied. Resolve the blocking findings or choose another layout.</p>}
      <button type="button" disabled={busy || !review.canApply || !review.changed} onClick={() => void submit(true)}>Apply reviewed profile</button>
    </>}
  </section>
}
