import { useEffect, useState } from 'react'
import { apiFetch } from '../api/client'

type Stamp = { revision: number; sha256: string }
type Slot = { number: number; blank: boolean; movable: boolean }
type Sequence = { available: true; editable: boolean; cards: Stamp; carriers: Stamp; carrierNumber: number; slots: Slot[] }
  | { available: false; reason: string }

type Props = { setId: string; number: number; revision: number; sha256: string; editable: boolean; dirty: boolean; busy: boolean;
  labels: Record<number, string>; onBusyChange: (busy: boolean) => void; onMoved: (to: number, carrierChanged: boolean) => void;
  onReload: () => void }

export function CardSequencePanel({ setId, number, revision, sha256, editable, dirty, busy, labels,
  onBusyChange, onMoved, onReload }: Props) {
  const endpoint = `/api/tables/sets/${setId}/records/${number}/card-sequence`
  const [sequence, setSequence] = useState<Sequence | null>(null)
  const [from, setFrom] = useState(''), [to, setTo] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    const abort = new AbortController()
    setSequence(null); setError(''); setFrom(''); setTo('')
    void apiFetch(endpoint, { signal: abort.signal }).then(async response => {
      if (!response.ok) throw new Error(`Could not load card sequence (${response.status}).`)
      const result = await response.json() as Sequence
      if (!abort.signal.aborted) setSequence(result)
    }).catch((err: unknown) => {
      if (!abort.signal.aborted) setError(err instanceof Error ? err.message : 'Could not load card sequence.')
    })
    return () => abort.abort()
  }, [endpoint, revision, sha256])

  const current = sequence?.available && sequence.cards.revision === revision && sequence.cards.sha256.toLowerCase() === sha256.toLowerCase()
  const canMove = current && sequence.editable && editable && !dirty && !busy

  async function move() {
    if (!sequence?.available || !canMove || !from || !to) return
    onBusyChange(true); setError('')
    try {
      const response = await apiFetch(endpoint, { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ from: Number(from), to: Number(to), cards: sequence.cards, carriers: sequence.carriers }) })
      const result = await response.json().catch(() => null)
      if (!response.ok) throw new Error(result?.error ?? `Could not move this card entry (${response.status}).`)
      onMoved(Number(to), result?.carrierChanged === true)
    } catch (err) { setError(err instanceof Error ? err.message : 'Could not move this card entry.') }
    finally { onBusyChange(false) }
  }

  return <section className="table-config-panel" aria-label="Card sequence">
    <h3>Card sequence</h3>
    <p>Move a configured range to a blank position without replacing another entry. Its carrier acceptance follows the card.</p>
    {error && <p role="alert">{error}</p>}
    {error && <button type="button" disabled={busy || dirty} className="quiet" onClick={onReload}>Reload card and carrier records</button>}
    {!sequence && !error && <p role="status">Loading card positions…</p>}
    {sequence && !sequence.available && <p>{sequence.reason}</p>}
    {sequence?.available && <>
      <p>{sequence.slots.length} positions; {sequence.slots.filter(slot => slot.movable).length} configured; {sequence.slots.filter(slot => slot.blank).length} blank.</p>
      {!current && <p>These card positions no longer match the open editor. <button type="button" disabled={busy || dirty} className="quiet" onClick={onReload}>Reload records</button></p>}
      {dirty && <p>Save the current field edits before moving a card entry.</p>}
      {editable && sequence.editable && <div className="table-config-selectors">
        <label>Move from<select value={from} disabled={!canMove} onChange={event => setFrom(event.target.value)}>
          <option value="">Choose a configured position</option>
          {sequence.slots.filter(slot => slot.movable).map(slot => <option key={slot.number} value={slot.number}>Position {slot.number}{labels[slot.number] ? ` · ${labels[slot.number]}` : ''}</option>)}
        </select></label>
        <label>Move to<select value={to} disabled={!canMove} onChange={event => setTo(event.target.value)}>
          <option value="">Choose a blank position</option>
          {sequence.slots.filter(slot => slot.blank).map(slot => <option key={slot.number} value={slot.number}>Position {slot.number} · Blank</option>)}
        </select></label>
        <button type="button" disabled={!canMove || !from || !to} onClick={() => void move()}>Move card entry</button>
      </div>}
      <p>Moving saves the draft MT{number} and any affected MT{sequence.carrierNumber} acceptance bits together. Publish and download both records if their positions change.</p>
    </>}
  </section>
}
