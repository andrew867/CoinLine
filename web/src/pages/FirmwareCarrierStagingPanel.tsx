import { useState } from 'react'
import { Link } from 'react-router-dom'
import { apiPostRaw } from '../api/client'

type Mapping = { kind: 'card' | 'screening'; sourceSlot: number; options: number[]; targetSlot: number | null }
type Preview = { recordCount: number; canCreate: boolean; changedSlots: number[]; defaultSelectionsChanged: boolean;
  rows: { reference: number; sourceSlot: number; targetSlot: number; changed: boolean }[]; mappings: Mapping[];
  findings: { blocking: boolean; carrierReference: number | null; message: string }[]; previewToken: string;
  cardPreparation?: { rows: { sourceSlot: number; targetSlot: number; created: boolean; changed: boolean }[]; changedSlots: number[] } | null }

export function FirmwareCarrierStagingPanel({ phoneId, targetId, candidateId, parentBusy, onBusyChange, onDraftCreated, canAdd }: {
  phoneId: string; targetId: string; candidateId: string; parentBusy: boolean; onBusyChange: (busy: boolean) => void;
  onDraftCreated: (id: string, name: string) => void; canAdd: boolean
}) {
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [preview, setPreview] = useState<Preview | null>(null)
  const [current, setCurrent] = useState(false)
  const [cardChoices, setCardChoices] = useState<Record<string, number>>({})
  const [prepareCards, setPrepareCards] = useState(false)
  const [screeningChoices, setScreeningChoices] = useState<Record<string, number>>({})
  const [error, setError] = useState<string | null>(null)
  const [created, setCreated] = useState<{ id: string; name: string } | null>(null)
  const disabled = busy || parentBusy

  async function request(apply: boolean) {
    setBusy(true); onBusyChange(true); setError(null); setCreated(null)
    if (!apply) setCurrent(false)
    try {
      const response = await apiPostRaw(`/api/firmware/transition-carriers${apply ? '' : '/preview'}`, {
        terminalId: phoneId, targetFirmwareVersionId: targetId, candidateSetId: candidateId, name: name.trim(),
        cardChoices, screeningChoices, prepareCards, previewToken: apply ? preview?.previewToken : undefined,
      })
      const json = await response.json().catch(() => null)
      if (!response.ok || !json) throw new Error(json?.error ?? `Carrier draft request failed (HTTP ${response.status}).`)
      if (apply) { setCreated({ id: json.id, name: name.trim() }); setPreview(null); setCurrent(false) }
      else { setPreview(json as Preview); setCurrent(true) }
    } catch (e) { setError(e instanceof Error ? e.message : 'Unable to prepare the target carriers.'); setCurrent(false) }
    finally { setBusy(false); onBusyChange(false) }
  }

  function choose(mapping: Mapping, value: string) {
    const setter = mapping.kind === 'card' ? setCardChoices : setScreeningChoices
    setter(before => { const next = { ...before }; if (value === '') delete next[mapping.sourceSlot]; else next[mapping.sourceSlot] = Number(value); return next })
    setCurrent(false); setCreated(null)
  }

  return <section aria-label="Carry forward carrier configuration" style={{ marginTop: 18 }}>
    <h3>Prepare phone-specific carriers</h3>
    <p>Carry common MT23 settings into a separate MT135 draft. Carrier identifiers remain unchanged, existing target carrier positions are reused, and new carrier rows occupy only completely vacant positions. The later local/intra defaults, rate permissions, reserved fields and unrelated target records remain as supplied.</p>
    <p>Use the prepared target magnetic card and screening records, or carry eligible earlier magnetic ranges into the target card record together with their carrier acceptance. A range and standard match identifies a card position, not its application acceptance. Unsupported stored-value or ambiguous mappings need separate review.</p>
    <label><input type="checkbox" checked={prepareCards} disabled={disabled} onChange={event => {
      setPrepareCards(event.target.checked); setCardChoices({}); setPreview(null); setCurrent(false); setCreated(null); setError(null)
    }} /> Prepare magnetic card ranges and service codes in the same draft</label>
    {prepareCards && <p>Keep existing target-only card settings; use completely vacant positions for missing ranges, preserve source selection order, and do not truncate a service-code list that exceeds target capacity. The paired carrier acceptance is reviewed in the same draft.</p>}
    <label>New carrier draft name <input value={name} maxLength={120} disabled={disabled} onChange={e => { setName(e.target.value); setCurrent(false); setCreated(null) }} /></label>{' '}
    <button type="button" disabled={disabled || !name.trim()} onClick={() => void request(false)}>{busy ? 'Working…' : prepareCards ? 'Preview card and carrier carry-forward' : 'Preview carrier carry-forward'}</button>
    {error && <p role="alert">{error}</p>}
    {preview && <>
      <p><strong>{preview.rows.length} source carriers mapped; {preview.changedSlots.length} target carrier positions changed.</strong> {preview.defaultSelectionsChanged ? 'Common default selections change. ' : 'Common defaults unchanged. '}{preview.recordCount - (preview.cardPreparation ? 2 : 1)} other records retained unchanged.</p>
      {preview.cardPreparation && <><h4>Magnetic card range preparation</h4>
        <p>{preview.cardPreparation.rows.length} source card positions mapped; {preview.cardPreparation.changedSlots.length} target card positions changed.</p>
        <table cellPadding={7} style={{ width: '100%', borderCollapse: 'collapse' }}><thead><tr><th>Source card position</th><th>Target card position</th><th>Action</th></tr></thead>
          <tbody>{preview.cardPreparation.rows.map(row => <tr key={row.sourceSlot}><th scope="row">{row.sourceSlot}</th><td>{row.targetSlot}</td>
            <td>{row.created ? 'Allocate vacant position' : row.changed ? 'Update common fields' : 'Unchanged'}</td></tr>)}</tbody></table></>}
      <div style={{ overflowX: 'auto' }}><table cellPadding={7} style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead><tr><th>Carrier reference</th><th>Source position</th><th>Target position</th><th>Action</th></tr></thead>
        <tbody>{preview.rows.map(row => <tr key={row.reference}><th scope="row">{row.reference}</th><td>{row.sourceSlot}</td><td>{row.targetSlot}</td><td>{row.changed ? 'Update target carrier' : 'Unchanged'}</td></tr>)}</tbody>
      </table></div>
      {preview.mappings.length > 0 && <><h4>Card and screening reference mapping</h4><table cellPadding={7} style={{ width: '100%' }}>
        <thead><tr><th>Reference family</th><th>Source position</th><th>Target position</th></tr></thead>
        <tbody>{preview.mappings.map(mapping => <tr key={`${mapping.kind}-${mapping.sourceSlot}`}><td>{mapping.kind === 'card' ? 'Card range' : 'Feature Group B screening'}</td><td>{mapping.sourceSlot}</td>
          <td>{preview.cardPreparation && mapping.kind === 'card' ? (mapping.targetSlot ?? 'No reviewed target match') : mapping.options.length ? <select aria-label={`Target ${mapping.kind} for source ${mapping.sourceSlot}`} disabled={disabled}
            value={(mapping.kind === 'card' ? cardChoices : screeningChoices)[mapping.sourceSlot] ?? mapping.targetSlot ?? ''} onChange={e => choose(mapping, e.target.value)}>
            <option value="">Choose or use unique match</option>{mapping.options.map(slot => <option key={slot} value={slot}>{slot}</option>)}
          </select> : 'No reviewed target match'}</td></tr>)}</tbody>
      </table></>}
      <ul>{preview.findings.map((finding,index) => <li key={index}><strong>{finding.blocking ? 'Action required' : 'Review'}{finding.carrierReference != null ? `, carrier ${finding.carrierReference}` : ''}:</strong> {finding.message}</li>)}</ul>
      {!current ? <p role="status">Preview again to confirm your changes.</p> : !preview.canCreate ? <p role="status">Resolve the blocking findings before creating a draft.</p>
        : !canAdd ? <p>Table set creation permission is required to save this draft.</p> : <button type="button" disabled={disabled} onClick={() => {
          if (window.confirm('Create this separate unassigned configuration draft? Review all companion card and screening settings before publication.')) void request(true)
        }}>{prepareCards ? 'Create card and carrier target draft' : 'Create carrier target draft'}</button>}
    </>}
    {created && <p>{prepareCards ? 'Card and carrier' : 'Carrier'} draft created, not assigned. <Link to={`/table-sets/${created.id}`}>{prepareCards ? 'Open card and carrier draft' : 'Open carrier draft'}</Link>.{' '}
      <button type="button" disabled={disabled} onClick={() => onDraftCreated(created.id, created.name)}>Use as target candidate for further review</button></p>}
  </section>
}
