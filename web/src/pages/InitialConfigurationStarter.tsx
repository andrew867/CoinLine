import { useEffect, useState } from 'react'
import { apiGet, apiGetRaw } from '../api/client'

export type StarterRecord = { number: number; id: string; tableSetId: string; setName: string; configurationId: string | null;
  revision: number; length: number; sha256: string; targetFirmware: string | null; issue: string | null }
type Source = { id: string; name: string; isDefault: boolean; recordCount: number; hasAccess: boolean }
type Sources = { available: boolean; reason?: string; total?: number; page?: number; pageSize?: number; sources?: Source[] }
type Detail = { id: string; name: string; records: StarterRecord[]; requiresAccessPersonalization: boolean;
  family: { documented: boolean; tables: { name: string; required: boolean; status: string }[] } | null }

export function InitialConfigurationStarter({ endpoint, disabled, onStage }: {
  endpoint: string; disabled: boolean; onStage: (records: StarterRecord[]) => void
}) {
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [sources, setSources] = useState<Sources | null>(null)
  const [sourceId, setSourceId] = useState('')
  const [detail, setDetail] = useState<Detail | null>(null)
  const [listError, setListError] = useState('')
  const [detailError, setDetailError] = useState('')
  useEffect(() => {
    if (!open) return
    let active = true
    setSources(null); setListError(''); setSourceId(''); setDetail(null); setDetailError('')
    void apiGet<Sources>(`${endpoint}/initial-sources?query=${encodeURIComponent(query)}&page=${page}`)
      .then(value => { if (active) setSources(value) })
      .catch(() => { if (active) setListError('Could not load starter configurations. Refresh the worksheet if the phone assignment changed.') })
    return () => { active = false }
  }, [endpoint, open, query, page, reload])
  useEffect(() => {
    setDetail(null); setDetailError('')
    if (!open || !sourceId) return
    let active = true
    void apiGetRaw(`${endpoint}/initial-sources/${sourceId}`).then(async response => {
      const data = await response.json().catch(() => null)
      if (!response.ok) throw new Error(data?.error ?? `Starter review failed (HTTP ${response.status}).`)
      if (active) setDetail(data as Detail)
    }).catch(error => { if (active) setDetailError(error instanceof Error ? error.message : 'Could not review this starter.') })
    return () => { active = false }
  }, [endpoint, open, sourceId])

  const missing = detail?.family?.tables.filter(row => row.required && row.status !== 'present') ?? []
  return <section className="worksheet-lookup" aria-label="Initial configuration starter">
    <h3>Start from a published configuration</h3>
    <p>Choose a matching published configuration for this customer or a shared one. Stage all its compatible records at once, then adjust individual choices and review the assignment. No record is written yet.</p>
    <button type="button" className="quiet" disabled={disabled && !open} onClick={() => setOpen(value => !value)}>{open ? 'Close starter selection' : 'Choose a matching starter'}</button>
    {disabled && !open && <p>Discard or finish the current staged changes before choosing another starter.</p>}
    {open && <>
      <div className="worksheet-controls"><label>Starter set name<input type="search" maxLength={120} value={search} disabled={disabled} onChange={event => setSearch(event.target.value)} /></label>
        <button type="button" disabled={disabled} onClick={() => { setQuery(search.trim()); setPage(1); setReload(value => value + 1) }}>Find starter sets</button></div>
      {listError && <p role="alert">{listError}</p>}
      {!sources && !listError && <p role="status">Finding published starters…</p>}
      {sources?.available === false && <p>{sources.reason}</p>}
      {sources?.available && <>
        <label>Published starter<select value={sourceId} disabled={disabled} onChange={event => setSourceId(event.target.value)}>
          <option value="">Choose a source configuration</option>
          {sources.sources?.map(source => <option key={source.id} value={source.id}>{source.name} · {source.recordCount} records{source.isDefault ? ' · Default' : ''}{!source.hasAccess ? ' · No MT21' : ''}</option>)}
        </select></label>
        {!sources.sources?.length && <p>No matching published configurations. Prepare and publish a reviewed draft first.</p>}
        <div className="worksheet-actions"><button type="button" disabled={disabled || page <= 1} onClick={() => setPage(value => value - 1)}>Previous starters</button>
          <span>{sources.total} matches, page {sources.page}</span><button type="button" disabled={disabled || page * (sources.pageSize ?? 50) >= (sources.total ?? 0)} onClick={() => setPage(value => value + 1)}>Next starters</button></div>
      </>}
      {detailError && <p role="alert">{detailError}</p>}
      {sourceId && !detail && !detailError && <p role="status">Reviewing starter records…</p>}
      {detail?.id === sourceId && <>
        <p>{detail.records.length} records can be staged: {detail.records.map(row => `MT${row.number}`).join(', ')}.</p>
        {detail.requiresAccessPersonalization && <p role="note">The MT21 record belongs to a different line. Explicitly select use of this phone’s registered wire ID after staging.</p>}
        {!detail.records.some(row => row.number === 21) && <p role="note">This source has no MT21. Add a valid management access record if required for the new phone.</p>}
        {missing.length > 0 && <p role="note">Required families needing review: {missing.map(row => `${row.name} (${row.status})`).join(', ')}.</p>}
        <p>Review management access, local routes, payment and card/coin settings, tariffs, and display settings for the new site. Copying a starter does not certify that it is ready for INSTALL.</p>
        <button type="button" disabled={disabled} onClick={() => { onStage(detail.records); setOpen(false) }}>Stage {detail.records.length} starter records</button>
      </>}
    </>}
  </section>
}
