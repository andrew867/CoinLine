import { useState } from 'react'
import { apiDownload, apiPostRaw } from '../api/client'
import { canPerform, consoleSession, permissionOperation as op } from '../api/consoleSession'

type Preview = {
  previewToken: string; canCreate: boolean; targetFirmware: string
  records: { number: number; action: string; sha256: string; length: number; retainedLocalFields: boolean }[]
  retainedNumbers: number[]; missingOmittedNumbers: number[]
  findings: { tableNumber: number; blocking: boolean; message: string }[]
}
const maximumFileBytes = 950_000
const numbers = (values: number[]) => values.length ? values.map(number => `MT${number}`).join(', ') : 'None'

export function PortableTerminalConfigurationPanel({ setId, disabled = false, supported, onCreated }: {
  setId: string; disabled?: boolean; supported: boolean; onCreated: (id: string) => void
}) {
  const mayRead = canPerform('table-sets', op.read) && canPerform('table-records', op.read)
  const mayCreate = consoleSession.role() !== 'Viewer' && canPerform('table-sets', op.add) && canPerform('table-records', op.add) && canPerform('table-records', op.update)
  const endpoint = `/api/tables/sets/${setId}/portable-package`
  const [open, setOpen] = useState(false)
  const [name, setName] = useState('')
  const [packageData, setPackageData] = useState<unknown>(null)
  const [fileName, setFileName] = useState('')
  const [preview, setPreview] = useState<Preview | null>(null)
  const [confirmed, setConfirmed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  function resetPreview() { setPreview(null); setConfirmed(false); setError('') }
  async function exportPackage() {
    if (!supported || disabled || busy) return
    setBusy(true); setError('')
    try { await apiDownload(endpoint, 'coinline-terminal-configuration.json') }
    catch (failure) { setError(failure instanceof Error ? failure.message : 'Unable to export the configuration package.') }
    finally { setBusy(false) }
  }
  async function selectFile(file?: File) {
    resetPreview(); setPackageData(null); setFileName('')
    if (!file) return
    if (file.size > maximumFileBytes) { setError('Choose a JSON package smaller than 950 KB.'); return }
    setBusy(true)
    try {
      const parsed: unknown = JSON.parse(await file.text())
      if (parsed == null || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('Choose a CoinLine JSON configuration package.')
      setPackageData(parsed); setFileName(file.name)
    } catch { setError('The selected file is not a readable CoinLine JSON configuration package.') }
    finally { setBusy(false) }
  }
  async function review() {
    if (!packageData || !name.trim() || disabled || busy) return
    setBusy(true); resetPreview()
    try {
      const response = await apiPostRaw(`${endpoint}/preview`, { name: name.trim(), package: packageData })
      const result = await response.json().catch(() => null)
      if (!response.ok || !result?.previewToken || !Array.isArray(result.records))
        throw new Error(result?.error ?? `Package preview failed (HTTP ${response.status}).`)
      setPreview(result as Preview)
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Could not preview the package.') }
    finally { setBusy(false) }
  }
  async function create() {
    if (!preview || !confirmed || !mayCreate || disabled || busy) return
    setBusy(true); setError('')
    try {
      const response = await apiPostRaw(`${endpoint}/import`, { name: name.trim(), package: packageData, previewToken: preview.previewToken, confirm: true })
      const result = await response.json().catch(() => null)
      if (!response.ok || typeof result?.id !== 'string') throw new Error(result?.error ?? `Package import failed (HTTP ${response.status}).`)
      onCreated(result.id)
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Could not import the package.') }
    finally { setBusy(false) }
  }

  if (!mayRead) return null
  return <section className="table-config-panel" aria-label="Portable configuration packages">
    <h2>Share or import configuration</h2>
    <button type="button" className="quiet" aria-expanded={open} onClick={() => setOpen(value => !value)}>{open ? 'Hide configuration packages' : 'Show configuration packages'}</button>
    {open && <>
      <p>Export reusable settings to JSON, or use this set as the local base for an imported package of the same verified profile. A package is not a fleet backup. Phone identity, protected payment and service records, and local access credentials are not included. Review advertising text, dial destinations and other user-authored content before sharing.</p>
      {!supported && <p role="note">Choose a set with a verified native firmware profile before sharing or importing.</p>}
      {disabled && <p role="note">Save pending record or target changes before working with packages.</p>}
      <button type="button" disabled={!supported || disabled || busy} onClick={() => void exportPackage()}>Export configuration JSON</button>
      {mayCreate && <div className="table-config-selectors">
        <label>Configuration package file<input type="file" accept=".json,application/json" disabled={!supported || disabled || busy}
          onChange={event => void selectFile(event.target.files?.[0])} /></label>
        <label>New draft name<input maxLength={120} value={name} disabled={!supported || disabled || busy}
          onChange={event => { setName(event.target.value); resetPreview() }} /></label>
        <button type="button" disabled={!supported || disabled || busy || !packageData || !name.trim()} onClick={() => void review()}>Preview configuration import</button>
      </div>}
      {fileName && <p>Selected package: {fileName}</p>}
      {preview && <section aria-label="Configuration import preview">
        <h3>Review new draft</h3>
        <p>Target: {preview.targetFirmware}. This creates a separate unassigned draft and does not change the local base, publish a set or queue a download.</p>
        <table><thead><tr><th>Record</th><th>Action</th><th>Bytes</th><th>Local fields</th></tr></thead>
          <tbody>{preview.records.map(row => <tr key={row.number}><td>MT{row.number}</td><td>{row.action}</td><td>{row.length}</td><td>{row.retainedLocalFields ? 'Retained from local base' : 'Not required'}</td></tr>)}</tbody></table>
        <p>Records retained from local base: {numbers(preview.retainedNumbers)}.</p>
        {!!preview.missingOmittedNumbers.length && <p role="note">Omitted package records missing from this base: {numbers(preview.missingOmittedNumbers)}. Prepare them locally before publishing or using the draft.</p>}
        {!!preview.findings.length && <ul>{preview.findings.map((finding, index) => <li key={index}>MT{finding.tableNumber}: {finding.blocking ? 'Needs correction. ' : 'Review. '}{finding.message}</li>)}</ul>}
        <label><input type="checkbox" checked={confirmed} disabled={busy || disabled} onChange={event => setConfirmed(event.target.checked)} /> I reviewed the included and retained records.</label>{' '}
        <button type="button" disabled={!confirmed || busy || disabled || !preview.canCreate} onClick={() => void create()}>Create imported draft</button>
      </section>}
      {error && <p role="alert">{error}</p>}
    </>}
  </section>
}
