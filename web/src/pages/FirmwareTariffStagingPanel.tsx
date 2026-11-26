import { useState } from 'react'
import { Link } from 'react-router-dom'
import { apiPostRaw } from '../api/client'

type RateGroup = { sourceIndex: number; sourceIndexes: number[]; classification: number; initialPeriod: number; initialCharge: number;
  additionalPeriod: number; additionalCharge: number; targetOptions: number[]; targetIndex: number | null; action: 'reuse' | 'allocate' | 'blocked';
  replacedClassification: number | null }
type Position = { index: number; recordNumbers: number[] }
type Preview = { sourceRows: number; targetRows: number; recordCount: number; canCreate: boolean; changedPositions: number[]; groups: RateGroup[];
  candidateReview: { protectedPositions: Position[]; reviewedRecords: number[]; missingReferenceRecords: number[]; unreviewedRecords: number[] };
  findings: { blocking: boolean; sourceIndex: number | null; message: string }[]; previewToken: string }

const classes = ['Host-rated intra-area toll', 'Local measured service', 'Fixed local charge', 'Rate unavailable', 'Invalid number',
  'Intra-area toll', 'Inter-area toll', 'Host-rated inter-area toll', 'Host-rated local']
const period = (seconds: number) => seconds === -1 ? 'Unlimited' : `${seconds} s`
const cents = (amount: number) => `${amount}¢`
const mts = (numbers: number[]) => numbers.map(number => `MT${number}`).join(', ')

function ranges(indexes: number[]) {
  const result: string[] = []
  for (let index = 0; index < indexes.length; index++) {
    const first = indexes[index]
    while (index + 1 < indexes.length && indexes[index + 1] === indexes[index] + 1) index++
    result.push(first === indexes[index] ? String(first) : `${first} to ${indexes[index]}`)
  }
  return result.join(', ')
}

