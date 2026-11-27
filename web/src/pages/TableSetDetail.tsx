import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { apiFetch, apiPost, apiPostBareOk, apiPut } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { TableSetAssignment } from './TableSetAssignment'
import type { FirmwareSchedule } from './TableSetAssignment'
import { TableSetConfiguration } from './TableSetConfiguration'
import { TableRecordCopy } from './TableRecordCopy'
import { TableDownloadScheduleList } from './TableDownloadScheduleList'
import { FirmwareVersionSelect } from '../components/FirmwareVersionSelect'
import { TerminalFamilyReview } from './TerminalFamilyReview'
import { PortableTerminalConfigurationPanel } from './PortableTerminalConfigurationPanel'
import './TableSetConfiguration.css'


type Ver = {
  id: string
  tableDefinitionId: string
  definitionName: string
  tableNumber: number
  dependsOnTableDefinitionId: string | null
  tableRevision: number
  configurationId: string | null
  payloadSha256Hex: string
  payloadLengthBytes: number
  sortOrder: number
  validationPassed: boolean
  validationDiagnosticsJson: string | null
  warnings: string[]
}

type Detail = {
  id: string
  name: string
  customerId: string | null
  isDefault: boolean
  status: number
  publishedAtUtc: string | null
  publishGeneration: number
  revision: number
  optionalRecordRemovals: { tableNumber: number; name: string }[]
  targetFirmwareVersionId: string | null
  targetFirmware: string | null
  tableProfile: string | null
  tableProfileEvidence?: { recordCount: number; terminalAcknowledgedCount: number; completeCatalogue: boolean;
    observedLengthsOnly?: boolean; evidenceNote?: string | null } | null
  firmwareFindings: { tableNumber: number; blocking: boolean; message: string }[]
  firmwareSchedule?: FirmwareSchedule | null
  versions: Ver[]
  provisioning?: {
    operatingType: string | null
    acceptedCoinSlots: number | null
    configuredCardRanges: number | null
    onHookAdvertising: boolean | null
    graphicalOnHookAdvertising?: boolean | null
    visibleOnHookTextMessages?: number | null
    findings: { tableNumber: number; message: string }[]
  }
}

