import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPost } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { StatusBadge } from '../components/StatusBadge'
import './TableSetConfiguration.css'

type Row = { id: string; name: string; tableNumber: number; description: string | null }
type Layout = { number: number; length: number; profile: string; draftTemplateAvailable: boolean; explicitAuthoringAvailable?: boolean }
type Firmware = { profileId: string; label: string; length: number; terminalAcknowledged: boolean }
type Reference = {
  number: number; name: string; kind: string; layouts: Layout[]; firmware: Firmware[]
  registeredDefinitions: { id: string; name: string }[]; behaviorObservation?: string | null
}
const kinds: Record<string, string> = {
  native_editor: 'Native editor', observed_only: 'Observed length only', operational: 'Operational message',
  scheduler_record: 'Code Download Scheduler', documented_only: 'Documented, layout unverified', registered_unclassified: 'Unclassified registry entry',
}

export function TableDefinitions() {
  const [rows, setRows] = useState<Row[] | null>(null)
  const [catalogue, setCatalogue] = useState<Reference[] | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [catalogueError, setCatalogueError] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [num, setNum] = useState(100)
  const [desc, setDesc] = useState('')
  const [filter, setFilter] = useState('all')
  const [search, setSearch] = useState('')
  const [refresh, setRefresh] = useState(0)
  const canAdd = canPerform('table-definitions', op.add)

  useEffect(() => {
    let active = true
    setErr(null); setCatalogueError(null)
    void apiGet<Row[]>('/api/tables/definitions').then(value => { if (active) setRows(value) })
      .catch(error => { if (active) setErr(error instanceof Error ? error.message : 'Could not load registry.') })
    void apiGet<Reference[]>('/api/tables/reference-records').then(value => { if (active) setCatalogue(value) })
      .catch(error => { if (active) setCatalogueError(error instanceof Error ? error.message : 'Could not load reference catalogue.') })
    return () => { active = false }
  }, [refresh])

  async function onCreate(e: React.FormEvent) {
    e.preventDefault()
    setErr(null)
    try {
      await apiPost<{ id: string }>('/api/tables/definitions', { name, tableNumber: num, description: desc || null })
      setName(''); setDesc(''); setRefresh(value => value + 1)
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'Could not create definition.')
    }
  }

  const query = search.trim().toLowerCase()
  const visible = catalogue?.filter(record => (filter === 'all' || record.kind === filter)
    && (!query || [
      String(record.number), `mt${record.number}`, record.name, kinds[record.kind] ?? '',
      ...record.registeredDefinitions.map(definition => definition.name),
    ].some(value => value.toLowerCase().includes(query)))) ?? []
  const byNumber = new Map(catalogue?.map(record => [record.number, record]))

  return (
    <div>
      <h1>Table definitions</h1>
      <p className="page-intro">Review the native record catalogue and registered storage definitions. A verified length,
        a native editor, an observed acknowledgement and confirmed device behavior are different levels of evidence.</p>
      <section className="table-config-panel" aria-labelledby="reference-records-heading">
        <h2 id="reference-records-heading">Reference record catalogue</h2>
        <p>Includes packed editor layouts, observed firmware record lengths, and known runtime messages that do not belong in an install table set.</p>
        <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
          <label>Find a record
            <input value={search} onChange={e => setSearch(e.target.value)} placeholder="MT92 or call screening" style={{ display: 'block' }} />
          </label>
          <label>Record category
            <select value={filter} onChange={e => setFilter(e.target.value)} style={{ display: 'block' }}>
              <option value="all">All records</option>
              {Object.entries(kinds).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </select>
          </label>
        </div>
        {catalogue === null && !catalogueError && <p role="status">Loading reference catalogue…</p>}
        {catalogueError && <p role="alert">Could not load reference catalogue: {catalogueError}</p>}
        {catalogue && <p role="status">{visible.length} of {catalogue.length} reference records</p>}
        {visible.length > 0 && <div role="region" aria-label="Reference records" tabIndex={0} style={{ overflowX: 'auto' }}>
          <table cellPadding={8} style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead><tr><th scope="col">MT</th><th scope="col">Reference name</th><th scope="col">Category</th>
              <th scope="col">Packed editor</th><th scope="col">Firmware evidence</th><th scope="col">Observed behavior</th><th scope="col">Registered definitions</th></tr></thead>
            <tbody>{visible.map(record => <tr key={record.number} style={{ borderBottom: '1px solid #eee' }}>
              <td>MT{record.number}</td><td>{record.name}</td>
              <td><StatusBadge label={kinds[record.kind] ?? 'Unknown'} variant={record.kind === 'native_editor' ? 'ok' : record.kind === 'operational' ? 'neutral' : 'info'} />
                {record.kind === 'operational' && <div><small>Not an install record</small></div>}
                {record.kind === 'scheduler_record' && <div><small>Separate scheduling workflow</small></div>}</td>
              <td>{record.layouts.length ? <details><summary>{record.layouts.length} layout{record.layouts.length === 1 ? '' : 's'}</summary>
                <ul>{record.layouts.map(layout => <li key={`${layout.profile}-${layout.length}`}>{layout.profile}: {layout.length.toLocaleString()} bytes;
                  {layout.draftTemplateAvailable ? ' draft template' : layout.explicitAuthoringAvailable ? ' explicit authoring (no default template)' : ' import/edit only'}</li>)}</ul></details> : 'Not established'}</td>
              <td>{record.firmware.length ? <details><summary>{record.firmware.length} profiles; {record.firmware.filter(f => f.terminalAcknowledged).length} acknowledged</summary>
                <ul>{record.firmware.map(profile => <li key={profile.profileId}>{profile.label}: {profile.length.toLocaleString()} bytes;
                  {profile.terminalAcknowledged ? ' emulator MT14 observed' : ' acknowledgement not recorded'}</li>)}</ul>
                <small>Length and emulator acknowledgement do not establish physical behavior.</small></details> : 'Not recorded'}</td>
              <td style={{ minWidth: 180, maxWidth: 300 }}>{record.behaviorObservation ?? 'Not recorded in this reference catalogue'}</td>
              <td>{record.registeredDefinitions.length ? record.registeredDefinitions.map(definition => <div key={definition.id}>
                <Link to={`/table-definitions/${definition.id}`}>{definition.name}</Link></div>) : 'None'}</td>
            </tr>)}</tbody>
          </table>
        </div>}
      </section>
      <section className="table-config-panel" aria-labelledby="registered-definitions-heading">
        <h2 id="registered-definitions-heading">Registered definitions</h2>
        <p>Stored definition names are not rewritten when reference information changes. A historical fixture label does not determine on-wire meaning.</p>
        {err && <p role="alert">Could not load or save definition: {err}</p>}
        {canAdd && <form onSubmit={onCreate} style={{ marginBottom: 24, display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'flex-end' }}>
          <label>Name<input value={name} onChange={(e) => setName(e.target.value)} required style={{ display: 'block' }} /></label>
          <label>Table #<input data-field-kind="key" type="number" value={num} onChange={(e) => setNum(Number(e.target.value))} style={{ display: 'block', width: 80 }} /></label>
          <label>Description<input value={desc} onChange={(e) => setDesc(e.target.value)} style={{ display: 'block', width: 280 }} /></label>
          <button type="submit">Create definition</button>
        </form>}
        {byNumber.get(num)?.kind === 'operational' && canAdd && <p role="status">MT{num} is a known operational message; do not assign it as a configuration table.</p>}
        {!rows && !err && <p role="status">Loading definitions…</p>}
        {rows?.length === 0 && <p role="status">No table definitions yet.</p>}
        {!!rows?.length && <div role="region" aria-label="Registered table definitions" tabIndex={0} style={{ overflowX: 'auto' }}>
          <table cellPadding={8} style={{ borderCollapse: 'collapse', width: '100%' }}>
            <thead><tr><th scope="col">Stored name</th><th scope="col">MT</th><th scope="col">Reference name</th><th scope="col">Detail</th></tr></thead>
            <tbody>{rows.map(row => <tr key={row.id} style={{ borderBottom: '1px solid #eee' }}>
              <td>{row.name}</td><td>{row.tableNumber}</td><td>{byNumber.get(row.tableNumber)?.name ?? 'Not classified'}</td>
              <td><Link to={`/table-definitions/${row.id}`}>View {row.name}</Link></td>
            </tr>)}</tbody>
          </table>
        </div>}
      </section>
    </div>
  )
}
