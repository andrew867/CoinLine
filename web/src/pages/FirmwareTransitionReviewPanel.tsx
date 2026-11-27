import { FirmwareTariffStagingPanel } from './FirmwareTariffStagingPanel'
import { useState } from 'react'
import { FirmwareLocalDialingStagingPanel } from './FirmwareLocalDialingStagingPanel'
import { Link } from 'react-router-dom'
import { apiGet, apiPostRaw } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { FirmwareScreeningStagingPanel } from './FirmwareScreeningStagingPanel'
import { FirmwareCarrierStagingPanel } from './FirmwareCarrierStagingPanel'

type Phone = { id: string; displayName: string }
type Version = { id: string; label: string; tableProfileId: string | null }
type Set = { id: string; name: string; status: number; targetFirmwareVersionId: string | null }
type Finding = { blocking: boolean; tableNumber: number | null; message: string }
type RecordReview = { tableNumber: number; sourceBytes: number | null; targetExpectedBytes: number | null; candidateBytes: number | null;
  action: string; detail: string; targetAcknowledged: boolean; requiresLayoutReview: boolean }
type Review = { sourceFirmwareLabel: string | null; targetFirmwareLabel: string; sourceSetId: string | null; sourceSetName: string | null;
  sourceProfileId?: string | null; targetProfileId?: string | null;
  candidateSetId: string | null; candidateSetName: string | null; candidatePublished: boolean; activeSessions: number; outstandingDownloads: number;
  configurationReadyForReview: boolean; upgradeQualified: boolean; records: RecordReview[]; findings: Finding[] }
type AccessPreview = { sourceSetId: string; candidateSetId: string; sourceBytes: number; targetBytes: number; changedBytes: number; recordCount: number; previewToken: string }

const actions: Record<string, string> = {
  'invalid-candidate': 'Invalid candidate', 'retire-from-target': 'Review retirement', 'unsupported-candidate': 'Unsupported record',
  'unverified-target': 'Target layout unverified', 'replace-candidate': 'Replace candidate', 'replace-required': 'Replacement needed',
  'review-source-omission': 'Review missing source feature', 'available-unassigned': 'Available, applicability unverified',
  'candidate-addition': 'New target record', 'candidate-replacement': 'Replacement supplied',
  'review-native-layout': 'Review field layout', 'candidate-unchanged': 'Same bytes and identified layout', 'candidate-changed': 'Changed target record',
}

const earlierTariffProfiles = new Set(['mtr19-multipay-200', 'mtr19-desk-200-single-compressed', 'mtr113-multipay-200-single-compressed', 'mtr20-early-multipay-200-single-compressed'])
const laterTariffProfiles = new Set(['mtr2x-multipay-180-double-compressed', 'mtr2x-multipay-graphical-180-double-compressed',
  'mtr120-multipay-180-double-compressed', 'mtr120-coin-basic-180-double-compressed', 'mtr211-multipay-180-double-compressed'])

