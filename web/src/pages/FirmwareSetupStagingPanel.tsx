import { useState } from 'react'
import { Link } from 'react-router-dom'
import { apiPostRaw } from '../api/client'

type FieldReview = { tableNumber: number; key: string; label: string; action: string; protected: boolean;
  sourceValue: string | null; targetBefore: string | null; targetAfter: string | null }
type Preview = { recordCount: number; canCreate: boolean; changedRecords: number[]; fields: FieldReview[];
  references: { sourceSlot: number; fields: string[]; options: number[]; targetSlot: number | null }[];
  findings: { blocking: boolean; tableNumber: number; message: string }[]; previewToken: string }
const labels: Record<string, string> = { changed: 'Carry forward', unchanged: 'Unchanged', 'source-only': 'No direct target mapping',
  'target-only': 'Retain target-only setting', 'retained-target': 'Keep candidate setting', unresolved: 'Resolve before staging' }

export function FirmwareSetupStagingPanel({ phoneId, targetId, candidateId, graphical, parentBusy, onBusyChange, onDraftCreated, canAdd }: {
  phoneId: string; targetId: string; candidateId: string; graphical: boolean; parentBusy: boolean;
  onBusyChange: (busy: boolean) => void; onDraftCreated: (id: string, name: string) => void; canAdd: boolean
}) {
  const [name, setName] = useState('')
  const [carryCredentials, setCarryCredentials] = useState(true)
  const [retainPresentation, setRetainPresentation] = useState(graphical)
  const [reviewedDifferences, setReviewedDifferences] = useState(false)
  const [referenceChoices, setReferenceChoices] = useState<Record<string, number>>({})
  const [preview, setPreview] = useState<Preview | null>(null)
  const [current, setCurrent] = useState(false)
  const [showAll, setShowAll] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [created, setCreated] = useState<{ id: string; name: string } | null>(null)
  const disabled = busy || parentBusy

  function invalidate(resetReview = true) {
    setCurrent(false); setCreated(null); setError(null)
    if (resetReview) setReviewedDifferences(false)
  }
  async function request(apply: boolean) {
    setBusy(true); onBusyChange(true); setError(null); setCreated(null)
    if (!apply) setCurrent(false)
    try {
      const response = await apiPostRaw(`/api/firmware/transition-setup${apply ? '' : '/preview'}`, {
        terminalId: phoneId, targetFirmwareVersionId: targetId, candidateSetId: candidateId, name: name.trim(),
        referenceChoices, carryCredentials, retainPresentation, reviewedDifferences, previewToken: apply ? preview?.previewToken : undefined,
      })
      const json = await response.json().catch(() => null)
      if (!response.ok || !json) throw new Error(json?.error ?? `Setup draft request failed (HTTP ${response.status}).`)
      if (apply) { setCreated({ id: json.id, name: name.trim() }); setPreview(null); setCurrent(false) }
      else { setPreview(json as Preview); setCurrent(true) }
    } catch (e) { setError(e instanceof Error ? e.message : 'Unable to prepare the target setup.'); setCurrent(false) }
    finally { setBusy(false); onBusyChange(false) }
  }
  function value(field: FieldReview, text: string | null) { return field.protected ? 'Protected' : text ?? 'Not applicable' }

  return <section aria-label="Carry forward terminal feature and installation settings" style={{ marginTop: 18 }}>
    <h3>Prepare terminal feature and installation settings</h3>
    <p>Carry identified common MT26 and MT31 fields into a separate target draft. Same-size records are not whole-record copies: later language, routing, predial and servicing controls retain their target meaning. Operator route pointers must match prepared target screening and any referenced tariff.</p>
    <label>New setup draft name <input value={name} maxLength={120} disabled={disabled} onChange={event => { setName(event.target.value); invalidate(false) }} /></label>{' '}
    <div className="row-actions">
      <label><input type="checkbox" checked={carryCredentials} disabled={disabled} onChange={event => { setCarryCredentials(event.target.checked); invalidate() }} /> Carry the configured source Craft and desk key-card fields (values stay protected)</label>
      <label><input type="checkbox" checked={retainPresentation} disabled={disabled} onChange={event => { setRetainPresentation(event.target.checked); invalidate() }} /> Retain target display and advertising settings</label>
    </div>
    <button type="button" disabled={disabled || !name.trim()} onClick={() => void request(false)}>{busy ? 'Working…' : 'Preview terminal setup carry-forward'}</button>
    {error && <p role="alert">{error}</p>}
    {preview && <>
      <p><strong>{preview.changedRecords.length ? `Changes proposed to ${preview.changedRecords.map(number => `MT${number}`).join(', ')}.` : 'No record bytes would change.'}</strong> {preview.recordCount - 2} other records retained unchanged.</p>
      {preview.references.length > 0 && <><h4>Operator access reference mapping</h4>
        <table cellPadding={7} style={{ width: '100%', borderCollapse: 'collapse' }}><thead><tr><th>Source screening position</th><th>Fields</th><th>Target screening position</th></tr></thead>
          <tbody>{preview.references.map(reference => <tr key={reference.sourceSlot}><th scope="row">{reference.sourceSlot}</th><td>{reference.fields.join(', ')}</td>
            <td>{reference.options.length ? <select aria-label={`Target screening for source ${reference.sourceSlot}`} value={referenceChoices[reference.sourceSlot] ?? reference.targetSlot ?? ''}
              disabled={disabled} onChange={event => { setReferenceChoices(previous => { const next = { ...previous }; if (event.target.value === '') delete next[reference.sourceSlot]; else next[reference.sourceSlot] = Number(event.target.value); return next }); invalidate() }}>
              <option value="">Choose or use unique match</option>{reference.options.map(slot => <option key={slot} value={slot}>{slot}</option>)}</select> : 'No reviewed match'}</td></tr>)}</tbody>
        </table></>}
      <h4>Field review</h4>
      <label><input type="checkbox" checked={showAll} onChange={event => setShowAll(event.target.checked)} /> Include unchanged common fields</label>
      <div style={{ overflowX: 'auto' }}><table cellPadding={7} style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead><tr><th>Record and setting</th><th>Source</th><th>Candidate</th><th>Proposed</th><th>Action</th></tr></thead>
        <tbody>{preview.fields.filter(field => showAll || field.action !== 'unchanged').map(field => <tr key={`${field.tableNumber}:${field.key}:${field.action}`}>
          <th scope="row">MT{field.tableNumber}: {field.label}</th><td>{value(field, field.sourceValue)}</td><td>{value(field, field.targetBefore)}</td>
          <td>{value(field, field.targetAfter)}</td><td>{labels[field.action] ?? field.action}</td></tr>)}</tbody>
      </table></div>
      <ul>{preview.findings.map((finding, index) => <li key={index}><strong>{finding.blocking ? 'Action required' : 'Review'}, MT{finding.tableNumber}:</strong> {finding.message}</li>)}</ul>
      <label><input type="checkbox" checked={reviewedDifferences} disabled={disabled} onChange={event => { setReviewedDifferences(event.target.checked); invalidate(false) }} /> I reviewed the source-only, target-only and retained options</label>
      {!current ? <p role="status">Preview again to confirm your choices and review.</p> : !preview.canCreate ? <p role="status">Resolve the blocking findings before creating a draft.</p>
        : !canAdd ? <p>Table set creation permission is required to save the draft.</p> : <button type="button" disabled={disabled} onClick={() => {
          if (window.confirm('Create this separate unassigned setup draft? It does not qualify or activate an upgrade.')) void request(true)
        }}>Create setup target draft</button>}
    </>}
    {created && <p>Setup draft created, not assigned. <Link to={`/table-sets/${created.id}`}>Open setup draft</Link>.{' '}
      <button type="button" disabled={disabled} onClick={() => onDraftCreated(created.id, created.name)}>Use as target candidate for further review</button></p>}
  </section>
}
