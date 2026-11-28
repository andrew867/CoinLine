import { useEffect, useState } from 'react'
import { apiFetch } from '../api/client'

type DiscountReference = { recordNumber: number; label: string; use: 'configured' | 'retained' | 'unreviewed'; savedIndex: number; payment: string | null }
type DiscountReview = { discount: number; setRevision: number; profile: string | null; review: {
  supported: boolean; reason: string | null; references: DiscountReference[]; reviewedRecords: number[];
  missingRecords: number[]; unreviewedRecords: number[] } }

export function TerminalDiscountReferencePanel({ setId, discount, dirty = false }: { setId: string; discount: number; dirty?: boolean }) {
  const [request, setRequest] = useState(0)
  const [result, setResult] = useState<DiscountReview | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!request) return
    const abort = new AbortController()
    setLoading(true); setError(''); setResult(null)
    void apiFetch(`/api/tables/sets/${setId}/discounts/${discount}/references`, { signal: abort.signal })
      .then(async response => {
        if (!response.ok) throw new Error(`Could not review discount references (HTTP ${response.status}).`)
        const data = await response.json() as DiscountReview
        if (!abort.signal.aborted) setResult(data)
      }).catch(reason => {
        if (!abort.signal.aborted) setError(reason instanceof Error ? reason.message : 'Could not review discount references.')
      }).finally(() => { if (!abort.signal.aborted) setLoading(false) })
    return () => abort.abort()
  }, [setId, discount, request])

  const review = result?.review
  const format = (numbers: number[]) => numbers.map(number => `MT${number}`).join(', ')
  const state = { configured: 'Configured reference', retained: 'Retained but inactive', unreviewed: 'Invalid time window' }
  return <section className="table-config-panel" aria-label="Terminal discount references">
    <h3>What uses discount {discount}?</h3>
    <p>Review saved card acceptance ranges and scheduled discount periods before changing this rate. The two records store the same discount number differently.</p>
    <button type="button" disabled={loading} onClick={() => setRequest(value => value + 1)}>{loading ? 'Reviewing…' : result ? 'Refresh references' : 'Review references'}</button>
    {dirty && <p role="note">Unsaved edits are not included in this review.</p>}
    {error && <p role="alert">{error}</p>}
    {review && !review.supported && <p role="status">{review.reason}</p>}
    {result && review?.supported && <div role="region" aria-label="Saved discount reference review">
      <p className="page-intro">Saved configuration revision {result.setRevision}. {result.profile}.</p>
      {review.references.length === 0 && <p>No recognized references in the saved, compatible records. This does not establish that the discount is unused.</p>}
      {review.references.length > 0 && <ul>{review.references.map(row => <li key={`${row.recordNumber}:${row.label}`}>
        <strong>MT{row.recordNumber}: {row.label}</strong><br />
        {state[row.use]}. Saved {row.recordNumber === 134 ? 'one-based number' : 'zero-based index'}: {row.savedIndex}.
        {row.payment && ` Payment: ${row.payment}.`}
      </li>)}</ul>}
      {review.reviewedRecords.length > 0 && <p>Reviewed: {format(review.reviewedRecords)}</p>}
      {review.missingRecords.length > 0 && <p>Not present here: {format(review.missingRecords)}. These records may already be installed on the phone or may be optional.</p>}
      {review.unreviewedRecords.length > 0 && <p role="alert">Incompatible, invalid or duplicate records not reviewed: {format(review.unreviewedRecords)}.</p>}
      <p className="page-intro">The review excludes unsaved edits, other configurations and unverified layouts. Configured references do not prove that a card is accepted or a scheduled period applies to a particular call.</p>
    </div>}
  </section>
}
