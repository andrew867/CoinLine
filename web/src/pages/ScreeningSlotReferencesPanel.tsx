import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api/client'

type Reference = { recordNumber: number; label: string; use: 'configured' | 'retained' | 'conditional' | 'fallback'; savedEntry: number }
type Result = { slot: number; setRevision: number; profile: string | null; review: {
  supported: boolean; reason: string | null; capacity: number | null; references: Reference[];
  reviewedRecords: number[]; missingRecords: number[]; unreviewedRecords: number[] } }

const labels: Record<Reference['use'], string> = {
  configured: 'Configured reference', retained: 'Retained, not enabled by this setting',
  conditional: 'Conditional service reference', fallback: 'Fallback to entry 1',
}

export function ScreeningSlotReferencesPanel({ setId, slot, dirty }: { setId: string; slot: number; dirty: boolean }) {
  const [request, setRequest] = useState(0)
  const [result, setResult] = useState<Result | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  useEffect(() => {
    if (!request) return
    const abort = new AbortController()
    setLoading(true); setResult(null); setError('')
    void apiFetch(`/api/tables/sets/${setId}/screening-slots/${slot}/references`, { signal: abort.signal })
      .then(async response => {
        if (!response.ok) throw new Error(`Could not review screening references (HTTP ${response.status}).`)
        const value = await response.json() as Result
        if (!abort.signal.aborted) setResult(value)
      }).catch(reason => {
        if (!abort.signal.aborted) setError(reason instanceof Error ? reason.message : 'Could not review screening references.')
      }).finally(() => { if (!abort.signal.aborted) setLoading(false) })
    return () => abort.abort()
  }, [setId, slot, request])

  const review = result?.review
  const format = (records: number[]) => records.map(number => `MT${number}`).join(', ')
  return <section className="table-config-panel" aria-label="Screening entry references">
    <h3>What uses screening entry {slot}?</h3>
    <p>Review saved operator, carrier and service references before blanking, replacing or deleting this entry.</p>
    <button type="button" disabled={loading} onClick={() => setRequest(value => value + 1)}>{loading ? 'Reviewing…' : result ? 'Refresh screening references' : 'Review screening references'}</button>
    {dirty && <p role="note">Unsaved edits are not included in this review.</p>}
    {error && <p role="alert">{error}</p>}
    {review && !review.supported && <p role="status">{review.reason}</p>}
    {result && review?.supported && <div role="region" aria-label="Saved screening reference review">
      <p className="page-intro">Saved configuration revision {result.setRevision}. {result.profile}. Entry {slot} of {review.capacity}.</p>
      {review.references.length === 0 && <p>No recognized references in these saved compatible records. This does not establish that the entry is unused.</p>}
      {review.references.length > 0 && <ul>{review.references.map((reference, index) => <li key={index}>
        <Link to={`/table-sets/${setId}?record=${reference.recordNumber}`}>MT{reference.recordNumber}</Link> · {reference.label}: {labels[reference.use]} (saved entry {reference.savedEntry})
      </li>)}</ul>}
      {review.reviewedRecords.length > 0 && <p>Reviewed: {format(review.reviewedRecords)}</p>}
      {review.missingRecords.length > 0 && <p>Not present here: {format(review.missingRecords)}. These records may already be installed or may be optional.</p>}
      {review.unreviewedRecords.length > 0 && <p role="alert">Incompatible, invalid or duplicate records not reviewed: {format(review.unreviewedRecords)}.</p>}
      <p className="page-intro">This review describes saved pointers, not actual call selection or device compatibility. It does not change any record.</p>
    </div>}
  </section>
}
