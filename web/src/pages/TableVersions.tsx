import { useEffect, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { apiFetch } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import './ConfigurationLibrary.css'

type Row = {
  id: string; tableSetId: string; setName: string; setStatus: number
  number: number; definitionName: string; configurationId: string | null; tableRevision: number
  payloadSha256Hex: string; payloadLengthBytes: number; validationPassed: boolean
  targetFirmwareVersionId: string | null; targetFirmware: string | null
  recordCountInSet: number; assignedPhoneCount: number | null; nativeLayoutKnown: boolean
}
type Library = { items: Row[]; total: number; page: number; pageSize: number; canReadAssignment: boolean }
type Option = { id: string; name: string }
type Options = { records: { number: number; name: string }[]; sets: Option[]; firmware: Option[] }
type Filter = { number: string; set: string; configurationId: string; query: string; status: string; firmware: string; sort: string }
const endpoint = '/api/tables/configuration-library'
const blank: Filter = { number: '', set: '', configurationId: '', query: '', status: 'all', firmware: '', sort: 'record' }

function readFilters(params: URLSearchParams): Filter {
  return {
    number: params.get('number') ?? '', set: params.get('set') ?? '', configurationId: params.get('configurationId') ?? '',
    query: params.get('query') ?? '', status: params.get('status') ?? 'all', firmware: params.get('firmware') ?? '', sort: params.get('sort') ?? 'record',
  }
}
function filterParams(filters: Filter, page = 1): URLSearchParams {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(filters)) {
    if (value && value !== blank[key as keyof Filter]) params.set(key, value)
  }
  if (page > 1) params.set('page', String(page))
  return params
}
async function read<T>(path: string, signal: AbortSignal): Promise<T> {
  const response = await apiFetch(path, { signal })
  if (!response.ok) {
    const body = await response.json().catch(() => null)
    throw new Error(body?.error ?? `Request failed (HTTP ${response.status}).`)
  }
  return response.json() as Promise<T>
}

