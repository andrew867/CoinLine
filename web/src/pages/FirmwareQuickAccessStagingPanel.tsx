import { useState } from 'react'
import { Link } from 'react-router-dom'
import { apiPostRaw } from '../api/client'

type Reference = { kind: 'tariff' | 'carrier'; sourceIndex: number; slots: number[]; options: number[]; targetIndex: number | null }
type Preview = { recordCount: number; canCreate: boolean; sourceActiveSlots: number[]; targetActiveSlots: number[];
  changedSlots: number[]; clearedSlots: number[]; retainedTargetSlots: number[]; references: Reference[];
  findings: { blocking: boolean; slot: number | null; message: string }[]; previewToken: string }

export function FirmwareQuickAccessStagingPanel({ phoneId, targetId, candidateId, parentBusy, onBusyChange, onDraftCreated, canAdd }: {
  phoneId: string; targetId: string; candidateId: string; parentBusy: boolean; onBusyChange(value: boolean): void;
  onDraftCreated(id: string, name: string): void; canAdd: boolean
}) {
  const [name, setName] = useState('Reviewed quick-access transition')
  const [sourceKeys, setSourceKeys] = useState('')
  const [targetKeys, setTargetKeys] = useState('')
  const [reviewed, setReviewed] = useState(false)
  const [tariffs, setTariffs] = useState<Record<string, number>>({})
  const [carriers, setCarriers] = useState<Record<string, number>>({})
  const [preview, setPreview] = useState<Preview | null>(null)
  const [current, setCurrent] = useState(false)
  const [created, setCreated] = useState<{ id: string; name: string } | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const disabled = parentBusy || busy
  function invalidate() { setCurrent(false); setCreated(null); setError('') }
  function changeLayout(setter: (value: string) => void, value: string) {
    setter(value); setTariffs({}); setCarriers({}); setReviewed(false); setPreview(null); invalidate()
  }
  function choose(reference: Reference, value: string) {
    const setter = reference.kind === 'tariff' ? setTariffs : setCarriers
    setter(previous => { const next = { ...previous }; if (value === '') delete next[reference.sourceIndex]; else next[reference.sourceIndex] = Number(value); return next })
    setReviewed(false); invalidate()
  }
  async function request(create: boolean) {
    if (!sourceKeys || !targetKeys || !name.trim() || (create && (!current || !preview?.canCreate || !canAdd))) return
    setBusy(true); onBusyChange(true); setError(''); setCreated(null)
    try {
      const response = await apiPostRaw(`/api/firmware/transition-quick-access${create ? '' : '/preview'}`, {
        terminalId: phoneId, targetFirmwareVersionId: targetId, candidateSetId: candidateId, name: name.trim(),
        sourceKeys: Number(sourceKeys), targetKeys: Number(targetKeys), tariffChoices: tariffs, carrierChoices: carriers, reviewed,
        ...(create ? { previewToken: preview?.previewToken } : {}),
      })
      const value = await response.json().catch(() => null)
      if (!response.ok || !value) throw new Error(value?.error ?? `Quick-access draft request failed (HTTP ${response.status}).`)
      if (create) { setCreated({ id: value.id, name: name.trim() }); setPreview(null); setCurrent(false) }
      else { setPreview(value); setCurrent(true) }
    } catch (reason) { setCurrent(false); setError(reason instanceof Error ? reason.message : 'Unable to prepare quick-access keys.') }
    finally { setBusy(false); onBusyChange(false) }
  }

  return <section aria-label="Carry forward quick-access keys" style={{ marginTop: 18 }}>
    <h3>Prepare quick-access key configuration</h3>
    <p>Choose the actual physical button layouts. The five-key layout uses positions 6 through 10; keys are never renumbered.
      Common destinations, display text, customer references and flags stay in the same positions. Rates and carrier references must match reviewed target configuration.
      Earlier messaging modes and incompatible later-only controls are not translated automatically.</p>
    <label>New quick-access draft name <input value={name} maxLength={120} disabled={disabled} onChange={event => { setName(event.target.value); invalidate() }} /></label>{' '}
    <label>Source physical key layout <select value={sourceKeys} disabled={disabled} onChange={event => changeLayout(setSourceKeys,event.target.value)}>
      <option value="">Choose layout</option><option value="5">Five keys (6 to 10)</option><option value="10">Ten keys (1 to 10)</option></select></label>{' '}
    <label>Target physical key layout <select value={targetKeys} disabled={disabled} onChange={event => changeLayout(setTargetKeys,event.target.value)}>
      <option value="">Choose layout</option><option value="5">Five keys (6 to 10)</option><option value="10">Ten keys (1 to 10)</option></select></label>{' '}
    <button type="button" disabled={disabled || !sourceKeys || !targetKeys || !name.trim()} onClick={() => void request(false)}>{busy ? 'Working…' : 'Preview quick-access carry-forward'}</button>
    {error && <p role="alert">{error}</p>}
    {preview && <>
      <p><strong>Source positions {preview.sourceActiveSlots.join(', ')}; target positions {preview.targetActiveSlots.join(', ')}.</strong></p>
      <p>{current ? `${preview.changedSlots.length} target keys would change.` : 'Preview the updated choices to calculate changes.'} {preview.recordCount - 1} other candidate records stay unchanged.</p>
      {preview.changedSlots.length > 0 && <p>Changed physical positions: {preview.changedSlots.join(', ')}.</p>}
      {preview.clearedSlots.length > 0 && <p><strong>Candidate keys being cleared: {preview.clearedSlots.join(', ')}.</strong></p>}
      {preview.retainedTargetSlots.length > 0 && <p>Untouched target positions: {preview.retainedTargetSlots.join(', ')}.</p>}
      {preview.references.length > 0 && <div style={{ overflowX: 'auto' }}><table cellPadding={7} style={{ borderCollapse: 'collapse', width: '100%' }}>
        <thead><tr><th>Source reference</th><th>Physical keys</th><th>Target reference</th></tr></thead>
        <tbody>{preview.references.map(reference => <tr key={`${reference.kind}-${reference.sourceIndex}`}>
          <th scope="row">{reference.kind === 'tariff' ? 'Rate' : 'Carrier'} {reference.sourceIndex}</th><td>{reference.slots.join(', ')}</td>
          <td>{reference.options.length === 0 ? 'No compatible target reference' : <select aria-label={`Quick-access target ${reference.kind} for ${reference.sourceIndex}`}
            disabled={disabled} value={(reference.kind === 'tariff' ? tariffs : carriers)[reference.sourceIndex] ?? reference.targetIndex ?? ''}
            onChange={event => choose(reference,event.target.value)}><option value="">Choose target reference</option>
            {reference.options.map(option => <option key={option} value={option}>{option}</option>)}</select>}</td></tr>)}</tbody>
      </table></div>}
      <ul>{preview.findings.map((finding,index) => <li key={index}><strong>{finding.blocking ? 'Action required' : 'Review'}{finding.slot != null ? `, key ${finding.slot}` : ''}:</strong> {finding.message}</li>)}</ul>
      <label><input type="checkbox" checked={reviewed} disabled={disabled} onChange={event => { setReviewed(event.target.checked); invalidate() }} /> I reviewed the physical key layouts and target references</label>
      {!current ? <p role="status">Preview again to confirm the selected layouts and reference choices.</p> : !preview.canCreate ? <p role="status">Resolve the quick-access findings before creating a draft.</p> : !canAdd ? <p>Table set creation permission is required to save the draft.</p> :
        <button type="button" disabled={disabled} onClick={() => { if (window.confirm('Create a separate unassigned quick-access target draft? No assignment or download will be made.')) void request(true) }}>Create quick-access target draft</button>}
    </>}
    {created && <p>Quick-access draft created, not assigned. <Link to={`/table-sets/${created.id}`}>Open quick-access draft</Link>.{' '}
      <button type="button" disabled={disabled} onClick={() => onDraftCreated(created.id,created.name)}>Use as target candidate for further review</button></p>}
  </section>
}
