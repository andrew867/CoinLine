import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPostRaw } from '../api/client'
import './DownloadPreparationPanel.css'

const endpoint = '/api/downloads/preparation'
const maximumPhones = 40

type Phone = { id: string; terminalIdHex: string; displayName: string; active: boolean }
type Options = { phones: Phone[]; more: boolean; maximumPhones: number }
type Review = { terminalId: string; terminalIdHex: string | null; displayName: string | null; tableSetId: string | null;
  tableSetName: string | null; records: { tableNumber: number; versionId: string; payloadSha256Hex: string }[]; eligible: boolean; reason: string | null }
type Preview = { phones: Review[]; preparationAtUtc: string | null; previewToken: string; eligibleCount: number }
type Prepared = { terminalId: string; terminalIdHex: string; displayName: string; tableSetName: string; scheduleId: string;
  batchId: string | null; availableAtUtc: string; preparedAtUtc: string | null }

const utcLabel = (value: string) => new Date(value).toISOString().slice(0, 16).replace('T', ' ') + ' UTC'
const phoneLabel = (phone: Phone) => `${phone.displayName} (${phone.terminalIdHex})`

export function DownloadPreparationPanel({ onPrepared, showBatchLinks = true }: { onPrepared(): void; showBatchLinks?: boolean }) {
  const [search, setSearch] = useState('')
  const [submittedSearch, setSubmittedSearch] = useState('')
  const [options, setOptions] = useState<Options | null>(null)
  const [searchError, setSearchError] = useState('')
  const [searchVersion, setSearchVersion] = useState(0)
  const [selected, setSelected] = useState<Phone[]>([])
  const [timing, setTiming] = useState<'now' | 'later'>('now')
  const [dateTime, setDateTime] = useState('')
  const [preview, setPreview] = useState<Preview | null>(null)
  const [results, setResults] = useState<Prepared[] | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    let current = true
    setOptions(null)
    setSearchError('')
    void apiGet<Options>(`${endpoint}/options?query=${encodeURIComponent(submittedSearch)}`)
      .then(value => { if (current) setOptions(value) })
      .catch(() => { if (current) setSearchError('Could not search for phones.') })
    return () => { current = false }
  }, [submittedSearch, searchVersion])

  function invalidate() {
    setPreview(null)
    setError('')
    setResults(null)
  }

  function addPhone(phone: Phone) {
    setSelected(current => current.length < maximumPhones && !current.some(row => row.id === phone.id) ? [...current, phone] : current)
    invalidate()
  }

  function removePhone(id: string) {
    setSelected(current => current.filter(phone => phone.id !== id))
    invalidate()
  }

  function requestBody() {
    const availableAtUtc = timing === 'later' ? new Date(`${dateTime}Z`).toISOString() : null
    return { terminalIds: selected.map(phone => phone.id), availableAtUtc }
  }

  async function review() {
    setBusy(true)
    invalidate()
    try {
      const response = await apiPostRaw(`${endpoint}/preview`, requestBody())
      const body = await response.json().catch(() => null)
      if (!response.ok || !body) throw new Error(body?.error ?? `Could not review the phone list (HTTP ${response.status}).`)
      setPreview(body as Preview)
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not review downloads.') }
    finally { setBusy(false) }
  }

  async function prepare() {
    if (!preview || preview.eligibleCount !== selected.length) return
    setBusy(true)
    setError('')
    try {
      const response = await apiPostRaw(endpoint, { ...requestBody(), previewToken: preview.previewToken })
      const body = await response.json().catch(() => null)
      if (!response.ok || !body) throw new Error(body?.error ?? `Could not prepare the phone list (HTTP ${response.status}).`)
      setResults(body.downloads as Prepared[])
      setSelected([])
      setPreview(null)
      onPrepared()
    } catch (reason) {
      setPreview(null)
      setError(reason instanceof Error ? reason.message : 'Could not prepare downloads.')
    } finally { setBusy(false) }
  }

  return <section className="download-preparation" aria-labelledby="prepare-assigned-heading">
    <h2 id="prepare-assigned-heading">Prepare assigned configurations</h2>
    <p className="page-intro">Select up to 40 phones. Each receives its own current published configuration, not a shared replacement set. Preparation does not mean the phone has picked up the download.</p>
    <div className="preparation-panes">
      <div className="preparation-pane">
        <h3>Find phones</h3>
        <form className="row-actions" onSubmit={event => {
          event.preventDefault(); setSubmittedSearch(search.trim()); setSearchVersion(value => value + 1)
        }}>
          <label>Search by phone ID or name<input value={search} onChange={event => setSearch(event.target.value)} maxLength={100} type="search" /></label>
          <button type="submit">Search phones</button>
        </form>
        {searchError && <p role="alert">{searchError}</p>}
        {!options && !searchError && <p role="status">Searching phones…</p>}
        {options && <>
          {options.phones.length === 0 && <p>No matching phones.</p>}
          {options.more && <p>Showing the first 100 matches. Refine the search to find other phones.</p>}
          <ul className="preparation-phone-list" aria-label="Available phones">
            {options.phones.map(phone => <li key={phone.id}>
              <div><strong>{phone.displayName}</strong><small>{phone.terminalIdHex}{!phone.active && ' · Deactivated'}</small></div>
              <button type="button" onClick={() => addPhone(phone)} aria-label={`Add ${phoneLabel(phone)}`}
                disabled={busy || selected.length >= maximumPhones || selected.some(row => row.id === phone.id)}>Add</button>
            </li>)}
          </ul>
        </>}
      </div>
      <div className="preparation-pane">
        <h3>Phones to receive downloads <span className="preparation-count">{selected.length} / {maximumPhones}</span></h3>
        {!selected.length && <p>Add phones from the search results. You can search again without losing your selection.</p>}
        <ul className="preparation-phone-list" aria-label="Selected phones">
          {selected.map(phone => <li key={phone.id}>
            <div><strong>{phone.displayName}</strong><small>{phone.terminalIdHex}</small></div>
            <button type="button" disabled={busy} onClick={() => removePhone(phone.id)} aria-label={`Remove ${phoneLabel(phone)}`}>Remove</button>
          </li>)}
        </ul>
        {selected.length > 0 && <button type="button" disabled={busy} onClick={() => { setSelected([]); invalidate() }}>Clear selection</button>}
      </div>
    </div>
    <fieldset className="preparation-time" disabled={busy}>
      <legend>Host preparation time</legend>
      <label><input type="radio" name="preparation-time" checked={timing === 'now'} onChange={() => { setTiming('now'); invalidate() }} /> Prepare now</label>
      <label><input type="radio" name="preparation-time" checked={timing === 'later'} onChange={() => { setTiming('later'); invalidate() }} /> Prepare later</label>
      {timing === 'later' && <label>Preparation time (UTC)<input type="datetime-local" value={dateTime} onChange={event => { setDateTime(event.target.value); invalidate() }} required /></label>}
    </fieldset>
    <p>Once confirmed, each schedule retains the reviewed published set. Changing a phone assignment later does not change that already scheduled set.</p>
    <button type="button" disabled={busy || !selected.length || (timing === 'later' && !dateTime)} onClick={() => void review()}>Review selected phones</button>
    {error && <p role="alert">{error}</p>}
    {preview && <section className="preparation-review" aria-labelledby="preparation-review-heading">
      <h3 id="preparation-review-heading">Download review</h3>
      <p>{preview.eligibleCount} of {preview.phones.length} phones eligible. {preview.preparationAtUtc ? `Prepare at ${utcLabel(preview.preparationAtUtc)}.` : 'Pack the reviewed configurations now.'}</p>
      <div className="preparation-table"><table>
        <thead><tr><th>Phone</th><th>Assigned configuration</th><th>Records</th><th>Review result</th></tr></thead>
        <tbody>{preview.phones.map(phone => <tr key={phone.terminalId}>
          <td>{phone.displayName ?? 'Unavailable'}<small>{phone.terminalIdHex ?? ''}</small></td>
          <td>{phone.tableSetName ?? 'None'}</td>
          <td>{phone.records.length ? phone.records.map(record => `MT${record.tableNumber}`).join(', ') : 'None'}</td>
          <td>{phone.reason ?? 'Ready for preparation'}</td>
        </tr>)}</tbody>
      </table></div>
      {preview.eligibleCount !== preview.phones.length && <p role="note">Remove blocked phones or resolve their issues, then review the list again.</p>}
      <button type="button" disabled={busy || preview.eligibleCount !== selected.length} onClick={() => void prepare()}>
        Confirm preparation for {selected.length} {selected.length === 1 ? 'phone' : 'phones'}
      </button>
    </section>}
    {results && <section className="preparation-review" aria-labelledby="preparation-result-heading">
      <h3 id="preparation-result-heading">{results.length} download {results.length === 1 ? 'schedule' : 'schedules'} created</h3>
      <ul>{results.map(result => <li key={result.scheduleId}><strong>{result.displayName}</strong> ({result.terminalIdHex}) · {result.tableSetName}. {result.batchId
        ? <>{showBatchLinks ? <Link to={`/downloads/${result.batchId}`}>Open prepared batch</Link> : 'Batch prepared'} · Waiting for phone contact.</>
        : <>Scheduled to prepare at {utcLabel(result.availableAtUtc)}.</>}</li>)}</ul>
    </section>}
  </section>
}