export function TableVersions() {
  const [params, setParams] = useSearchParams()
  const queryKey = params.toString()
  const [draft, setDraft] = useState<Filter>(() => readFilters(params))
  const [data, setData] = useState<Library | null>(null)
  const [options, setOptions] = useState<Options | null>(null)
  const [error, setError] = useState('')
  const [optionError, setOptionError] = useState('')
  const [refresh, setRefresh] = useState(0)
  const canRead = canPerform('table-records', op.read)
  const canOpenSet = canPerform('table-sets', op.read)
  const canEdit = canPerform('table-records', op.update)

  useEffect(() => {
    if (!canRead) return
    const controller = new AbortController()
    read<Options>(`${endpoint}/options`, controller.signal)
      .then(choices => { if (!controller.signal.aborted) { setOptions(choices); setOptionError('') } })
      .catch(failure => { if (!controller.signal.aborted) setOptionError(failure.message) })
    return () => controller.abort()
  }, [canRead, refresh])

  useEffect(() => {
    const active = new URLSearchParams(queryKey)
    const filters = readFilters(active)
    setDraft(filters)
    if (!canRead) return
    setData(null); setError('')
    const controller = new AbortController()
    const request = filterParams(filters)
    if (request.has('set')) { request.set('tableSetId', request.get('set')!); request.delete('set') }
    if (request.has('firmware')) {
      const firmware = request.get('firmware')!
      request.delete('firmware')
      if (firmware === 'untargeted') request.set('untargeted', 'true')
      else request.set('firmwareVersionId', firmware)
    }
    if (active.has('page')) request.set('page', active.get('page')!)
    read<Library>(`${endpoint}?${request}`, controller.signal)
      .then(result => { if (!controller.signal.aborted) setData(result) })
      .catch(failure => { if (!controller.signal.aborted) setError(failure.message) })
    return () => controller.abort()
  }, [queryKey, canRead, refresh])

  function change(key: keyof Filter, value: string) { setDraft(previous => ({ ...previous, [key]: value })) }
  function search(event: FormEvent) { event.preventDefault(); setParams(filterParams(draft)) }
  function page(next: number) { setParams(filterParams(readFilters(params), next)) }
  function choose(name: string, key: 'set' | 'firmware', choices: Option[], extra?: Option) {
    const all = extra ? [extra, ...choices] : choices
    return <label>{name}<select aria-label={name} value={draft[key]} onChange={event => change(key, event.target.value)}>
      <option value="">All</option>
      {draft[key] && !all.some(choice => choice.id === draft[key]) && <option value={draft[key]}>Selected item</option>}
      {all.map(choice => <option key={choice.id} value={choice.id}>{choice.name}</option>)}
    </select></label>
  }

  if (!canRead) return <p role="alert">You do not have permission to view table versions.</p>
  const first = data?.items.length ? (data.page - 1) * data.pageSize + 1 : 0
  const last = data?.items.length ? first + data.items.length - 1 : 0
  return <div className="configuration-library">
    <header className="configuration-library-hero">
      <div><h1>Configuration library</h1><p>Find saved terminal records by configuration ID, record type, set and firmware target.</p></div>
      {canOpenSet && <Link to="/table-sets">Table sets</Link>}
    </header>
    <section className="table-config-panel" aria-label="Configuration filters">
      <form className="configuration-library-filters" onSubmit={search}>
        <label>Search names and IDs<input type="search" value={draft.query} maxLength={120} onChange={event => change('query', event.target.value)} /></label>
        <label>Configuration ID<input value={draft.configurationId} maxLength={6} onChange={event => change('configurationId', event.target.value)} /></label>
        <label>Record type<select aria-label="Record type" value={draft.number} onChange={event => change('number', event.target.value)}>
          <option value="">All records</option>
          {draft.number && !options?.records.some(record => String(record.number) === draft.number) && <option value={draft.number}>MT{draft.number}</option>}
          {options?.records.map(record => <option key={record.number} value={record.number}>MT{record.number} · {record.name}</option>)}
        </select></label>
        {choose('Table set', 'set', options?.sets ?? [])}
        {choose('Firmware target', 'firmware', options?.firmware ?? [], { id: 'untargeted', name: 'Untargeted' })}
        <label>Set status<select aria-label="Set status" value={draft.status} onChange={event => change('status', event.target.value)}>
          <option value="all">All</option><option value="draft">Draft</option><option value="published">Published</option>
        </select></label>
        <label>Sort by<select aria-label="Sort by" value={draft.sort} onChange={event => change('sort', event.target.value)}>
          <option value="record">Record type</option><option value="set">Set name</option><option value="configuration">Configuration ID</option>
        </select></label>
        <div className="row-actions"><button type="submit">Search</button><button type="button" onClick={() => { setDraft(blank); setParams({}) }}>Clear filters</button></div>
      </form>
      {optionError && <p role="alert">Could not load filter choices: {optionError}</p>}
    </section>
    <section className="table-config-panel" aria-label="Configuration results">
      <div className="configuration-library-summary"><h2>Saved records</h2><button type="button" onClick={() => setRefresh(value => value + 1)}>Refresh</button></div>
      {error && <p role="alert">Configuration search failed: {error}</p>}
      {!data && !error && <p role="status">Loading configurations…</p>}
      {data && <p role="status">{data.items.length ? `Showing ${first} to ${last} of ${data.total} records`
        : data.total ? 'No records on this page. Choose an earlier page.' : 'No records match this search.'}</p>}
      {data && data.items.length > 0 && <div className="configuration-library-table" tabIndex={0} role="region" aria-label="Saved configuration records">
        <table><thead><tr><th>Record</th><th>Configuration ID</th><th>Table set</th><th>Firmware target</th><th>Revision / size</th><th>Validation</th>
          {data.canReadAssignment && <th>Assigned phones</th>}<th>Payload SHA-256</th></tr></thead>
          <tbody>{data.items.map(row => <tr key={row.id}>
            <td><strong>MT{row.number}</strong><br />{row.definitionName}
              {canOpenSet && row.recordCountInSet === 1 && row.nativeLayoutKnown && <><br /><Link to={`/table-sets/${encodeURIComponent(row.tableSetId)}?record=${row.number}#config-heading`}>
                {row.setStatus === 0 && canEdit ? 'Open editor' : 'View record'}</Link></>}
              {row.recordCountInSet > 1 && <small className="configuration-library-warning">Multiple records of this type in the set</small>}
              {!row.nativeLayoutKnown && <small>No matching native layout</small>}
            </td>
            <td>{row.configurationId ?? 'Unnamed'}</td>
            <td>{canOpenSet ? <Link to={`/table-sets/${encodeURIComponent(row.tableSetId)}`}>{row.setName}</Link> : row.setName}
              <small>{row.setStatus === 0 ? 'Draft' : 'Published'}</small></td>
            <td>{row.targetFirmware ?? 'Untargeted'}</td>
            <td>rev {row.tableRevision}<br />{row.payloadLengthBytes} B</td>
            <td>{row.validationPassed ? 'Passed' : <span className="configuration-library-warning">Needs review</span>}</td>
            {data.canReadAssignment && <td>{row.assignedPhoneCount ?? 0}</td>}
            <td><code>{row.payloadSha256Hex}</code></td>
          </tr>)}</tbody></table>
      </div>}
      {data && <div className="configuration-library-summary">
        <button type="button" disabled={data.page <= 1} onClick={() => page(data.page - 1)}>Previous page</button>
        <span>Page {data.page} of {Math.max(1, Math.ceil(data.total / data.pageSize))}</span>
        <button type="button" disabled={data.page * data.pageSize >= data.total} onClick={() => page(data.page + 1)}>Next page</button>
      </div>}
      <p className="page-intro">Validation and assignment counts are host evidence. They do not establish terminal acknowledgement or device behavior.</p>
    </section>
  </div>
}
