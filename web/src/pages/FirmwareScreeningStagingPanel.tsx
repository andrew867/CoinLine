import { useState } from 'react'
import { Link } from 'react-router-dom'
import { apiPostRaw } from '../api/client'

type Reference = { kind: 'tariff' | 'carrier'; sourceIndex: number; slots: number[]; options: number[]; targetIndex: number | null }
type Finding = { blocking: boolean; slot: number | null; message: string }
type Preview = { sourceSlots: number; targetSlots: number; sourceConfiguredSlots: number; changedSlots: number;
  sourceReservedSlots: number; retainedLaterFlagsSlots: number; descriptionCount: number; recordCount: number;
  canCreate: boolean; changedPositions: number[]; clearedTargetPositions: number[]; references: Reference[]; findings: Finding[]; previewToken: string }

export function FirmwareScreeningStagingPanel({ phoneId, targetId, candidateId, parentBusy, onBusyChange, onDraftCreated }: {
  phoneId: string; targetId: string; candidateId: string; parentBusy: boolean; onBusyChange: (busy: boolean) => void;
  onDraftCreated: (id: string, name: string) => void
}) {
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [preview, setPreview] = useState<Preview | null>(null)
  const [previewCurrent, setPreviewCurrent] = useState(false)
  const [tariffs, setTariffs] = useState<Record<string, number>>({})
  const [carriers, setCarriers] = useState<Record<string, number>>({})
  const [error, setError] = useState<string | null>(null)
  const [created, setCreated] = useState<{ id: string; name: string } | null>(null)

  async function request(apply: boolean) {
    setBusy(true); onBusyChange(true); setError(null); setCreated(null)
    if (!apply) setPreviewCurrent(false)
    try {
      const response = await apiPostRaw(`/api/firmware/transition-screening${apply ? '' : '/preview'}`, {
        terminalId: phoneId, targetFirmwareVersionId: targetId, candidateSetId: candidateId, name: name.trim(),
        tariffChoices: tariffs, carrierChoices: carriers, previewToken: apply ? preview?.previewToken : undefined,
      })
      const json = await response.json().catch(() => null)
      if (!response.ok || !json) throw new Error(json?.error ?? `Screening draft request failed (HTTP ${response.status}).`)
      if (apply) { setCreated({ id: json.id, name: name.trim() }); setPreview(null); setPreviewCurrent(false) }
      else { setPreview(json as Preview); setPreviewCurrent(true) }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to prepare screening rules.')
      setPreviewCurrent(false)
    } finally { setBusy(false); onBusyChange(false) }
  }

  function changeChoice(reference: Reference, value: string) {
    const setter = reference.kind === 'tariff' ? setTariffs : setCarriers
    setter(before => {
      const next = { ...before }
      if (value === '') delete next[reference.sourceIndex]
      else next[reference.sourceIndex] = Number(value)
      return next
    })
    setPreviewCurrent(false); setCreated(null)
  }

  const disabled = parentBusy || busy
  return <section aria-label="Carry forward screening rules" style={{ marginTop: 18 }}>
    <h3>Prepare phone-specific screening rules</h3>
    <p>Carry the assigned 200-entry MT92 list into the 180-entry target candidate. Keep entry positions, common screening settings and descriptions. Target reserved fields stay intact. Paid references use identical target tariff tuples; carrier references require review of the prepared carrier table. Overflow entries and conflicting later-only options must be resolved first.</p>
    <label>New screening draft name <input value={name} maxLength={120} disabled={disabled} onChange={e => {
      setName(e.target.value); setPreviewCurrent(false); setCreated(null)
    }} /></label>{' '}
    <button type="button" disabled={disabled || !name.trim()} onClick={() => void request(false)}>{busy ? 'Working…' : 'Preview screening carry-forward'}</button>
    {error && <p role="alert">{error}</p>}
    {preview && <>
      <p><strong>{preview.sourceSlots} source entries to {preview.targetSlots} target entries.</strong> {preview.sourceConfiguredSlots} configured source positions; {preview.changedSlots} changed target positions; {preview.descriptionCount} source descriptions. {preview.recordCount - 1} other records retained unchanged.</p>
      <p>{preview.sourceReservedSlots} source positions contain reserved data that will not be reinterpreted. {preview.retainedLaterFlagsSlots} target positions contain later-only flags.</p>
      {preview.clearedTargetPositions.length > 0 && <p><strong>Configured candidate positions becoming vacant ({preview.clearedTargetPositions.length}):</strong> {preview.clearedTargetPositions.join(', ')}.</p>}
      {preview.changedPositions.length > 0 && <details><summary>Changed target positions</summary><p>{preview.changedPositions.join(', ')}</p></details>}
      {preview.references.length > 0 && <div style={{ overflowX: 'auto' }}><table cellPadding={7} style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead><tr style={{ textAlign: 'left' }}><th>Source reference</th><th>Screening positions</th><th>Target reference</th></tr></thead>
        <tbody>{preview.references.map(reference => {
          const choices = reference.kind === 'tariff' ? tariffs : carriers
          const chosen = choices[reference.sourceIndex] ?? reference.targetIndex ?? ''
          return <tr key={`${reference.kind}-${reference.sourceIndex}`}>
            <th scope="row">{reference.kind === 'tariff' ? 'Rate' : 'Carrier'} {reference.sourceIndex}</th>
            <td>{reference.slots.join(', ')}</td>
            <td>{reference.options.length === 0 ? 'No compatible target reference' : <select aria-label={`Target ${reference.kind} for ${reference.sourceIndex}`}
              value={chosen} disabled={disabled} onChange={e => changeChoice(reference, e.target.value)}>
              <option value="">Choose a target reference</option>{reference.options.map(option => <option key={option} value={option}>{option}</option>)}
            </select>}</td>
          </tr>
        })}</tbody>
      </table></div>}
      <ul>{preview.findings.map((finding, index) => <li key={index}>
        <strong>{finding.blocking ? 'Action required' : 'Review'}{finding.slot != null ? `, entry ${finding.slot}` : ''}:</strong> {finding.message}</li>)}</ul>
      {!previewCurrent ? <p role="status">Preview again to confirm your changes.</p> : !preview.canCreate ? <p role="status">Resolve the blocking findings and preview again.</p> :
        <button type="button" disabled={disabled} onClick={() => {
          if (window.confirm('Create this separate unassigned screening draft? Review the remaining configuration before publishing.')) void request(true)
        }}>Create screening target draft</button>}
    </>}
    {created && <p>Screening draft created, not assigned. <Link to={`/table-sets/${created.id}`}>Open screening draft</Link>.{' '}
      <button type="button" disabled={disabled} onClick={() => onDraftCreated(created.id, created.name)}>Use as target candidate for further review</button></p>}
  </section>
}
