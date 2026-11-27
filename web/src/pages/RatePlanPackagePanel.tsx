import { useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPostRaw } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'

type Package = { formatVersion: number; name: string; mode: number; rules: unknown[];
  tariffs: unknown[]; destinationPrefixes: unknown[]; timeBands: unknown[] }
type Preview = { name: string; rules: number; tariffs: number; destinationPrefixes: number; timeBands: number;
  explicitPaidServiceOverrides: number; notice: string }

export function RatePlanPackagePanel({ onImported, sources = [] }: { onImported(): void; sources?: { id: string; name: string; publishedVersionId: string | null }[] }) {
  const [value, setValue] = useState<Package | null>(null)
  const [name, setName] = useState('')
  const [preview, setPreview] = useState<Preview | null>(null)
  const [created, setCreated] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [starter, setStarter] = useState(false)
  const [sourceId, setSourceId] = useState('')
  const canAdd = canPerform('rate-plans', op.add)

  function loadPackage(pack: Package, isStarter: boolean) {
    if (!pack || typeof pack.name !== 'string' || !Array.isArray(pack.rules)) throw new Error('Choose a valid CoinLine rate plan JSON package.')
    setValue(pack); setName(pack.name); setStarter(isStarter); setPreview(null); setCreated(null)
  }
  async function loadStarter() {
    setBusy(true); setError('')
    try {
      const source = sources.find(row => row.id === sourceId && row.publishedVersionId)
      const query = source ? '?' + new URLSearchParams({ sourcePlanId: source.id, sourceVersionId: source.publishedVersionId! }) : ''
      loadPackage(await apiGet<Package>('/api/rate-plans/packages/regional-starter' + query), true)
    }
    catch (e) { setError(e instanceof Error ? e.message : 'Unable to load the rating starter.') }
    finally { setBusy(false) }
  }
  async function readFile(file: File) {
    setBusy(true); setError(''); setValue(null); setPreview(null); setCreated(null)
    try {
      if (file.size > 2_000_000) throw new Error('Choose a JSON package smaller than 2 MB.')
      loadPackage(JSON.parse(await file.text()) as Package, false)
    } catch (e) { setError(e instanceof Error ? e.message : 'Unable to read the rating package.') }
    finally { setBusy(false) }
  }
  async function submit(create: boolean) {
    if (!value || (create && (!preview || !canAdd))) return
    setBusy(true); setError(''); setCreated(null)
    try {
      const response = await apiPostRaw('/api/rate-plans/packages/' + (create ? 'import' : 'preview'), { package: value, name: name.trim() })
      const json = await response.json().catch(() => null)
      if (!response.ok || !json) throw new Error(json?.error ?? `Rate plan package request failed (HTTP ${response.status}).`)
      if (create) { setCreated(json.id); setPreview(null); onImported() }
      else setPreview(json as Preview)
    } catch (e) { setPreview(null); setError(e instanceof Error ? e.message : 'Unable to import the rate plan.') }
    finally { setBusy(false) }
  }

  return <section aria-label="Import a portable rate plan" style={{ marginBottom: 22 }}>
    <h2>Rate plan packages</h2>
    <p>Share host rating rules, tariffs, prefixes and time bands in a portable JSON file. Import creates a new draft; it never replaces an existing plan or assigns one to a phone.</p>
    <label>Retain existing exact local free routes <select value={sourceId} disabled={busy}
      onChange={e => { setSourceId(e.target.value); setValue(null); setPreview(null); setCreated(null); setStarter(false) }}>
      <option value="">None, original regional template</option>{sources.filter(row => row.publishedVersionId).map(row =>
        <option key={row.id} value={row.id}>{row.name}</option>)}</select></label>{' '}
    <button type="button" disabled={busy} onClick={() => void loadStarter()}>Load Newfoundland and North American starter</button>{' '}
    <label>Import rate plan JSON <input type="file" accept=".json,application/json" disabled={busy}
      onChange={e => { const file = e.target.files?.[0]; if (file) void readFile(file); e.target.value = '' }} /></label>
    {starter && value && <div>
      <h3>Starter pricing (USD)</h3>
      <ul>
        <li>709 ten-digit: $0.25 for the whole call.</li>
        <li>1-709: $0.50 first minute, $0.25 each additional minute.</li>
        <li>Other Canadian 1+10-digit: $1.00 first minute, $0.25 each additional minute.</li>
        <li>US 1+10-digit: $1.50 first minute, $0.50 each additional minute.</li>
        <li>211, 311, 511, 611, 811, 911 and toll-free NPAs: free; 411: $1 per call; 711: $0.25 per started minute.</li>
      </ul>
      <p>This is a host rating template. Supported terminal numbering, screening and rate tables also need review before use.</p>
      {sourceId && <p>Exact existing four-digit and 709 free-call exceptions are retained from the chosen published version. Broad and conflicting ordinary pricing rules are not carried.</p>}
    </div>}
    {value && <p><label>Imported plan name <input value={name} maxLength={120} disabled={busy} onChange={e => { setName(e.target.value); setPreview(null); setCreated(null) }} /></label>{' '}
      <button type="button" disabled={busy || !name.trim()} onClick={() => void submit(false)}>Preview rate plan package</button></p>}
    {error && <p role="alert">{error}</p>}
    {preview && <>
      <p>{preview.rules} rules, {preview.tariffs} tariffs, {preview.destinationPrefixes} destination prefixes, {preview.timeBands} time bands.</p>
      {preview.explicitPaidServiceOverrides > 0 && <p>{preview.explicitPaidServiceOverrides} explicitly priced service rules override overlapping free number classes. Emergency and blocked classes remain authoritative.</p>}
      <p>{preview.notice}</p>
      {canAdd ? <button type="button" disabled={busy} onClick={() => { if (window.confirm('Create a separate unassigned rate plan draft?')) void submit(true) }}>Create imported rate plan draft</button> :
        <p>Rate plan creation permission is required to import.</p>}
    </>}
    {created && <p>Draft created. <Link to={`/rate-plans/${created}`}>Open imported rate plan</Link>. Review and publish it separately.</p>}
  </section>
}
