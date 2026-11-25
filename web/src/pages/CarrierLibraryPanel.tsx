import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, apiGet } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import type { CarrierEntry } from './CarrierLibraryPage'

type Props = { setId: string; number: number; slot: number; revision: number; sha256: string; reference: string | null;
  editable: boolean; dirty: boolean; onApplied: () => void }
const endpoint = '/api/carrier-library'

async function check(response: Response) {
  if (!response.ok) {
    const body = await response.json().catch(() => null)
    throw new Error(body?.error ?? `Carrier request failed (${response.status}).`)
  }
  return response
}

export function CarrierLibraryPanel({ setId, number, slot, revision, sha256, reference, editable, dirty, onApplied }: Props) {
  const canRead = canPerform('carrier-library', op.read), canAdd = canPerform('carrier-library', op.add)
  const [entries, setEntries] = useState<CarrierEntry[]>([])
  const [selected, setSelected] = useState(''), [keep, setKeep] = useState('clear'), [screen, setScreen] = useState('')
  const [code, setCode] = useState(''), [description, setDescription] = useState('')
  const [error, setError] = useState(''), [notice, setNotice] = useState(''), [busy, setBusy] = useState(false)
  const choice = entries.find(entry => entry.id === selected)
  const maxScreen = number === 23 ? 200 : 180
  const validScreen = /^\d+$/.test(screen) && Number(screen) >= 1 && Number(screen) <= maxScreen

  function refresh() {
    if (!canRead) return
    void apiGet<CarrierEntry[]>(`${endpoint}?tableNumber=${number}`).then(setEntries).catch(() => setError('Could not load the carrier library.'))
  }
  useEffect(refresh, [number, canRead])

  async function apply() {
    if (!choice || !editable || dirty || (choice.featureGroupB && !validScreen)) return
    setBusy(true); setError(''); setNotice('')
    try {
      await check(await apiFetch(`${endpoint}/${choice.id}/apply`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ setId, slot, libraryRevision: choice.revision, recordRevision: revision, recordSha256: sha256,
          keepCardAcceptance: keep === 'keep', screeningEntry: choice.featureGroupB ? Number(screen) : undefined }) }))
      setNotice('Carrier definition applied to this draft row.')
      onApplied()
    } catch (err) { setError(err instanceof Error ? err.message : 'Could not apply the carrier definition.') }
    finally { setBusy(false) }
  }

  async function saveRow() {
    if (!canAdd || dirty || !code.trim() || reference === '255') return
    setBusy(true); setError(''); setNotice('')
    try {
      await check(await apiFetch(endpoint, { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code, description, tableNumber: number, source: { setId, slot, revision, sha256 }, changes: {} }) }))
      setCode(''); setDescription(''); setNotice('Carrier definition saved to the library. Card acceptance is not copied.'); refresh()
    } catch (err) { setError(err instanceof Error ? err.message : 'Could not save the carrier definition.') }
    finally { setBusy(false) }
  }

  if (!canRead && !canAdd) return null
  return <section className="table-config-panel" aria-label="Reusable carrier definitions">
    <h3>Carrier library</h3>
    <p><Link to="/carrier-library">Manage carrier definitions</Link>. Applying a definition copies its reusable fields into this row; it never changes a published set or silently copies card-range positions.</p>
    {dirty && <p>Save the current record edits before applying or saving a library definition.</p>}
    {error && <p role="alert">{error}</p>}{notice && <p role="status">{notice}</p>}
    {canRead && editable && <div className="table-config-selectors">
      <label>Library carrier<select value={selected} disabled={busy || dirty} onChange={event => {
        const entry = entries.find(row => row.id === event.target.value)
        setSelected(event.target.value); setScreen(''); setKeep(entry?.reference === reference ? 'keep' : 'clear')
      }}><option value="">Select a carrier definition</option>{entries.map(entry => <option key={entry.id} value={entry.id}>{entry.code} · {entry.displayName || `Reference ${entry.reference}`}</option>)}</select></label>
      {choice && <>
        <label>Card acceptance for this row<select value={keep} disabled={busy || dirty} onChange={event => setKeep(event.target.value)}>
          <option value="clear">Clear accepted card ranges</option><option value="keep">Keep this row's accepted card ranges</option></select></label>
        {choice.featureGroupB && <label>Target call-screening entry<input inputMode="numeric" value={screen} disabled={busy || dirty}
          onChange={event => setScreen(event.target.value)} /><small>Choose the matching access number in this set's MT92. Library suggestion: {choice.screeningEntry ?? 'none'}.</small></label>}
        <button type="button" disabled={busy || dirty || (choice.featureGroupB && !validScreen)} onClick={() => void apply()}>Apply carrier definition</button>
      </>}
    </div>}
    {canAdd && reference != null && reference !== '255' && <div className="table-config-selectors">
      <label>New library carrier ID<input maxLength={6} value={code} disabled={busy || dirty} onChange={event => setCode(event.target.value)} /></label>
      <label>Library description<input maxLength={80} value={description} disabled={busy || dirty} onChange={event => setDescription(event.target.value)} /></label>
      <button type="button" disabled={busy || dirty || !code.trim()} onClick={() => void saveRow()}>Save this row to library</button>
    </div>}
  </section>
}
