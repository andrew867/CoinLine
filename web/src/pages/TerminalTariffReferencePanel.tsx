import { useEffect, useState } from 'react'
import { apiFetch } from '../api/client'

type ReferenceGroup = { recordNumber: number; kind: string; use: 'configured' | 'conditional'; count: number; entries: string[] }
type ReferenceReview = { rateIndex: number; setRevision: number; profile: string | null; review: {
  supported: boolean; reason: string | null; groups: ReferenceGroup[]; reviewedRecords: number[]; missingRecords: number[];
  unreviewedRecords: number[]; warnings: string[] } }

export function TerminalTariffReferencePanel({ setId, rateIndex }: { setId: string; rateIndex: number }) {
  const [request, setRequest] = useState(0)
  const [result, setResult] = useState<ReferenceReview | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!request) return
    const abort = new AbortController()
    setLoading(true); setError(''); setResult(null)
    void apiFetch(`/api/tables/sets/${setId}/tariffs/${rateIndex}/references`, { signal: abort.signal })
      .then(async response => {
        if (!response.ok) throw new Error(`Could not review tariff references (HTTP ${response.status}).`)
        const data = await response.json() as ReferenceReview
        if (!abort.signal.aborted) setResult(data)
      }).catch(reason => {
        if (!abort.signal.aborted) setError(reason instanceof Error ? reason.message : 'Could not review tariff references.')
      }).finally(() => { if (!abort.signal.aborted) setLoading(false) })
    return () => abort.abort()
  }, [setId, rateIndex, request])

  const review = result?.review
  const format = (numbers: number[]) => numbers.map(number => `MT${number}`).join(', ')
  return <section className="table-config-panel" aria-label="Terminal tariff references">
    <h3>What uses tariff {rateIndex}?</h3>
    <p>Check saved records in this configuration before changing the price. Several routes, exchange ranges or keys may use the same tariff index.</p>
    <button type="button" disabled={loading} onClick={() => setRequest(value => value + 1)}>{loading ? 'Reviewing…' : result ? 'Refresh references' : 'Review references'}</button>
    {error && <p role="alert">{error}</p>}
    {review && !review.supported && <p role="status">{review.reason}</p>}
    {result && review?.supported && <div role="region" aria-label="Saved tariff reference review">
      <p className="page-intro">Saved configuration revision {result.setRevision}. {result.profile}. This is not a call simulation; a configured reference may be overridden by other dialing rules or disabled features.</p>
      {review.groups.length === 0 && <p>No recognized references in the saved, compatible records. This does not establish that the tariff is unused.</p>}
      {review.groups.map(group => <details key={`${group.recordNumber}:${group.kind}`} className="native-spare-fields">
        <summary>MT{group.recordNumber} · {group.kind} ({group.count.toLocaleString()} {group.count === 1 ? 'reference' : 'references'}{group.use === 'conditional' ? ', conditional' : ''})</summary>
        <ul>{group.entries.map((entry, index) => <li key={index}>{entry}</li>)}</ul>
      </details>)}
      {review.reviewedRecords.length > 0 && <p>Reviewed: {format(review.reviewedRecords)}</p>}
      {review.missingRecords.length > 0 && <p>Not present here: {format(review.missingRecords)}. These records may already be installed on the phone or may be optional.</p>}
      {review.unreviewedRecords.length > 0 && <p role="alert">Incompatible, invalid or duplicate records not reviewed: {format(review.unreviewedRecords)}.</p>}
      {review.warnings.length > 0 && <ul>{review.warnings.map(warning => <li key={warning}>{warning}</li>)}</ul>}
      <p className="page-intro">The review does not include unsaved edits, other configurations, unverified layouts or host-side tariffs. Review the affected dialing rules before publication.</p>
    </div>}
  </section>
}