export function FirmwareTransitionReviewPanel({ phones }: { phones: Phone[] }) {
  const [open, setOpen] = useState(false)
  const [loadingOptions, setLoadingOptions] = useState(false)
  const [versions, setVersions] = useState<Version[]>([])
  const [sets, setSets] = useState<Set[]>([])
  const [phoneId, setPhoneId] = useState('')
  const [targetId, setTargetId] = useState('')
  const [candidateId, setCandidateId] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [review, setReview] = useState<Review | null>(null)
  const [draftName, setDraftName] = useState('')
  const [accessPreview, setAccessPreview] = useState<AccessPreview | null>(null)
  const [createdDraft, setCreatedDraft] = useState<string | null>(null)
  const [stageError, setStageError] = useState<string | null>(null)

  function resetStage() { setAccessPreview(null); setCreatedDraft(null); setStageError(null) }

  async function loadOptions() {
    setOpen(true); setLoadingOptions(true); setError(null)
    try {
      const [v, s] = await Promise.all([apiGet<Version[]>('/api/firmware/versions'), apiGet<Set[]>('/api/tables/sets')])
      setVersions(v); setSets(s); setPhoneId(phones[0]?.id || '')
    } catch (e) { setError(e instanceof Error ? e.message : 'Unable to load transition options.') }
    finally { setLoadingOptions(false) }
  }

  async function preview() {
    setBusy(true); setError(null); setReview(null); resetStage()
    try {
      const query = new URLSearchParams({ terminalId: phoneId, targetFirmwareVersionId: targetId })
      if (candidateId) query.set('candidateSetId', candidateId)
      setReview(await apiGet<Review>(`/api/firmware/transition-review?${query}`))
    } catch (e) { setError(e instanceof Error ? e.message : 'Unable to review the transition.') }
    finally { setBusy(false) }
  }

  async function stageAccess(apply: boolean) {
    setBusy(true); setStageError(null)
    if (!apply) { setAccessPreview(null); setCreatedDraft(null) }
    try {
      const body = { terminalId: phoneId, targetFirmwareVersionId: targetId, candidateSetId: candidateId,
        name: draftName.trim(), previewToken: apply ? accessPreview?.previewToken : undefined }
      const response = await apiPostRaw(`/api/firmware/transition-access${apply ? '' : '/preview'}`, body)
      const json = await response.json().catch(() => null)
      if (!response.ok || !json) throw new Error(json?.error ?? `Target draft request failed (HTTP ${response.status}).`)
      if (apply) { setCreatedDraft(json.id); setAccessPreview(null) }
      else setAccessPreview(json as AccessPreview)
    } catch (e) {
      setStageError(e instanceof Error ? e.message : 'Unable to prepare the target draft.')
      if (apply) setAccessPreview(null)
    } finally { setBusy(false) }
  }

  return <section style={{ border: '1px solid #ddd', borderRadius: 8, marginBottom: 20, padding: 16 }}>
    <h2 style={{ marginTop: 0 }}>Configuration transition review</h2>
    <p>Compare the phone's assigned configuration with a different registered firmware version. Review replacements and new records before planning an upgrade. This tool does not schedule a transfer, change an assignment, or certify compatibility.</p>
    {!open ? <button type="button" onClick={() => void loadOptions()}>Review a firmware transition</button> : <>
      {loadingOptions ? <p role="status">Loading firmware and configuration choices…</p> : <form onSubmit={e => { e.preventDefault(); void preview() }}>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 14, marginBottom: 12 }}>
          <label>Source phone<select style={{ display: 'block' }} value={phoneId} disabled={busy} onChange={e => { setPhoneId(e.target.value); setReview(null); resetStage() }}>
            <option value="">Choose a phone</option>
            {phones.map(phone => <option key={phone.id} value={phone.id}>{phone.displayName}</option>)}</select></label>
          <label>Target firmware<select style={{ display: 'block' }} value={targetId} disabled={busy} onChange={e => { setTargetId(e.target.value); setCandidateId(''); setReview(null); resetStage() }}>
            <option value="">Choose a registered version</option>{versions.map(version => <option key={version.id} value={version.id}>
              {version.label}{version.tableProfileId ? '' : ' (layout not assigned)'}</option>)}</select></label>
          <label>Prepared target configuration<select style={{ display: 'block' }} value={candidateId} disabled={busy || !targetId} onChange={e => { setCandidateId(e.target.value); setReview(null); resetStage() }}>
            <option value="">None, review source only</option>{sets.filter(set => set.targetFirmwareVersionId === targetId).map(set => <option key={set.id} value={set.id}>
              {set.name} ({set.status === 1 ? 'published' : 'draft'})</option>)}</select></label>
        </div>
        <button type="submit" disabled={!phoneId || !targetId || busy}>{busy ? 'Reviewing…' : 'Compare configurations'}</button>
      </form>}
      {error && <p role="alert" style={{ color: 'crimson' }}>{error} <button type="button" onClick={() => void loadOptions()}>Reload choices</button></p>}
      {review && <div style={{ marginTop: 20 }}>
        <h3>{review.sourceFirmwareLabel ?? 'Unknown installed version'} → {review.targetFirmwareLabel}</h3>
        <p>Assigned source: {review.sourceSetName ?? 'Not assigned'}. Target candidate: {review.candidateSetName ?? 'Not selected'}{review.candidateSetName ? (review.candidatePublished ? ' (published)' : ' (draft)') : ''}.</p>
        <p role="status"><strong>{review.configurationReadyForReview ? 'Configuration checks passed for further review.' : 'Configuration preparation required.'}</strong>{' '}
          {review.upgradeQualified ? 'Upgrade qualified.' : 'Remote upgrade is not qualified.'} Active sessions: {review.activeSessions}; outstanding downloads: {review.outstandingDownloads}.</p>
        <ul>{review.findings.map((finding, index) => <li key={index}>
          <strong>{finding.blocking ? 'Action required' : 'Review'}{finding.tableNumber ? `, MT${finding.tableNumber}` : ''}:</strong> {finding.message}</li>)}</ul>
        <div style={{ overflowX: 'auto' }}><table cellPadding={7} style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead><tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}><th>Record</th><th>Source bytes</th><th>Target bytes</th><th>Candidate bytes</th><th>Review</th><th>Target evidence</th></tr></thead>
          <tbody>{review.records.map(record => <tr key={record.tableNumber} style={{ borderBottom: '1px solid #eee' }}>
            <th scope="row">MT{record.tableNumber}</th><td>{record.sourceBytes ?? 'Not assigned'}</td><td>{record.targetExpectedBytes ?? 'Unknown'}</td>
            <td>{record.candidateBytes ?? 'Not supplied'}</td><td><strong>{actions[record.action] ?? record.action}</strong><br />{record.detail}</td>
            <td>{record.targetAcknowledged ? 'Previously acknowledged' : 'Not acknowledged in catalogue'}{record.requiresLayoutReview && <><br />Field layout needs review</>}</td>
          </tr>)}</tbody>
        </table></div>
        {review.sourceSetId && review.candidateSetId && earlierTariffProfiles.has(review.sourceProfileId ?? '') &&
          laterTariffProfiles.has(review.targetProfileId ?? '') && review.records.some(record => record.tableNumber === 73 &&
          record.sourceBytes === 1192 && record.targetExpectedBytes === 1192 && record.candidateBytes === 1192) &&
          <FirmwareTariffStagingPanel key={`${phoneId}-${targetId}-${candidateId}`} phoneId={phoneId} targetId={targetId} candidateId={candidateId}
            parentBusy={busy} onBusyChange={setBusy} canAdd={canPerform('table-sets', op.add)} onDraftCreated={(id, name) => {
              setSets(previous => [...previous, { id, name, status: 0, targetFirmwareVersionId: targetId }])
              setCandidateId(id); setReview(null); resetStage()
            }} />}
        {review.sourceSetId && review.candidateSetId && canPerform('table-sets', op.add) && review.records.some(record =>
          record.tableNumber === 21 && record.targetExpectedBytes === record.candidateBytes &&
          [18, 48].includes(record.sourceBytes ?? 0) && [18, 48].includes(record.candidateBytes ?? 0)) &&
          <section aria-label="Carry forward management access" style={{ marginTop: 18 }}>
            <h3>Prepare phone-specific management access</h3>
            <p>Copy the reviewed target candidate into a new draft using the assigned phone's MT21 identity, primary and alternate manager numbers. Target ADSI identifiers and every other record remain as supplied by the candidate. This does not assign, publish, download or qualify the upgrade.</p>
            <label>New target draft name <input value={draftName} maxLength={120} disabled={busy} onChange={e => { setDraftName(e.target.value); resetStage() }} /></label>{' '}
            <button type="button" disabled={busy || !draftName.trim()} onClick={() => void stageAccess(false)}>Preview management-access carry-forward</button>
            {stageError && <p role="alert">{stageError}</p>}
            {accessPreview && <>
              <p>MT21: {accessPreview.sourceBytes} bytes to {accessPreview.targetBytes} bytes; {accessPreview.changedBytes} changed target bytes. {accessPreview.recordCount - 1} other records retained unchanged.</p>
              <button type="button" disabled={busy} onClick={() => { if (window.confirm('Create this unassigned target draft?')) void stageAccess(true) }}>Create target draft</button>
            </>}
            {createdDraft && <p>Target draft created, not assigned. <Link to={`/table-sets/${createdDraft}`}>Open target draft and review remaining records</Link>.</p>}
          </section>}
        {review.sourceSetId && review.candidateSetId && canPerform('table-sets', op.add) && review.records.some(record =>
          record.tableNumber === 92 && record.sourceBytes === 3401 && record.targetExpectedBytes === 3061 && record.candidateBytes === 3061) &&
          <FirmwareScreeningStagingPanel key={`${phoneId}-${targetId}-${candidateId}`} phoneId={phoneId} targetId={targetId}
            candidateId={candidateId} parentBusy={busy} onBusyChange={setBusy} onDraftCreated={(id, name) => {
              setSets(previous => [...previous, { id, name, status: 0, targetFirmwareVersionId: targetId }])
              setCandidateId(id); setReview(null); resetStage()
            }} />}
      </div>}
      {review && review.sourceSetId && review.candidateSetId &&
          [[22,661],[23,678],[92,3401]].every(([number,bytes]) => review.records.some(record => record.tableNumber === number && record.sourceBytes === bytes)) &&
          [[134,1153],[135,1109],[92,3061]].every(([number,bytes]) => review.records.some(record => record.tableNumber === number && record.targetExpectedBytes === bytes && record.candidateBytes === bytes)) &&
          <FirmwareCarrierStagingPanel key={`${phoneId}-${targetId}-${candidateId}`} phoneId={phoneId} targetId={targetId} candidateId={candidateId}
            parentBusy={busy} onBusyChange={setBusy} canAdd={canPerform('table-sets', op.add)} onDraftCreated={(id, name) => {
              setSets(previous => [...previous, { id, name, status: 0, targetFirmwareVersionId: targetId }]); setCandidateId(id); setReview(null); resetStage()
            }} />}
      {review && review.sourceSetId && review.candidateSetId && review.records.some(record => record.tableNumber >= 101 && record.tableNumber <= 115 && record.sourceBytes === 403) &&
          review.records.some(record => ((record.tableNumber >= 136 && record.tableNumber <= 149) || [154, 155].includes(record.tableNumber)) && record.targetExpectedBytes === 203) &&
          review.records.some(record => record.tableNumber === 73 && record.sourceBytes === 1192 && record.candidateBytes === 1192) &&
          <FirmwareLocalDialingStagingPanel key={`${phoneId}-${targetId}-${candidateId}`} phoneId={phoneId} targetId={targetId} candidateId={candidateId}
            parentBusy={busy} onBusyChange={setBusy} canAdd={canPerform('table-sets', op.add)} onDraftCreated={(id, name) => {
              setSets(previous => [...previous, { id, name, status: 0, targetFirmwareVersionId: targetId }]); setCandidateId(id); setReview(null); resetStage()
            }} />}
    </>}
  </section>
}
