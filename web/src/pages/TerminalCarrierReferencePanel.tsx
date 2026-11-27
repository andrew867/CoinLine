import { useEffect, useState } from 'react'
import { apiFetch } from '../api/client'

type CarrierReference = { recordNumber: number; label: string; use: 'configured' | 'conditional' | 'retained' | 'unreviewed' }
type CarrierReview = { reference: number; setRevision: number; profile: string | null; review: {
  supported: boolean; reason: string | null; matchingRows: { row: number; acceptedCardRanges: number[] }[];
  references: CarrierReference[]; reviewedRecords: number[]; missingRecords: number[]; unreviewedRecords: number[]; warnings: string[] } }

const labels: Record<CarrierReference['use'], string> = {
  configured: 'Configured reference', conditional: 'Conditional reference', retained: 'Retained setting', unreviewed: 'Needs manual review',
}

export function TerminalCarrierReferencePanel({ setId, reference, dirty = false }: { setId: string; reference: number; dirty?: boolean }) {
  const [request, setRequest] = useState(0)
  const [result, setResult] = useState<CarrierReview | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  useEffect(() => {
    if (!request) return
    const abort = new AbortController()
    setLoading(true); setResult(null); setError('')
    void apiFetch(`/api/tables/sets/${setId}/carriers/${reference}/references`, { signal: abort.signal })
      .then(async response => {
        if (!response.ok) throw new Error(`Could not review carrier references (HTTP ${response.status}).`)
        const data = await response.json() as CarrierReview
        if (!abort.signal.aborted) setResult(data)
      }).catch(reason => {
        if (!abort.signal.aborted) setError(reason instanceof Error ? reason.message : 'Could not review carrier references.')
      }).finally(() => { if (!abort.signal.aborted) setLoading(false) })
    return () => abort.abort()
  }, [setId, reference, request])

  const review = result?.review
  const format = (numbers: number[]) => numbers.map(number => `MT${number}`).join(', ')
  return <section className="table-config-panel" aria-label="Terminal carrier references">
    <h3>What uses carrier reference {reference}?</h3>
    <p>Default selections, card ranges and free Feature Group B routes use the saved carrier identifier, not the physical carrier row number.</p>
    <button type="button" disabled={loading} onClick={() => setRequest(value => value + 1)}>{loading ? 'Reviewing…' : result ? 'Refresh carrier references' : 'Review carrier references'}</button>
    {dirty && <p role="note">Unsaved edits are not included. This review uses the original saved carrier identifier.</p>}
    {error && <p role="alert">{error}</p>}
    {review && !review.supported && <p role="status">{review.reason}</p>}
    {result && review?.supported && <div role="region" aria-label="Saved carrier reference review">
      <p className="page-intro">Saved configuration revision {result.setRevision}. {result.profile}.</p>
      {review.matchingRows.map(row => <p key={row.row}>Carrier row {row.row}: {row.acceptedCardRanges.length ? `Acceptance bits for card ranges ${row.acceptedCardRanges.join(', ')}` : 'No card acceptance bits set'}.</p>)}
      {review.references.length === 0 && <p>No recognized references in the saved, compatible records. This does not establish that the carrier is unused.</p>}
      {review.references.length > 0 && <ul>{review.references.map((item, index) => <li key={index}>MT{item.recordNumber}: {item.label}. {labels[item.use]}.</li>)}</ul>}
      {review.reviewedRecords.length > 0 && <p>Reviewed: {format(review.reviewedRecords)}</p>}
      {review.missingRecords.length > 0 && <p>Not present here: {format(review.missingRecords)}. These records may already be installed or may be optional.</p>}
      {review.unreviewedRecords.length > 0 && <p role="alert">Incompatible, invalid or duplicate records not reviewed: {format(review.unreviewedRecords)}.</p>}
      {review.warnings.length > 0 && <ul>{review.warnings.map(warning => <li key={warning}>{warning}</li>)}</ul>}
      <p className="page-intro">This is a saved configuration review, not a call simulation. Changing an identifier does not rewrite these other records.</p>
    </div>}
  </section>
}
