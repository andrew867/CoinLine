import { useState } from 'react'
import { Link } from 'react-router-dom'
import { apiPostRaw } from '../api/client'

type Tariff = { index: number; classification: number; initialPeriod: number; initialCharge: number; additionalPeriod: number; additionalCharge: number }
type RateMapping = { sourceIndex: number; exchangeCount: number; targetOptions: number[]; targetIndex: number | null; sourceTariff: Tariff }
type AreaMapping = { sourceRecord: number; areaCode: string; targetOptions: number[]; targetRecord: number | null;
  replacesExistingArea: boolean; ratesMatched: boolean; changedExchangeCodes: number; changedRateTuples: number }
type Preview = { canCreate: boolean; recordCount: number; rates: RateMapping[]; targetTariffs: Tariff[]; areas: AreaMapping[];
  changedRecords: number[]; preservedTargetRecords: number[]; findings: { blocking: boolean; recordNumber: number | null; message: string }[]; previewToken: string }

const classes = ['Host-rated intra-area toll', 'Local measured service', 'Fixed local charge', 'Rate unavailable', 'Invalid number',
  'Intra-area toll', 'Inter-area toll', 'Host-rated inter-area toll', 'Host-rated local', 'International toll']
const classify = (value: number) => classes[value] ?? (value === 12 ? 'Local data-port call' : `Unknown (${value})`)
const period = (value: number) => value === -1 ? 'Unlimited' : `${value} s`
const summary = (tariff: Tariff) => `${classify(tariff.classification)}: ${tariff.initialCharge}¢ / ${period(tariff.initialPeriod)}, then ${tariff.additionalCharge}¢ / ${period(tariff.additionalPeriod)}`
const mt = (numbers: number[]) => numbers.map(number => `MT${number}`).join(', ')

