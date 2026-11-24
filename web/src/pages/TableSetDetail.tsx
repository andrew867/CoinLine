import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { apiFetch, apiPost, apiPostBareOk, apiPut } from '../api/client'
import { TableSetAssignment } from './TableSetAssignment'
import { TableSetConfiguration } from './TableSetConfiguration'
import { TableRecordCopy } from './TableRecordCopy'
import { FirmwareVersionSelect } from '../components/FirmwareVersionSelect'
import './TableSetConfiguration.css'


type Ver = {
  id: string
  tableDefinitionId: string
  definitionName: string
  tableNumber: number
  dependsOnTableDefinitionId: string | null
  tableRevision: number
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
  firmwareFindings: { tableNumber: number; blocking: boolean; message: string }[]
  versions: Ver[]
  provisioning?: {
    operatingType: string | null
    acceptedCoinSlots: number | null
    configuredCardRanges: number | null
    onHookAdvertising: boolean | null
    findings: { tableNumber: number; message: string }[]
  }
}

export function TableSetDetail() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const initialRecord = Number(searchParams.get('record') ?? 29)
  const [d, setD] = useState<Detail | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [copying, setCopying] = useState(false)
  const [targetEdit, setTargetEdit] = useState<string | null>(null)
  const [savingTarget, setSavingTarget] = useState(false)
  const [recordDirty, setRecordDirty] = useState(false)

  const load = () => {
    if (!id) return
    setErr(null)
    apiFetch(`/api/tables/sets/${id}`)
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Detail>
      })
      .then((j) => {
        setD(j)
        setName(j.name)
      })
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
  }

  useEffect(() => {
    setTargetEdit(null)
    setRecordDirty(false)
    load()
  }, [id])

  async function saveTarget(e: React.FormEvent) {
    e.preventDefault()
    if (!d || targetEdit == null || recordDirty) return
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
    if (!id) return
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
    if (!id) return
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
    if (!id) return
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
  if (!d) return err ? <p style={{ color: 'crimson' }}>{err}</p> : <p>Loading…</p>

  const draft = d.status !== 1
  const noVersions = d.versions.length === 0
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
      {draft && (
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
      {!draft && (
        <form className="table-config-selectors" onSubmit={(event) => void clone(event)}>
          <label>Draft copy name<input value={name} maxLength={120} onChange={(event) => setName(event.target.value)} /></label>
          <button type="submit" disabled={copying || !name.trim()}>Make draft copy</button>
        </form>
      )}
      <section className="table-config-panel" aria-labelledby="firmware-target-heading">
        <h2 id="firmware-target-heading">Firmware compatibility</h2>
        {draft ? (
          <form className="table-config-selectors" onSubmit={(event) => void saveTarget(event)}>
            <FirmwareVersionSelect label="Target firmware" value={targetSelection} onChange={setTargetEdit}
              emptyLabel="No firmware target" disabled={savingTarget} />
            <button type="submit" disabled={!targetChanged || savingTarget || recordDirty}>Save target</button>
          </form>
        ) : <p>Target firmware <strong>{d.targetFirmware ?? 'Any'}</strong></p>}
        {draft && recordDirty && targetChanged && <p>Save the record edits before changing the target.</p>}
        {d.targetFirmware && <p>Table layout <strong>{d.tableProfile ?? 'Unverified'}</strong></p>}
        {!!d.firmwareFindings?.length && <ul>{d.firmwareFindings.map((finding) => (
          <li key={finding.tableNumber}><strong>MT{finding.tableNumber} {finding.blocking ? 'needs correction' : 'needs verification'}.</strong> {finding.message}</li>
        ))}</ul>}
      </section>
      {d.provisioning && (d.provisioning.operatingType || d.provisioning.acceptedCoinSlots !== null ||
        d.provisioning.configuredCardRanges !== null || d.provisioning.findings.length > 0) && (
        <section className="table-config-panel" aria-labelledby="provisioning-heading">
          <h2 id="provisioning-heading">Provisioning review</h2>
          <dl className="native-table-summary">
            {d.provisioning.operatingType && <div><dt>Operating type</dt><dd>{d.provisioning.operatingType}</dd></div>}
            {d.provisioning.acceptedCoinSlots !== null && <div><dt>Accepted coin slots</dt><dd>{d.provisioning.acceptedCoinSlots}</dd></div>}
            {d.provisioning.configuredCardRanges !== null && <div><dt>Configured card ranges</dt><dd>{d.provisioning.configuredCardRanges}</dd></div>}
            {d.provisioning.onHookAdvertising !== null && <div><dt>On-hook advertising</dt><dd>{d.provisioning.onHookAdvertising ? 'Enabled' : 'Disabled'}</dd></div>}
          </dl>
          {d.provisioning.findings.length > 0 && <ul>{d.provisioning.findings.map((finding) => (
            <li key={`${finding.tableNumber}:${finding.message}`}>MT{finding.tableNumber} · {finding.message}</li>
          ))}</ul>}
        </section>
      )}
      {draft && <TableRecordCopy key={d.id} setId={d.id} revision={d.revision} customerId={d.customerId}
        disabled={recordDirty || targetChanged} onSaved={load} />}
      <TableSetConfiguration key={`${id}:${d.targetFirmwareVersionId ?? ''}:${initialRecord}`} setId={id} draft={draft} initialRecord={initialRecord}
        versions={d.versions} onSaved={load} recordDirty={recordDirty} onRecordDirty={setRecordDirty} />
      {!draft && <TableSetAssignment key={id} setId={id} revision={d.revision} versions={d.versions} optionalRecords={d.optionalRecordRemovals}
        codeOnly={d.versions.length === 1 && d.versions[0].tableNumber === 94} />}
      <p>
        <Link to={`/table-versions?set=${encodeURIComponent(id)}`}>View versions list</Link>
      </p>
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
              <th>Rev</th>
              <th>SHA-256 (payload)</th>
              <th>Bytes</th>
              <th>Valid</th>
              <th>Warnings</th>
            </tr>
          </thead>
          <tbody>
            {d.versions.map((v) => (
              <tr key={v.id} style={{ borderBottom: '1px solid #eee', verticalAlign: 'top' }}>
                <td>{v.definitionName}</td>
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
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
