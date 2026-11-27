import { useState } from 'react'
import { Link } from 'react-router-dom'
import { apiPostRaw } from '../api/client'

type Message = { sourceMessage: number; targetMessage: number | null; sourceLabel: string; targetLabel: string | null;
  sourceLines: (string | null)[]; targetLines: (string | null)[]; status: string; eligible: boolean; selected: boolean; potentialChangedBytes: number }
type Pair = { sourceNumber: number; targetNumber: number; status: string; messages: Message[]; changedBytes: number }
type Preview = { sourceSetId: string; candidateSetId: string; recordCount: number; canCreate: boolean; pairs: Pair[]; changedBytes: number;
  findings: { blocking: boolean; message: string }[]; previewToken: string }

export function FirmwareVisualPromptStagingPanel({ phoneId, targetId, candidateId, parentBusy, onBusyChange, onDraftCreated, canAdd }: {
  phoneId: string; targetId: string; candidateId: string; parentBusy: boolean; onBusyChange(value: boolean): void;
  onDraftCreated(id: string, name: string): void; canAdd: boolean
}) {
  const [name, setName] = useState('Reviewed visual prompt transition')
  const [selected, setSelected] = useState<Record<number, number[]>>({})
  const [reviewed, setReviewed] = useState(false)
  const [showSame, setShowSame] = useState(false)
  const [preview, setPreview] = useState<Preview | null>(null)
  const [current, setCurrent] = useState(false)
  const [created, setCreated] = useState<{ id: string; name: string } | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const disabled = busy || parentBusy

  function invalidate() { setCurrent(false); setCreated(null); setError('') }
  function choose(number: number, message: number, include: boolean) {
    setSelected(previous => ({ ...previous, [number]: include ? [...(previous[number] ?? []), message] : (previous[number] ?? []).filter(row => row !== message) }))
    setReviewed(false); invalidate()
  }

  async function request(create: boolean) {
    if (create && (!current || !preview?.canCreate || !canAdd)) return
    setBusy(true); onBusyChange(true); setError(''); setCreated(null)
    const selections = Object.entries(selected).filter(([, ids]) => ids.length > 0)
      .map(([sourceNumber, messageIds]) => ({ sourceNumber: Number(sourceNumber), messageIds: [...messageIds].sort((a,b) => a-b) }))
    try {
      const response = await apiPostRaw(`/api/firmware/transition-prompts${create ? '' : '/preview'}`, {
        terminalId: phoneId, targetFirmwareVersionId: targetId, candidateSetId: candidateId, name: name.trim(),
        selections, reviewed, ...(create ? { previewToken: preview?.previewToken } : {}),
      })
      const json = await response.json().catch(() => null)
      if (!response.ok || !json) throw new Error(json?.error ?? `Prompt transition request failed (HTTP ${response.status}).`)
      if (create) { setCreated({ id: json.id, name: name.trim() }); setPreview(null); setCurrent(false) }
      else { setPreview(json as Preview); setCurrent(true) }
    } catch (reason) { setCurrent(false); setError(reason instanceof Error ? reason.message : 'Unable to prepare the visual prompts.') }
    finally { setBusy(false); onBusyChange(false) }
  }

  const selectedCount = Object.values(selected).reduce((count, rows) => count + rows.length, 0)
  const status = (message: Message) => (({ same: 'Same visible text', different: `${message.potentialChangedBytes} cell bytes differ`,
    'unknown-glyph': 'Unmapped display cells; retain target', unmapped: 'Message meaning differs or is unidentified; retain target' } as Record<string, string>)[message.status] ?? message.status)
  function lines(values: (string | null)[]) {
    return values.length ? values.map((value, index) => <div key={index}><small>{index === 0 ? 'Upper: ' : 'Lower: '}</small>{value === null ? 'Unmapped cells' : value || '(blank)'}</div>) : 'Not mapped'
  }

  return <section aria-label="Carry forward visual prompt configuration" style={{ marginTop: 18 }}>
    <h3>Prepare visual prompt languages</h3>
    <p>Compare the earlier MT27/28 messages with the prepared MT85/86 language records. Select individual compatible two-line messages. Unmapped positions, unselected text and all additional later rows stay as supplied; blank source lines can clear target wording only when selected explicitly.</p>
    <label>New visual prompt draft name <input value={name} maxLength={120} disabled={disabled} onChange={event => { setName(event.target.value); invalidate() }} /></label>{' '}
    <button type="button" disabled={disabled || !name.trim()} onClick={() => void request(false)}>{busy ? 'Working…' : 'Preview visual prompt carry-forward'}</button>
    {error && <p role="alert">{error}</p>}
    {preview && <>
      <p><strong>{selectedCount} message pairs selected.</strong> {current ? `${preview.changedBytes} prompt cell bytes would change.` : 'Preview the updated choices to calculate changes.'} Every other candidate record is retained.</p>
      <button type="button" disabled={disabled || selectedCount === 0} onClick={() => { setSelected({}); setReviewed(false); invalidate() }}>Clear selected messages</button>{' '}
      <label><input type="checkbox" checked={showSame} onChange={event => setShowSame(event.target.checked)} /> Include messages with the same visible text</label>
      {preview.pairs.map(pair => <section key={pair.sourceNumber} aria-label={`MT${pair.sourceNumber} to MT${pair.targetNumber} messages`}>
        <h4>MT{pair.sourceNumber} to MT{pair.targetNumber}</h4>
        {pair.status !== 'available' ? <p>{pair.status === 'missing-source' ? 'Earlier language record missing or incompatible.' : 'Prepared target language record missing or incompatible.'}</p> :
          <div style={{ overflowX: 'auto' }}><table cellPadding={7} style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead><tr><th>Carry</th><th>Earlier message</th><th>Target message</th><th>Source text</th><th>Target text</th><th>Review</th></tr></thead>
            <tbody>{pair.messages.filter(message => showSame || message.status !== 'same' || selected[pair.sourceNumber]?.includes(message.sourceMessage)).map(message =>
              <tr key={message.sourceMessage}>
                <td><input type="checkbox" aria-label={`Carry MT${pair.sourceNumber} message ${message.sourceMessage}`} disabled={disabled || (!message.eligible && !selected[pair.sourceNumber]?.includes(message.sourceMessage))}
                  checked={selected[pair.sourceNumber]?.includes(message.sourceMessage) ?? false} onChange={event => choose(pair.sourceNumber, message.sourceMessage, event.target.checked)} /></td>
                <th scope="row">{message.sourceMessage} · {message.sourceLabel}</th>
                <td>{message.targetMessage == null ? 'Not mapped' : `${message.targetMessage} · ${message.targetLabel}`}</td>
                <td>{lines(message.sourceLines)}</td><td>{lines(message.targetLines)}</td><td>{status(message)}</td>
              </tr>)}</tbody>
          </table></div>}
      </section>)}
      <ul>{preview.findings.map((finding, index) => <li key={index}><strong>{finding.blocking ? 'Action required' : 'Review'}:</strong> {finding.message}</li>)}</ul>
      <label><input type="checkbox" checked={reviewed} disabled={disabled} onChange={event => { setReviewed(event.target.checked); invalidate() }} /> I reviewed the selected text and target message positions</label>
      {!current ? <p role="status">Preview again to confirm your choices and review.</p> : !preview.canCreate ? <p role="status">Resolve the blocking findings before creating a draft.</p> : !canAdd ? <p>Table set creation permission is required to save the draft.</p> :
        <button type="button" disabled={disabled} onClick={() => { if (window.confirm('Create this separate unassigned visual prompt draft? It does not qualify or activate an upgrade.')) void request(true) }}>Create visual prompt target draft</button>}
    </>}
    {created && <p>Visual prompt draft created, not assigned. <Link to={`/table-sets/${created.id}`}>Open visual prompt draft</Link>.{' '}
      <button type="button" disabled={disabled} onClick={() => onDraftCreated(created.id, created.name)}>Use as target candidate for further review</button></p>}
  </section>
}
