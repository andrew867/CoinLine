import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { CardMasterFieldInput } from './CardMasterPage'
import { cardMasterEndpoint, cardMasterResponse, type CardMasterDetails, type CardMasterEntry } from './cardMasterApi'

export type CardStamp = { revision: number; sha256: string }
export type CardPosition = { number: number; blank: boolean; movable: boolean;
  master?: { definitionId: string; revision: number; code: string } | null; mapping?: Record<string, string> | null; acceptedCarriers?: number[] }
type Mode = 'add' | 'refresh' | 'link'
type Props = { endpoint: string; setId: string; number: number; cards: CardStamp; carriers: CardStamp; slots: CardPosition[];
  carrierReferences: number[]; labels: Record<number, string>; editable: boolean; busy: boolean; dirty: boolean;
  onBusyChange: (busy: boolean) => void; onCompleted: (action: string, position: number, carrierChanged: boolean) => void }

export function CardMasterConfigurationPanel({ endpoint, setId, number, cards, carriers, slots, carrierReferences, labels,
  editable, busy, dirty, onBusyChange, onCompleted }: Props) {
  const canRead = canPerform('card-master', op.read), canAdd = canPerform('card-master', op.add)
  const canChange = editable && !busy && !dirty
  const [entries, setEntries] = useState<CardMasterEntry[]>([]), [position, setPosition] = useState('')
  const [mode, setMode] = useState<Mode>('add'), [definitionId, setDefinitionId] = useState('')
  const [details, setDetails] = useState<CardMasterDetails | null>(null), [mapping, setMapping] = useState<Record<string, string>>({})
  const [acceptance, setAcceptance] = useState<number[]>([]), [reviewed, setReviewed] = useState(false)
  const [explicit, setExplicit] = useState(false), [code, setCode] = useState(''), [name, setName] = useState('')
  const [error, setError] = useState(''), [notice, setNotice] = useState('')
  const current = slots.find(slot => slot.number === Number(position))
  const firstBlank = slots.find(slot => slot.blank)?.number
  const selectedId = mode === 'refresh' ? current?.master?.definitionId ?? '' : definitionId
  const choice = entries.find(entry => entry.id === selectedId)
  const fields = details?.sections.flatMap(section => section.fields).filter(field => field.targetMapping) ?? []
  const needsMapping = mode === 'add' || (mode === 'refresh' && explicit)
  const canRetain = mode === 'refresh' && fields.length > 0 && fields.every(field => field.key in (current?.mapping ?? {}))
    && Object.keys(current?.mapping ?? {}).length === fields.length

  function reloadList() {
    if (canRead) void apiFetch(`${cardMasterEndpoint}?tableNumber=${number}`).then(cardMasterResponse<CardMasterEntry[]>).then(setEntries)
      .catch(err => setError(err instanceof Error ? err.message : 'Could not load the card Master List.'))
  }
  useEffect(reloadList, [number, canRead])

  useEffect(() => {
    setDetails(null); setMapping({}); setReviewed(false); setExplicit(false); setError('')
    if (!choice) return
    const abort = new AbortController()
    void apiFetch(`${cardMasterEndpoint}/${choice.id}`, { signal: abort.signal }).then(cardMasterResponse<CardMasterDetails>).then(data => {
      if (abort.signal.aborted) return
      const fields = data.sections.flatMap(section => section.fields).filter(field => field.targetMapping)
      const values = Object.fromEntries(fields.map(field => [field.key,
        mode === 'refresh' && current?.mapping?.[field.key] != null ? current.mapping[field.key] : field.value ?? '']))
      setMapping(values); setDetails(data)
      setAcceptance(mode === 'refresh' ? (current?.acceptedCarriers ?? []).filter(value => carrierReferences.includes(value)) : [])
    }).catch(err => { if (!abort.signal.aborted) setError(err instanceof Error ? err.message : 'Could not load master card settings.') })
    return () => abort.abort()
  }, [choice?.id, choice?.revision, mode, position])

  function editMapping(key: string, value: string) { setMapping(old => ({ ...old, [key]: value })); setReviewed(false) }
  function toggleCarrier(reference: number) {
    setAcceptance(old => old.includes(reference) ? old.filter(value => value !== reference) : [...old, reference].sort((a, b) => a - b)); setReviewed(false)
  }

  async function apply(action: Mode | 'remove') {
    if (!canChange || (action !== 'remove' && !choice)) return
    if (action === 'remove' && (!current || !window.confirm(`Remove card position ${current.number} from this draft configuration? The Master List definition will remain.`))) return
    onBusyChange(true); setError(''); setNotice('')
    try {
      const mapped = action === 'add' || (action === 'refresh' && explicit)
      const result = await cardMasterResponse<{ position: number; carrierChanged: boolean }>(await apiFetch(`${endpoint}/${action}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ cards, carriers,
          definitionId: action === 'add' || action === 'link' ? choice?.id : undefined,
          definitionRevision: action !== 'remove' ? choice?.revision : undefined, position: action !== 'add' ? current?.number : undefined,
          mapping: mapped ? mapping : undefined, acceptedCarriers: mapped ? acceptance : undefined, confirm: action === 'remove' })
      }))
      onCompleted(action, result.position, result.carrierChanged)
    } catch (err) { setError(err instanceof Error ? err.message : 'Could not change this card configuration.') }
    finally { onBusyChange(false) }
  }

  async function saveExisting() {
    if (!canAdd || busy || dirty || !current?.movable || !code.trim()) return
    onBusyChange(true); setError(''); setNotice('')
    try {
      const data = await cardMasterResponse<CardMasterDetails>(await apiFetch(cardMasterEndpoint, { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code, name, tableNumber: number, changes: {}, source: { setId, position: current.number, ...cards } }) }))
      setCode(''); setName(''); setNotice('Card definition saved to the Master List. Link the existing position to track future updates, or add it to another draft.')
      if (canRead) { reloadList(); setMode('link'); setDefinitionId(data.entry.id) }
    } catch (err) { setError(err instanceof Error ? err.message : 'Could not save this card definition.') }
    finally { onBusyChange(false) }
  }

  const definitionReady = !!details && details.entry.id === choice?.id && details.entry.revision === choice?.revision
  const primary = Number(mapping['card-0.carrier'])
  const validCarrier = primary === 255 || (carrierReferences.includes(primary) && acceptance.includes(primary))
  const goodMapping = definitionReady && reviewed && validCarrier && fields.every(field => mapping[field.key] != null && mapping[field.key] !== '')
  return <section className="table-config-panel" aria-label="Card master configuration">
    <h3>Card configuration membership</h3>
    <p>{canRead && <><Link to="/card-master">Open Card Master List</Link>. </>}Linked entries retain the applied definition revision. Directly editing a linked card row makes that row an independent configuration.</p>
    {error && <p role="alert">{error}</p>}{notice && <p role="status">{notice}</p>}
    <div className="table-set-versions"><table><thead><tr><th>Position</th><th>Card range</th><th>Master definition</th></tr></thead><tbody>
      {slots.filter(slot => slot.movable).map(slot => <tr key={slot.number}><td>{slot.number}</td><td>{labels[slot.number] || 'Configured'}</td>
        <td>{slot.master ? `${slot.master.code} · revision ${slot.master.revision}` : 'Independent'}</td></tr>)}
    </tbody></table></div>
    {(editable || canAdd) && <div className="table-config-selectors"><label>Configured card position<select value={position} disabled={busy || dirty} onChange={event => {
      setPosition(event.target.value); setMode('add'); setDefinitionId(''); setReviewed(false)
    }}><option value="">Choose a configured position</option>{slots.filter(slot => slot.movable).map(slot =>
      <option key={slot.number} value={slot.number}>Position {slot.number}{slot.master ? ` · ${slot.master.code}` : ''}</option>)}</select></label>
      {editable && current?.movable && <button type="button" className="quiet" disabled={!canChange} onClick={() => void apply('remove')}>Remove card from configuration</button>}
    </div>}
    {canAdd && current?.movable && <div className="table-config-selectors"><label>New master card type ID<input maxLength={4} value={code} disabled={busy || dirty} onChange={event => setCode(event.target.value)} /></label>
      <label>New master card name<input maxLength={80} value={name} disabled={busy || dirty} onChange={event => setName(event.target.value)} /></label>
      <button type="button" disabled={busy || dirty || !code.trim()} onClick={() => void saveExisting()}>Save position to Master List</button></div>}
    {canRead && editable && <>
      <div className="table-config-selectors"><label>Master list action<select value={mode} disabled={!canChange} onChange={event => { setMode(event.target.value as Mode); setReviewed(false) }}>
        <option value="add">Add to first blank position</option><option value="link" disabled={!current?.movable || !!current.master}>Link matching existing position</option>
        <option value="refresh" disabled={!current?.master}>Refresh linked position</option>
      </select></label>
        {mode !== 'refresh' && <label>Master card definition<select value={definitionId} disabled={!canChange} onChange={event => setDefinitionId(event.target.value)}>
          <option value="">Choose a master definition</option>{entries.map(entry => <option key={entry.id} value={entry.id}>{entry.code} · {entry.name || `MT${entry.tableNumber}`} · revision {entry.revision}</option>)}</select></label>}
      </div>
      {mode === 'add' && <p>{firstBlank == null ? 'No blank positions remain.' : `The next blank position is ${firstBlank}.`}</p>}
      {mode === 'refresh' && current?.master && <p>Applied revision {current.master.revision}. Latest active revision: {choice?.revision ?? 'not available'}.</p>}
      {mode === 'refresh' && choice && <label><input type="checkbox" checked={explicit} disabled={!canChange} onChange={event => { setExplicit(event.target.checked); setReviewed(false) }} /> Review and change target mappings instead of retaining existing references</label>}
      {needsMapping && definitionReady && <fieldset className="native-table-fields" disabled={!canChange}><legend>Destination-specific references</legend>
        {fields.map(field => field.key === 'card-0.carrier' ? <label key={field.key}>Primary carrier reference<select value={mapping[field.key] ?? ''} onChange={event => editMapping(field.key, event.target.value)}>
          {mapping[field.key] !== '255' && !carrierReferences.includes(Number(mapping[field.key])) && <option value={mapping[field.key]}>Unavailable reference {mapping[field.key]}</option>}
          <option value="255">No primary carrier here</option>{carrierReferences.map(reference => <option key={reference} value={reference}>Carrier {reference}</option>)}
        </select></label> : <CardMasterFieldInput key={field.key} field={field} value={mapping[field.key] ?? ''} onChange={value => editMapping(field.key, value)} />)}
        <div role="group" aria-label="Carrier acceptance"><strong>Carriers accepting this position</strong>
          {carrierReferences.length === 0 ? <p>No assigned carrier rows.</p> : carrierReferences.map(reference => <label key={reference} className="native-table-toggle"><input type="checkbox"
            checked={acceptance.includes(reference)} onChange={() => toggleCarrier(reference)} />Carrier {reference}</label>)}</div>
        {!validCarrier && <p>Select the primary carrier in its acceptance list, or choose no primary carrier.</p>}
        <label><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> I have reviewed the references against the destination terminal configuration.</label>
      </fieldset>}
      <div className="row-actions">
        {mode === 'add' && <button type="button" disabled={!canChange || firstBlank == null || !goodMapping} onClick={() => void apply('add')}>Add master card</button>}
        {mode === 'link' && <button type="button" disabled={!canChange || !current?.movable || !!current.master || !definitionReady} onClick={() => void apply('link')}>Link matching card position</button>}
        {mode === 'refresh' && <button type="button" disabled={!canChange || !current?.master || !definitionReady || (explicit ? !goodMapping : !canRetain)}
          onClick={() => void apply('refresh')}>Refresh linked card</button>}
      </div>
    </>}
  </section>
}
