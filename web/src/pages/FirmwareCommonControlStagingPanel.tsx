import { useState } from 'react'
import { Link } from 'react-router-dom'
import { apiPostRaw } from '../api/client'

type Field = { key: string; label: string; source: string | null; target: string | null; status: string }
type Group = { group: { id: string; number: number; label: string; note: string }; selected: boolean; fields: Field[] }
type RecordReview = { number: number; status: string; groups: Group[]; changedBytes: number }
type Preview = { sourceSetId: string; candidateSetId: string; recordCount: number; canCreate: boolean; records: RecordReview[];
  changedBytes: number; findings: { blocking: boolean; number: number; message: string }[]; previewToken: string }

export function FirmwareCommonControlStagingPanel({ phoneId, targetId, candidateId, parentBusy, onBusyChange, onDraftCreated, canAdd }: {
  phoneId: string; targetId: string; candidateId: string; parentBusy: boolean; onBusyChange(value: boolean): void;
  onDraftCreated(id: string, name: string): void; canAdd: boolean
}) {
  const [name, setName] = useState('Reviewed common configuration transition')
  const [selected, setSelected] = useState<string[]>([])
  const [reviewed, setReviewed] = useState(false)
  const [showSame, setShowSame] = useState(false)
  const [preview, setPreview] = useState<Preview | null>(null)
  const [current, setCurrent] = useState(false)
  const [created, setCreated] = useState<{ id: string; name: string } | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const disabled = busy || parentBusy

  function invalidate() { setCurrent(false); setCreated(null); setError('') }
  function choose(id: string, include: boolean) {
    setSelected(previous => include ? [...previous, id] : previous.filter(value => value !== id))
    setReviewed(false); invalidate()
  }

  async function request(create: boolean) {
    if (create && (!current || !preview?.canCreate || !canAdd)) return
    setBusy(true); onBusyChange(true); setError(''); setCreated(null)
    try {
      const response = await apiPostRaw(`/api/firmware/transition-common-controls${create ? '' : '/preview'}`, {
        terminalId: phoneId, targetFirmwareVersionId: targetId, candidateSetId: candidateId, name: name.trim(),
        selections: [...selected].sort(), reviewed, ...(create ? { previewToken: preview?.previewToken } : {}),
      })
      const json = await response.json().catch(() => null)
      if (!response.ok || !json) throw new Error(json?.error ?? `Common configuration transition failed (HTTP ${response.status}).`)
      if (create) { setCreated({ id: json.id, name: name.trim() }); setPreview(null); setCurrent(false) }
      else { setPreview(json as Preview); setCurrent(true) }
    } catch (reason) { setCurrent(false); setError(reason instanceof Error ? reason.message : 'Unable to prepare common configuration controls.') }
    finally { setBusy(false); onBusyChange(false) }
  }

  return <section aria-label="Carry forward common configuration controls" style={{ marginTop: 18 }}>
    <h3>Prepare common configuration controls</h3>
    <p>Review shared text advertising, interface timing, reporting, routine call-in, coin settings and daily service restrictions. Choose only the groups intended for the target. Candidate-specific spare bytes, one-time clock adjustment, unselected hardware settings and separate restricted-route permissions are retained; nothing is assigned or downloaded.</p>
    <label>New common configuration draft name <input value={name} maxLength={120} disabled={disabled} onChange={event => { setName(event.target.value); invalidate() }} /></label>{' '}
    <button type="button" disabled={disabled || !name.trim()} onClick={() => void request(false)}>{busy ? 'Working…' : 'Preview common configuration carry-forward'}</button>
    {error && <p role="alert">{error}</p>}
    {preview && <>
      <p><strong>{selected.length} groups selected.</strong> {current ? `${preview.changedBytes} record bytes would change.` : 'Preview the updated choices to calculate changes.'} Every other candidate record is retained.</p>
      <button type="button" disabled={disabled || selected.length === 0} onClick={() => { setSelected([]); setReviewed(false); invalidate() }}>Clear common configuration selections</button>{' '}
      <label><input type="checkbox" checked={showSame} onChange={event => setShowSame(event.target.checked)} /> Include unchanged common values</label>
      {preview.records.map(record => <section key={record.number} aria-label={`MT${record.number} common controls`}>
        <h4>MT{record.number}{current && record.changedBytes > 0 ? ` · ${record.changedBytes} bytes would change` : ''}</h4>
        {record.status !== 'available' && <p>{record.status === 'missing-source' ? 'Earlier record missing or incompatible.' : 'Prepared target record missing or incompatible.'}</p>}
        {record.groups.map(row => <div key={row.group.id} style={{ marginBottom: 12 }}>
          <label><input type="checkbox" checked={selected.includes(row.group.id)} disabled={disabled || (record.status !== 'available' && !selected.includes(row.group.id))}
            onChange={event => choose(row.group.id, event.target.checked)} /> {row.group.label}</label>
          <p>{row.group.note}</p>
          {record.status === 'available' && <details>
            <summary>{row.fields.filter(field => field.status === 'different').length} different values; {row.fields.filter(field => field.status === 'unmapped').length} need review. Compare fields.</summary>
            <div style={{ overflowX: 'auto' }}><table cellPadding={7} style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead><tr><th>Setting</th><th>Source</th><th>Candidate</th><th>Review</th></tr></thead>
              <tbody>{row.fields.filter(field => showSame || field.status !== 'same').map(field => <tr key={field.key}>
                <th scope="row">{field.label}</th><td>{field.source ?? 'Unrecognized'}</td><td>{field.target ?? 'Unrecognized'}</td>
                <td>{field.status === 'same' ? 'Same' : field.status === 'different' ? selected.includes(row.group.id) ? 'Carry if reviewed' : 'Retain candidate' : 'Unmapped value'}</td>
              </tr>)}</tbody>
            </table></div>
          </details>}
        </div>)}
      </section>)}
      <ul>{preview.findings.map((finding, index) => <li key={index}><strong>{finding.blocking ? 'Action required' : 'Review'}{finding.number ? `, MT${finding.number}` : ''}:</strong> {finding.message}</li>)}</ul>
      <label><input type="checkbox" checked={reviewed} disabled={disabled} onChange={event => { setReviewed(event.target.checked); invalidate() }} /> I reviewed the selected controls and target hardware</label>
      {!current ? <p role="status">Preview again to confirm your choices and review.</p> : !preview.canCreate ? <p role="status">Resolve the blocking findings before creating a draft.</p> : !canAdd ? <p>Table set creation permission is required to save the draft.</p> :
        <button type="button" disabled={disabled} onClick={() => { if (window.confirm('Create this separate unassigned common configuration draft? It does not qualify or activate an upgrade.')) void request(true) }}>Create common configuration target draft</button>}
    </>}
    {created && <p>Common configuration draft created, not assigned. <Link to={`/table-sets/${created.id}`}>Open common configuration draft</Link>.{' '}
      <button type="button" disabled={disabled} onClick={() => onDraftCreated(created.id, created.name)}>Use as target candidate for further review</button></p>}
  </section>
}
