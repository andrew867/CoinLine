import { useState } from 'react'
import { Link } from 'react-router-dom'
import { apiPostRaw } from '../api/client'

type Field = { key: string; label: string; action: string; protected: boolean; sourceValue: string | null; targetBefore: string | null; targetAfter: string | null }
type Graph = { section: 'dial' | 'format'; sourceCapacity: number; targetCapacity: number; sourceRules: number | null; candidateRules: number | null;
  proposedRules: number | null; sourceUnassignedBytes: number | null; targetUnassignedBytes: number | null; action: string; changedBytes: number }
type Preview = { recordCount: number; canCreate: boolean; changedBytes: number; fields: Field[]; graphs: Graph[];
  findings: { blocking: boolean; message: string }[]; previewToken: string }
const labels: Record<string, string> = { changed: 'Carry forward', carried: 'Carry recognized graph', unchanged: 'Unchanged',
  'source-only': 'No direct target mapping', 'target-only': 'Retain target-only setting', 'retained-target': 'Keep candidate setting', unresolved: 'Resolve before staging' }

export function FirmwareNumberingStagingPanel({ phoneId, targetId, candidateId, graphical, parentBusy, onBusyChange, onDraftCreated, canAdd }: {
  phoneId: string; targetId: string; candidateId: string; graphical: boolean; parentBusy: boolean;
  onBusyChange: (busy: boolean) => void; onDraftCreated: (id: string, name: string) => void; canAdd: boolean
}) {
  const [name, setName] = useState('')
  const [carryHeader, setCarryHeader] = useState(true)
  const [carryDial, setCarryDial] = useState(true)
  const [carryFormat, setCarryFormat] = useState(!graphical)
  const [carryDeskCredential, setCarryDeskCredential] = useState(false)
  const [reviewedDifferences, setReviewedDifferences] = useState(false)
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
      const response = await apiPostRaw(`/api/firmware/transition-numbering${apply ? '' : '/preview'}`, {
        terminalId: phoneId, targetFirmwareVersionId: targetId, candidateSetId: candidateId, name: name.trim(),
        carryHeader, carryDial, carryFormat, carryDeskCredential, reviewedDifferences, previewToken: apply ? preview?.previewToken : undefined,
      })
      const json = await response.json().catch(() => null)
      if (!response.ok || !json) throw new Error(json?.error ?? `Numbering-plan draft request failed (HTTP ${response.status}).`)
      if (apply) { setCreated({ id: json.id, name: name.trim() }); setPreview(null); setCurrent(false) }
      else { setPreview(json as Preview); setCurrent(true) }
    } catch (e) { setError(e instanceof Error ? e.message : 'Unable to prepare the target numbering plan.'); setCurrent(false) }
    finally { setBusy(false); onBusyChange(false) }
  }
  const options = [
    ['Carry ordinary digit collection and rate-response settings', carryHeader, setCarryHeader],
    ['Carry the source dialing-rule graph', carryDial, setCarryDial],
    ['Carry the source display-format graph', carryFormat, setCarryFormat],
    ['Carry the source desk key-card field (value stays protected)', carryDeskCredential, setCarryDeskCredential],
  ] as const
  function value(field: Field, text: string | null) { return field.protected ? 'Protected' : text ?? 'Not applicable' }
  const count = (value: number | null) => value ?? 'Unrecognized'

  return <section aria-label="Carry forward numbering-plan configuration" style={{ marginTop: 18 }}>
    <h3>Prepare the numbering plan</h3>
    <p>Carry identified MT62 settings and recognized linked rules. The later display-format area is larger; earlier currency bytes are not its rules. The prepared target's stored-value discount fields, later-only and unassigned bytes remain as supplied. The selected graphs replace recognized target rules, not unknown candidate bytes.</p>
    <label>New numbering draft name <input value={name} maxLength={120} disabled={disabled} onChange={event => { setName(event.target.value); invalidate(false) }} /></label>
    <div className="row-actions">{options.map(([label, checked, setter]) => <label key={label}><input type="checkbox" checked={checked} disabled={disabled}
      onChange={event => { setter(event.target.checked); invalidate() }} /> {label}</label>)}</div>
    <button type="button" disabled={disabled || !name.trim()} onClick={() => void request(false)}>{busy ? 'Working…' : 'Preview numbering-plan carry-forward'}</button>
    {error && <p role="alert">{error}</p>}
    {preview && <>
      <p><strong>{preview.changedBytes} MT62 bytes would change.</strong> {preview.recordCount - 1} other records retained unchanged.</p>
      <h4>Linked rule graph review</h4>
      <div style={{ overflowX: 'auto' }}><table cellPadding={7} style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead><tr><th>Rules</th><th>Source</th><th>Candidate</th><th>Proposed count</th><th>Unassigned nonzero bytes (source / target)</th><th>Changed bytes</th><th>Action</th></tr></thead>
        <tbody>{preview.graphs.map(graph => <tr key={graph.section}><th scope="row">{graph.section === 'dial' ? 'Dialing' : 'Display formatting'}</th>
          <td>{count(graph.sourceRules)} rules / {graph.sourceCapacity} bytes available</td><td>{count(graph.candidateRules)} rules / {graph.targetCapacity} bytes available</td>
          <td>{count(graph.proposedRules)}</td><td>{count(graph.sourceUnassignedBytes)} / {count(graph.targetUnassignedBytes)}</td><td>{graph.changedBytes}</td><td>{labels[graph.action] ?? graph.action}</td></tr>)}</tbody>
      </table></div>
      <h4>Field review</h4>
      <label><input type="checkbox" checked={showAll} onChange={event => setShowAll(event.target.checked)} /> Include unchanged common fields</label>
      <div style={{ overflowX: 'auto' }}><table cellPadding={7} style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead><tr><th>Setting</th><th>Source</th><th>Candidate</th><th>Proposed</th><th>Action</th></tr></thead>
        <tbody>{preview.fields.filter(field => showAll || field.action !== 'unchanged').map(field => <tr key={`${field.key}:${field.action}`}><th scope="row">{field.label}</th>
          <td>{value(field, field.sourceValue)}</td><td>{value(field, field.targetBefore)}</td><td>{value(field, field.targetAfter)}</td><td>{labels[field.action] ?? field.action}</td></tr>)}</tbody>
      </table></div>
      <ul>{preview.findings.map((finding, index) => <li key={index}><strong>{finding.blocking ? 'Action required' : 'Review'}:</strong> {finding.message}</li>)}</ul>
      <label><input type="checkbox" checked={reviewedDifferences} disabled={disabled} onChange={event => { setReviewedDifferences(event.target.checked); invalidate(false) }} /> I reviewed the graph changes and profile-specific retained settings</label>
      {!current ? <p role="status">Preview again to confirm your choices and review.</p> : !preview.canCreate ? <p role="status">Resolve the blocking findings before creating a draft.</p>
        : !canAdd ? <p>Table set creation permission is required to save the draft.</p> : <button type="button" disabled={disabled} onClick={() => {
          if (window.confirm('Create this separate unassigned numbering draft? It does not qualify or activate an upgrade.')) void request(true)
        }}>Create numbering target draft</button>}
    </>}
    {created && <p>Numbering draft created, not assigned. <Link to={`/table-sets/${created.id}`}>Open numbering draft</Link>.{' '}
      <button type="button" disabled={disabled} onClick={() => onDraftCreated(created.id, created.name)}>Use as target candidate for further review</button></p>}
  </section>
}