export function TableSetDetail() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const initialRecord = Number(searchParams.get('record') ?? 29)
  const initialFamily = searchParams.get('family') ?? ''
  const initialSchedulePhone = searchParams.get('schedulePhone') ?? ''
  const initialScheduleRecord = searchParams.has('scheduleRecord') ? Number(searchParams.get('scheduleRecord')) : undefined
  const initialRandomizePhone = searchParams.get('randomizePhone') ?? ''
  const [d, setD] = useState<Detail | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [copying, setCopying] = useState(false)
  const [targetEdit, setTargetEdit] = useState<string | null>(null)
  const [savingTarget, setSavingTarget] = useState(false)
  const [recordDirty, setRecordDirty] = useState(false)
  const [reviewFamily, setReviewFamily] = useState(initialFamily)
  const currentId = useRef(id)
  const requestRef = useRef<AbortController | null>(null)
  currentId.current = id
  const canRead = canPerform('table-sets', op.read)
  const canUpdate = canPerform('table-sets', op.update)
  const canAdd = canPerform('table-sets', op.add)
  const canReadRecords = canPerform('table-records', op.read)

  const load = () => {
    if (!id || id !== currentId.current || !canRead) return
    requestRef.current?.abort()
    const request = new AbortController()
    requestRef.current = request
    setErr(null)
    apiFetch(`/api/tables/sets/${id}`, { signal: request.signal })
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Detail>
      })
      .then((j) => {
        if (request.signal.aborted || currentId.current !== id) return
        setD(j)
        setName(j.name)
      })
      .catch((e) => {
        if (!request.signal.aborted && currentId.current === id)
          setErr(e instanceof Error ? e.message : 'error')
      })
  }

  useEffect(() => {
    setTargetEdit(null)
    setRecordDirty(false)
    load()
    return () => requestRef.current?.abort()
  }, [id, canRead])
  useEffect(() => { setReviewFamily(initialFamily) }, [id, initialFamily])

  async function saveTarget(e: React.FormEvent) {
    e.preventDefault()
    if (!canUpdate || !d || targetEdit == null || recordDirty) return
    setSavingTarget(true)
    setErr(null)
    try {
      await apiPut(`/api/tables/sets/${d.id}/firmware-target`, { firmwareVersionId: targetEdit || null })
      load()
    } catch (error) {
      setErr(error instanceof Error ? error.message : 'Could not save target firmware.')
    } finally {
      setSavingTarget(false)
    }
  }

  async function save(e: React.FormEvent) {
    e.preventDefault()
    if (!id || !canUpdate) return
    setErr(null)
    try {
      await apiPut(`/api/tables/sets/${id}`, {
        name,
        customerId: d?.customerId ?? null,
        isDefault: d?.isDefault ?? false,
      })
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function publish() {
    if (!id || !canUpdate) return
    if (
      !window.confirm(
        'Publish this table set? Published sets affect which payloads terminals may download (validate on bench — HARDWARE_VALIDATION_REQUIRED).'
      )
    )
      return
    setErr(null)
    try {
      await apiPostBareOk(`/api/tables/sets/${id}/publish`, { confirm: true })
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function clone(event: React.FormEvent) {
    event.preventDefault()
    if (!id || !canAdd) return
    setCopying(true)
    setErr(null)
    try {
      const copy = await apiPost<{ id: string }>(`/api/tables/sets/${id}/clone`, { name })
      setD(null)
      void navigate(`/table-sets/${copy.id}${initialRecord === 29 ? '' : `?record=${initialRecord}`}`)
    } catch (error) {
      setErr(error instanceof Error ? error.message : 'Could not make a draft copy.')
    } finally {
      setCopying(false)
    }
  }

  if (!id) return <p>Missing id.</p>
  if (!canRead) return <p role="alert">You do not have permission to view table sets.</p>
  if (!d || d.id !== id) return err ? <p style={{ color: 'crimson' }}>{err}</p> : <p>Loading…</p>

  const draft = d.status !== 1
  const codeOnly = d.versions.length === 1 && d.versions[0].tableNumber === 94
  const canPreviewScope = canPerform('table-assignments', op.read) || canPerform('table-assignments', op.update) ||
    canPerform('downloads', op.add) || (!codeOnly && (canReadRecords || canPerform('table-records', op.update)))
  const noVersions = d.versions.length === 0
  const canRemoveRecords = draft && canPerform('table-records', op.delete)
  const targetSelection = targetEdit ?? d.targetFirmwareVersionId ?? ''
  const targetChanged = targetSelection !== (d.targetFirmwareVersionId ?? '')

  return (
    <div>
      <p>
        <Link to="/table-sets">← Table sets</Link>
      </p>
      <h1>Table set: {d.name}</h1>
      {err && <p style={{ color: 'crimson' }}>Action failed: {err}</p>}
      <p>
        Status: <strong>{draft ? 'Draft' : 'Published'}</strong>
        {d.publishedAtUtc && (
          <>
            {' '}
            at {new Date(d.publishedAtUtc).toLocaleString()} (gen {d.publishGeneration})
          </>
        )}
      </p>
      {draft && canUpdate && (
        <form onSubmit={save} style={{ marginBottom: 16, display: 'flex', gap: 8, alignItems: 'center' }}>
          <label>
            Name{' '}
            <input value={name} onChange={(e) => setName(e.target.value)} />
          </label>
          <button type="submit">Save</button>
          <button type="button" onClick={() => void publish()}
            disabled={recordDirty || targetChanged || d.firmwareFindings?.some((finding) => finding.blocking)}>
            Publish set
          </button>
        </form>
      )}
      {!draft && canAdd && (
        <form className="table-config-selectors" onSubmit={(event) => void clone(event)}>
          <label>Draft copy name<input value={name} maxLength={120} onChange={(event) => setName(event.target.value)} /></label>
          <button type="submit" disabled={copying || !name.trim()}>Make draft copy</button>
        </form>
      )}
      <section className="table-config-panel" aria-labelledby="firmware-target-heading">
        <h2 id="firmware-target-heading">Firmware compatibility</h2>
        {draft && canUpdate ? (
          <form className="table-config-selectors" onSubmit={(event) => void saveTarget(event)}>
            <FirmwareVersionSelect label="Target firmware" value={targetSelection} onChange={setTargetEdit}
              emptyLabel="No firmware target" disabled={savingTarget} optionsPath="/api/tables/firmware-options" />
            <button type="submit" disabled={!targetChanged || savingTarget || recordDirty}>Save target</button>
          </form>
        ) : <p>Target firmware <strong>{d.targetFirmware ?? 'Any'}</strong></p>}
        {draft && recordDirty && targetChanged && <p>Save the record edits before changing the target.</p>}
        {d.targetFirmware && <p>Table layout <strong>{d.tableProfile ?? 'Unverified'}</strong></p>}
        {d.tableProfileEvidence && <p className="page-intro">{d.tableProfileEvidence.recordCount} known record lengths.
          {' '}{d.tableProfileEvidence.terminalAcknowledgedCount} acknowledged by emulated terminals.
          {d.tableProfileEvidence.completeCatalogue && ' Records outside the complete target catalogue cannot be published for this target.'}
          {d.tableProfileEvidence.observedLengthsOnly && ' Observed subset only; the full catalogue and ambiguous native layouts are not established.'}
          {d.tableProfileEvidence.evidenceNote && ` ${d.tableProfileEvidence.evidenceNote}`}</p>}
        {!!d.firmwareFindings?.length && <ul>{d.firmwareFindings.map((finding) => (
          <li key={`${finding.tableNumber}:${finding.message}`}><strong>MT{finding.tableNumber} {finding.blocking ? 'needs correction' : 'needs verification'}.</strong> {finding.message}
            {canReadRecords && !recordDirty && d.versions.some(version => version.tableNumber === finding.tableNumber) &&
              <> <Link to={`/table-sets/${id}?record=${finding.tableNumber}#config-heading`}>Review MT{finding.tableNumber}</Link></>}
          </li>
        ))}</ul>}
      </section>
      {canReadRecords && <PortableTerminalConfigurationPanel key={`portable:${d.id}`} setId={d.id}
        supported={!!d.tableProfileEvidence && !d.tableProfileEvidence.observedLengthsOnly}
        disabled={recordDirty || targetChanged} onCreated={created => navigate(`/table-sets/${created}`)} />}
      <TerminalFamilyReview key={`${id}:${initialFamily}`} setId={id} initialFamily={initialFamily}
        dataKey={`${d.revision}:${d.targetFirmwareVersionId}:${d.versions.map(version => `${version.tableNumber}:${version.payloadSha256Hex}`).join(',')}`}
        recordDirty={recordDirty} onFamilyChange={setReviewFamily} />
      {d.provisioning && (d.provisioning.operatingType || d.provisioning.acceptedCoinSlots !== null ||
        d.provisioning.configuredCardRanges !== null || d.provisioning.findings.length > 0) && (
        <section className="table-config-panel" aria-labelledby="provisioning-heading">
          <h2 id="provisioning-heading">Provisioning review</h2>
          <dl className="native-table-summary">
            {d.provisioning.operatingType && <div><dt>Operating type</dt><dd>{d.provisioning.operatingType}</dd></div>}
            {d.provisioning.acceptedCoinSlots !== null && <div><dt>Accepted coin slots</dt><dd>{d.provisioning.acceptedCoinSlots}</dd></div>}
            {d.provisioning.configuredCardRanges !== null && <div><dt>Configured card ranges</dt><dd>{d.provisioning.configuredCardRanges}</dd></div>}
            {d.provisioning.onHookAdvertising !== null && <div><dt>On-hook text advertising (MT26)</dt><dd>{d.provisioning.onHookAdvertising ? 'Enabled' : 'Disabled'}</dd></div>}
            {d.provisioning.graphicalOnHookAdvertising != null && <div><dt>On-hook graphical advertising (MT131)</dt><dd>{d.provisioning.graphicalOnHookAdvertising ? 'Enabled' : 'Disabled'}</dd></div>}
            {d.provisioning.visibleOnHookTextMessages != null && <div><dt>Visible idle text messages (MT29)</dt><dd>{d.provisioning.visibleOnHookTextMessages}</dd></div>}
          </dl>
          {d.provisioning.findings.length > 0 && <ul>{d.provisioning.findings.map((finding) => (
            <li key={`${finding.tableNumber}:${finding.message}`}>MT{finding.tableNumber} · {finding.message}
              {canReadRecords && !recordDirty && d.versions.some(version => version.tableNumber === finding.tableNumber) &&
                <> <Link to={`/table-sets/${id}?record=${finding.tableNumber}#config-heading`}>Review MT{finding.tableNumber}</Link></>}
            </li>
          ))}</ul>}
        </section>
      )}
      {draft && canUpdate && <TableRecordCopy key={d.id} setId={d.id} revision={d.revision} customerId={d.customerId}
        disabled={recordDirty || targetChanged} onSaved={load} />}
      {canReadRecords && <TableSetConfiguration key={`${id}:${d.targetFirmwareVersionId ?? ''}:${initialRecord}`} setId={id} draft={draft} initialRecord={initialRecord}
        versions={d.versions} onSaved={load} recordDirty={recordDirty} onRecordDirty={setRecordDirty} familyId={reviewFamily} />}
      {!draft && canPreviewScope && <TableSetAssignment key={`assignment:${id}:${initialSchedulePhone}:${initialScheduleRecord}:${initialRandomizePhone}`} setId={id} revision={d.revision} versions={d.versions} optionalRecords={d.optionalRecordRemovals}
        codeOnly={codeOnly} firmwareSchedule={d.firmwareSchedule} initialSchedulePhone={initialSchedulePhone} initialScheduleRecord={initialScheduleRecord} initialRandomizePhone={initialRandomizePhone} />}
      {!draft && !canPreviewScope && canPerform('downloads', op.read) && <TableDownloadScheduleList setId={id} />}
      {canReadRecords && <p>
        <Link to={`/table-versions?set=${encodeURIComponent(id)}`}>View versions list</Link>
      </p>}
      <h2>Versions in this set</h2>
      {noVersions && (
        <p style={{ color: '#666' }}>
          No versions in this set yet. Add table versions from the versions list, then publish this set.
        </p>
      )}
      <div className="table-set-versions" tabIndex={0} role="region" aria-label="Versions in this set">
        <table cellPadding={8} style={{ borderCollapse: 'collapse', fontSize: 14 }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>Table</th>
              <th>Configuration ID</th>
              <th>Rev</th>
              <th>SHA-256 (payload)</th>
              <th>Bytes</th>
              <th>Valid</th>
              <th>Warnings</th>
              {canRemoveRecords && <th>Draft actions</th>}
            </tr>
          </thead>
          <tbody>
            {d.versions.map((v) => (
              <tr key={v.id} style={{ borderBottom: '1px solid #eee', verticalAlign: 'top' }}>
                <td>{v.definitionName}</td>
                <td>{draft && canUpdate && v.tableNumber !== 94
                  ? <ConfigurationIdField key={v.tableRevision} setId={d.id} record={v} disabled={recordDirty || targetChanged} onSaved={load} />
                  : v.configurationId ?? 'None'}</td>
                <td>{v.tableRevision}</td>
                <td>
                  <code style={{ fontSize: 11 }} title="SHA-256 of opaque table payload bytes">
                    {v.payloadSha256Hex}
                  </code>
                </td>
                <td>{v.payloadLengthBytes}</td>
                <td>{v.validationPassed ? 'yes' : 'no'}</td>
                <td>
                  {v.warnings?.length ? (
                    <ul style={{ margin: 0, paddingLeft: 18, color: '#a50' }}>
                      {v.warnings.map((w) => (
                        <li key={w}>{w}</li>
                      ))}
                    </ul>
                  ) : (
                    ''
                  )}
                </td>
                {canRemoveRecords && <td><DraftRecordRemoval key={`${d.revision}:${v.id}:${v.tableRevision}`}
                  setId={d.id} setRevision={d.revision} record={v} disabled={recordDirty || targetChanged || name !== d.name} onSaved={load} /></td>}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function DraftRecordRemoval({ setId, setRevision, record, disabled, onSaved }: {
  setId: string; setRevision: number; record: Ver; disabled: boolean; onSaved: () => void
}) {
  const [review, setReview] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function remove() {
    if (!review || disabled || busy || !canPerform('table-records', op.delete)) return
    setBusy(true)
    setError('')
    try {
      const response = await apiFetch(`/api/tables/sets/${setId}/records/${record.id}/remove?confirm=true`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ setRevision, recordRevision: record.tableRevision, payloadSha256Hex: record.payloadSha256Hex }),
      })
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? 'Could not remove the draft record.')
      setReview(false)
      onSaved()
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Could not remove the draft record.')
    } finally { setBusy(false) }
  }

  return <div>
    {!review ? <button type="button" className="quiet" disabled={disabled}
      onClick={() => { setError(''); setReview(true) }}>Remove MT{record.tableNumber} from draft</button> :
      <section aria-label={`Remove MT${record.tableNumber} from draft review`}>
        <p>Remove {record.definitionName}, revision {record.tableRevision}, from this draft only? This does not change another set, a phone assignment or an installed record.</p>
        <button type="button" disabled={disabled || busy} onClick={() => void remove()}>Confirm removal</button>{' '}
        <button type="button" disabled={busy} onClick={() => { setReview(false); setError('') }}>Cancel</button>
      </section>}
    {error && <p role="alert">{error}</p>}
  </div>
}

function ConfigurationIdField({ setId, record, disabled, onSaved }: {
  setId: string; record: Ver; disabled: boolean; onSaved: () => void
}) {
  const original = record.configurationId ?? ''
  const [value, setValue] = useState(original)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const label = `Configuration ID for ${record.definitionName}`

  async function save(event: React.FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError('')
    try {
      const response = await apiFetch(`/api/tables/sets/${setId}/records/${record.tableNumber}/configuration-id`, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ recordRevision: record.tableRevision, configurationId: value || null }),
      })
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? 'Could not save the Configuration ID.')
      setValue(value.trimEnd().toUpperCase())
      onSaved()
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Could not save the Configuration ID.')
    } finally {
      setSaving(false)
    }
  }

  return <form onSubmit={event => void save(event)} className="row-actions">
    <input data-field-kind="key" aria-label={label} autoComplete="off" value={value} maxLength={6} size={6} disabled={disabled || saving}
      onChange={event => setValue(event.target.value)} />
    <button type="submit" disabled={disabled || saving || value === original}>Save ID</button>
    {error && <p role="alert">{error}</p>}
  </form>
}