export function FirmwareTariffStagingPanel({ phoneId, targetId, candidateId, parentBusy, onBusyChange, onDraftCreated, canAdd }: {
  phoneId: string; targetId: string; candidateId: string; parentBusy: boolean; onBusyChange: (busy: boolean) => void;
  onDraftCreated: (id: string, name: string) => void; canAdd: boolean
}) {
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [preview, setPreview] = useState<Preview | null>(null)
  const [current, setCurrent] = useState(false)
  const [choices, setChoices] = useState<Record<string, number>>({})
  const [error, setError] = useState<string | null>(null)
  const [created, setCreated] = useState<{ id: string; name: string } | null>(null)

  async function request(apply: boolean) {
    setBusy(true); onBusyChange(true); setError(null); setCreated(null)
    if (!apply) setCurrent(false)
    try {
      const response = await apiPostRaw(`/api/firmware/transition-tariffs${apply ? '' : '/preview'}`, {
        terminalId: phoneId, targetFirmwareVersionId: targetId, candidateSetId: candidateId, name: name.trim(), choices,
        previewToken: apply ? preview?.previewToken : undefined,
      })
      const json = await response.json().catch(() => null)
      if (!response.ok || !json) throw new Error(json?.error ?? `Tariff draft request failed (HTTP ${response.status}).`)
      if (apply) { setCreated({ id: json.id, name: name.trim() }); setPreview(null); setCurrent(false) }
      else { setPreview(json as Preview); setCurrent(true) }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to prepare the target tariffs.'); setCurrent(false)
    } finally { setBusy(false); onBusyChange(false) }
  }

  function change(index: number, value: string) {
    setChoices(before => {
      const next = { ...before }
      if (value === '') delete next[index]
      else next[index] = Number(value)
      return next
    }); setCurrent(false); setCreated(null)
  }

  const disabled = busy || parentBusy
  return <section aria-label="Carry forward terminal tariffs" style={{ marginTop: 18 }}>
    <h3>Prepare phone-specific terminal tariffs</h3>
    <p>Preserve the source MT73 classifications, periods and charges, including unavailable and invalid rates. Equal source rows share a target definition. Exact target tuples are reused; new definitions can occupy only unreferenced empty invalid or unavailable rows outside local dialing indexes 0 to 3. This does not modify existing active tariffs, effective dates or companion records.</p>
    <label>New tariff draft name <input value={name} maxLength={120} disabled={disabled} onChange={e => {
      setName(e.target.value); setCurrent(false); setCreated(null)
    }} /></label>{' '}
    <button type="button" disabled={disabled || !name.trim()} onClick={() => void request(false)}>{busy ? 'Working…' : 'Preview tariff carry-forward'}</button>
    {error && <p role="alert">{error}</p>}
    {preview && <>
      <p><strong>{preview.sourceRows} source rows, {preview.groups.length} distinct tariff definitions.</strong> {preview.groups.filter(group => group.action === 'reuse').length} existing tuples reused; {preview.changedPositions.length} target positions allocated. {preview.recordCount - 1} other records retained unchanged.</p>
      {preview.changedPositions.length > 0 && <p><strong>Target positions to change:</strong> {ranges(preview.changedPositions)}.</p>}
      <div style={{ overflowX: 'auto' }}><table cellPadding={7} style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead><tr style={{ textAlign: 'left' }}><th>Source indexes</th><th>Classification</th><th>Initial charge / period</th><th>Additional charge / period</th><th>Target index</th><th>Action</th></tr></thead>
        <tbody>{preview.groups.map(group => <tr key={group.sourceIndex}>
          <th scope="row">{ranges(group.sourceIndexes)}</th><td>{classes[group.classification] ?? `Unknown (${group.classification})`}</td>
          <td>{cents(group.initialCharge)} / {period(group.initialPeriod)}</td><td>{cents(group.additionalCharge)} / {period(group.additionalPeriod)}</td>
          <td>{group.targetOptions.length === 0 ? 'No eligible target' : <select aria-label={`Target tariff for source ${group.sourceIndex}`} disabled={disabled}
            value={choices[group.sourceIndex] ?? group.targetIndex ?? ''} onChange={e => change(group.sourceIndex, e.target.value)}>
            <option value="">Use suggested position</option>{group.targetOptions.map(index => <option key={index} value={index}>{index}</option>)}
          </select>}</td><td>{group.action === 'reuse' ? 'Reuse exact tuple' : group.action === 'allocate'
            ? `Allocate empty ${classes[group.replacedClassification ?? -1] ?? 'row'}` : 'Resolve finding'}</td>
        </tr>)}</tbody>
      </table></div>
      <details><summary>Protected target indexes ({preview.candidateReview.protectedPositions.length})</summary>
        <p>Local dialing indexes 0 to 3 remain reserved even when its records are absent. Recorded companion references can also prevent allocation.</p>
        <ul>{preview.candidateReview.protectedPositions.map(position => <li key={position.index}>Rate {position.index}: {position.recordNumbers.length ? mts(position.recordNumbers) : 'Reserved local dialing domain'}</li>)}</ul>
      </details>
      {preview.candidateReview.missingReferenceRecords.length > 0 && <p>Reference records not included in this candidate: {mts(preview.candidateReview.missingReferenceRecords)}.</p>}
      {preview.candidateReview.unreviewedRecords.length > 0 && <p>Unreviewed candidate records: {mts(preview.candidateReview.unreviewedRecords)}.</p>}
      <ul>{preview.findings.map((finding, index) => <li key={index}><strong>{finding.blocking ? 'Action required' : 'Review'}{finding.sourceIndex != null ? `, rate ${finding.sourceIndex}` : ''}:</strong> {finding.message}</li>)}</ul>
      {!current ? <p role="status">Preview again to confirm your changes.</p> : !preview.canCreate ? <p role="status">Resolve the blocking findings before creating a draft.</p> : !canAdd ?
        <p>Table set creation permission is required to save this draft.</p> : <button type="button" disabled={disabled} onClick={() => {
          if (window.confirm('Create this separate unassigned tariff draft? Review companion records before publication.')) void request(true)
        }}>Create tariff target draft</button>}
    </>}
    {created && <p>Tariff draft created, not assigned. <Link to={`/table-sets/${created.id}`}>Open tariff draft</Link>.{' '}
      <button type="button" disabled={disabled} onClick={() => onDraftCreated(created.id, created.name)}>Use as target candidate for further review</button></p>}
  </section>
}
