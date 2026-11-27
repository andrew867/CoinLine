import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'

type Review = {
  supported: boolean; reason: string | null
  findings: { tableNumber: number; entry: string; interpretation: string; rateIndex: number | null }[]
  tariffs: { index: number; classification: string; initialSeconds: number; initialCents: number; additionalSeconds: number; additionalCents: number }[]
  reviewedRecords: number[]; missingRecords: number[]; unreviewedRecords: number[]; warnings: string[]
}
type Result = { setRevision: number; profile: string | null; review: Review }

const list = (records: number[]) => records.length ? records.map(number => `MT${number}`).join(', ') : 'None'
const period = (seconds: number) => seconds === -1 ? 'unlimited' : `${seconds} seconds`

export function DialedNumberReview({ setId, recordRevision }: { setId: string; recordRevision: string }) {
  const [digits, setDigits] = useState('')
  const [internationalDigits, setInternationalDigits] = useState('')
  const [result, setResult] = useState<Result | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const active = useRef<AbortController | null>(null)
  function clear() {
    active.current?.abort(); active.current = null
    setResult(null); setError(''); setBusy(false)
  }
  useEffect(() => { clear(); return () => active.current?.abort() }, [setId, recordRevision])

  async function run() {
    clear()
    const controller = new AbortController()
    active.current = controller
    setBusy(true)
    try {
      const response = await apiFetch(`/api/tables/sets/${setId}/dialed-number-review`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ digits, internationalDigits: internationalDigits || undefined }), signal: controller.signal,
      })
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? `Configuration review failed (HTTP ${response.status}).`)
      const saved = await response.json() as Result
      if (!controller.signal.aborted) setResult(saved)
    } catch (failure) {
      if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : 'Configuration review failed.')
    } finally {
      if (!controller.signal.aborted) setBusy(false)
    }
  }

  return <details className="dialed-number-review">
    <summary>Review a dialed number across saved tables</summary>
    <p>Check exact supplied digits against ordinary screening patterns, local exchange and area-code settings, referenced terminal tariffs and rate-response payment configuration. Review ten- and eleven-digit forms separately.</p>
    <p>For international set-based rating, optionally enter the destination digits after the access prefix. The review lists all matching MT151 prefixes and the configured fallback without guessing prefix removal or precedence.</p>
    <p>This is a saved-configuration inspection, not a host rating quote or a prediction of terminal behavior. It does not evaluate the linked dialing graph, translation, screening precedence, service restrictions or payment eligibility. Unsaved edits are not included.</p>
    <form onSubmit={event => { event.preventDefault(); void run() }} className="row-actions">
      <label>Test dialed digits <input value={digits} onChange={event => { clear(); setDigits(event.target.value) }} inputMode="numeric" maxLength={16} pattern="[0-9]{1,16}" required autoComplete="off" /></label>
      <label>International destination after access prefix (optional)<input value={internationalDigits} onChange={event => { clear(); setInternationalDigits(event.target.value) }}
        inputMode="numeric" maxLength={16} pattern="[0-9]{1,16}" autoComplete="off" /></label>
      <button type="submit" disabled={busy || !/^[0-9]{1,16}$/.test(digits) || !!internationalDigits && !/^[0-9]{1,16}$/.test(internationalDigits)}>{busy ? 'Reviewing…' : 'Review saved configuration'}</button>
    </form>
    {error && <p role="alert">{error}</p>}
    {result && <div aria-live="polite">
      <p>Saved revision {result.setRevision}{result.profile ? ` · ${result.profile}` : ''}</p>
      {canPerform('rating', op.read) && canPerform('rate-plans', op.read) &&
        <p><Link to="/rating-quote" state={{ source: 'saved-configuration', dialedDigits: digits }}>Compare a published host quote for these digits</Link> (opens the separate quote tool; does not place a call).</p>}
      {!result.review.supported ? <p role="status">{result.review.reason}</p> : <>
        <table><caption>Configured candidates and payment settings</caption><thead><tr><th>Record</th><th>Entry</th><th>Configured value</th></tr></thead>
          <tbody>{result.review.findings.map((finding, index) => <tr key={`${finding.tableNumber}-${index}`}>
            <td>MT{finding.tableNumber}</td><td>{finding.entry}</td><td>{finding.interpretation}</td>
          </tr>)}</tbody></table>
        {result.review.tariffs.length > 0 && <table><caption>Referenced MT73 tariff rows (not selected call price)</caption><thead><tr><th>Rate</th><th>Class</th><th>Initial</th><th>Additional</th></tr></thead>
          <tbody>{result.review.tariffs.map(tariff => <tr key={tariff.index}><td>{tariff.index}</td><td>{tariff.classification}</td>
            <td>{tariff.initialCents} cents / {period(tariff.initialSeconds)}</td><td>{tariff.additionalCents} cents / {period(tariff.additionalSeconds)}</td></tr>)}</tbody></table>}
        {result.review.warnings.length > 0 && <ul>{result.review.warnings.map(warning => <li key={warning}>{warning}</li>)}</ul>}
        <p>Reviewed: {list(result.review.reviewedRecords)}<br />Missing: {list(result.review.missingRecords)}<br />Not reviewed: {list(result.review.unreviewedRecords)}</p>
      </>}
    </div>}
  </details>
}