export function FirmwareLocalDialingStagingPanel({ phoneId, targetId, candidateId, parentBusy, onBusyChange, onDraftCreated, canAdd }: {
  phoneId: string; targetId: string; candidateId: string; parentBusy: boolean; onBusyChange: (busy: boolean) => void
  onDraftCreated: (id: string, name: string) => void; canAdd: boolean
}) {
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [preview, setPreview] = useState<Preview | null>(null)
  const [current, setCurrent] = useState(false)
  const [rateChoices, setRateChoices] = useState<Record<string, number>>({})
  const [areaChoices, setAreaChoices] = useState<Record<string, number>>({})
  const [error, setError] = useState<string | null>(null)
  const [created, setCreated] = useState<{ id: string; name: string } | null>(null)

  async function request(apply: boolean) {
    setBusy(true); onBusyChange(true); setError(null); setCreated(null)
    if (!apply) setCurrent(false)
    try {
      const response = await apiPostRaw(`/api/firmware/transition-local-dialing${apply ? '' : '/preview'}`, {
        terminalId: phoneId, targetFirmwareVersionId: targetId, candidateSetId: candidateId, name: name.trim(), rateChoices, areaChoices,
        previewToken: apply ? preview?.previewToken : undefined,
      })
      const json = await response.json().catch(() => null)
      if (!response.ok || !json) throw new Error(json?.error ?? `Local dialing draft request failed (HTTP ${response.status}).`)
      if (apply) { setCreated({ id: json.id, name: name.trim() }); setPreview(null); setCurrent(false) }
      else { setPreview(json as Preview); setCurrent(true) }
    } catch (e) { setError(e instanceof Error ? e.message : 'Unable to prepare the target local dialing records.'); setCurrent(false) }
    finally { setBusy(false); onBusyChange(false) }
  }

  function choose(kind: 'rate' | 'area', id: number, value: string) {
    const setter = kind === 'rate' ? setRateChoices : setAreaChoices
    setter(before => { const next = { ...before }; if (value === '') delete next[id]; else next[id] = Number(value); return next })
    setCurrent(false); setCreated(null)
  }
  const disabled = busy || parentBusy
  return <section aria-label="Carry forward local dialing" style={{ marginTop: 18 }}>
    <h3>Prepare phone-specific local dialing areas</h3>
    <p>Earlier single-compressed segments contain 800 exchanges with 16 possible rate indexes. Later double-compressed segments have the same exchange count, but only four rate indexes. Every used source tariff must match one of target MT73 indexes 0 to 3 byte for byte. Unrelated target areas and all target tariff bytes remain unchanged.</p>
    <label>New local dialing draft name <input value={name} maxLength={120} disabled={disabled} onChange={e => {
      setName(e.target.value); setCurrent(false); setCreated(null)
    }} /></label>{' '}
    <button type="button" disabled={disabled || !name.trim()} onClick={() => void request(false)}>{busy ? 'Working…' : 'Preview local dialing carry-forward'}</button>
    {error && <p role="alert">{error}</p>}
    {preview && <>
      <p><strong>{preview.areas.length} source {preview.areas.length === 1 ? 'area' : 'areas'}, {preview.rates.length} source tariff {preview.rates.length === 1 ? 'index' : 'indexes'}.</strong> {preview.changedRecords.length > 0
        ? `Target records to change: ${mt(preview.changedRecords)}.` : 'No target segment byte changes proposed.'}</p>
      <h4>Local tariff equivalence</h4>
      <div style={{ overflowX: 'auto' }}><table cellPadding={7} style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead><tr><th>Source index</th><th>Tariff tuple</th><th>Exchanges</th><th>Exact target index</th></tr></thead>
        <tbody>{preview.rates.map(rate => <tr key={rate.sourceIndex}>
          <th scope="row">{rate.sourceIndex}</th><td>{summary(rate.sourceTariff)}</td><td>{rate.exchangeCount}</td>
          <td>{rate.targetOptions.length === 0 ? 'No exact local match' : <select aria-label={`Local target rate for source ${rate.sourceIndex}`} disabled={disabled}
            value={rateChoices[rate.sourceIndex] ?? rate.targetIndex ?? ''} onChange={e => choose('rate', rate.sourceIndex, e.target.value)}>
            <option value="">Use suggested index</option>{rate.targetOptions.map(index => <option key={index} value={index}>{index}</option>)}
          </select>}</td>
        </tr>)}</tbody></table></div>
      <details><summary>Candidate local tariffs (4 positions)</summary><ul>{preview.targetTariffs.map(tariff => <li key={tariff.index}>Rate {tariff.index}: {summary(tariff)}</li>)}</ul></details>
      <h4>Area migration</h4>
      <div style={{ overflowX: 'auto' }}><table cellPadding={7} style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead><tr><th>Source segment</th><th>Area code</th><th>Target segment</th><th>Operation</th><th>Changed codes / rate tuples</th></tr></thead>
        <tbody>{preview.areas.map(area => <tr key={area.sourceRecord}>
          <th scope="row">MT{area.sourceRecord}</th><td>{area.areaCode}</td>
          <td>{area.targetOptions.length === 0 ? 'No available segment' : <select aria-label={`Target segment for MT${area.sourceRecord}`} disabled={disabled}
            value={areaChoices[area.sourceRecord] ?? area.targetRecord ?? ''} onChange={e => choose('area', area.sourceRecord, e.target.value)}>
            <option value="">Use suggested segment</option>{area.targetOptions.map(number => <option key={number} value={number}>MT{number}</option>)}
          </select>}</td>
          <td>{area.targetRecord == null ? 'Resolve capacity' : !area.ratesMatched ? 'Resolve local tariff match' : area.replacesExistingArea ? 'Replace same area' : 'Use vacant segment'}</td>
          <td>{area.ratesMatched ? `${area.changedExchangeCodes} / ${area.changedRateTuples}` : 'Not computed'}</td>
        </tr>)}</tbody></table></div>
      {preview.preservedTargetRecords.length > 0 && <p>Target local records retained unchanged: {mt(preview.preservedTargetRecords)}.</p>}
      <ul>{preview.findings.map((finding, index) => <li key={index}><strong>{finding.blocking ? 'Action required' : 'Review'}{finding.recordNumber != null ? `, MT${finding.recordNumber}` : ''}:</strong> {finding.message}</li>)}</ul>
      {!current ? <p role="status">Preview again to confirm your changes.</p> : !preview.canCreate ? <p role="status">Resolve the blocking findings before creating a draft.</p> : !canAdd ?
        <p>Table set creation permission is required to save this draft.</p> : <button type="button" disabled={disabled} onClick={() => {
          if (window.confirm('Create this separate unassigned local dialing draft? Confirm the listed area replacements and review all companion records before publication.')) void request(true)
        }}>Create local dialing target draft</button>}
    </>}
    {created && <p>Local dialing draft created, not assigned. <Link to={`/table-sets/${created.id}`}>Open local dialing draft</Link>.{' '}
      <button type="button" disabled={disabled} onClick={() => onDraftCreated(created.id, created.name)}>Use as target candidate for further review</button></p>}
  </section>
}
