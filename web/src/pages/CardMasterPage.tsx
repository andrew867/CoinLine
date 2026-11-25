import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { CardMasterRolloutPanel } from './CardMasterRolloutPanel'
import { cardMasterEndpoint, cardMasterResponse, type CardMasterEntry, type CardMasterField, type CardMasterDetails, type CardMasterSection as Section } from './cardMasterApi'
import './TableSetConfiguration.css'

type Usage = { assignedPhones: number; outdatedAssignedPhones: number; configurations: { setId: string; name: string; status: string;
  assignedPhones: number; positions: { position: number; appliedRevision: number; outdated: boolean }[] }[] }

export function CardMasterFieldInput({ field, value, onChange }: { field: CardMasterField; value: string; onChange: (value: string) => void }) {
  return <label className={field.editor === 'checkbox' ? 'native-table-toggle' : 'native-table-field'}>
    {field.editor === 'checkbox' ? <><input type="checkbox" checked={value === 'true'} onChange={event => onChange(String(event.target.checked))} />{field.label}</>
      : <><span>{field.label}</span>{field.choices ? <select value={value} onChange={event => onChange(event.target.value)}>
        {!field.choices.some(choice => String(choice.value) === value) && <option value={value}>Stored value {value || 'blank'}</option>}
        {field.choices.map(choice => <option key={choice.value} value={choice.value}>{choice.label}</option>)}
      </select> : <input type={field.editor === 'number' ? 'number' : 'text'} value={value}
        min={field.editor === 'number' ? field.min : undefined} max={field.editor === 'number' ? field.max : undefined}
        maxLength={field.maxLength ?? undefined} onChange={event => onChange(event.target.value)} />}</>}
    {field.targetMapping && <small>Target-specific reference, review it when adding to a configuration.</small>}
    {field.help && <small>{field.help}</small>}
  </label>
}

export function CardMasterPage() {
  const canRead = canPerform('card-master', op.read), canAdd = canPerform('card-master', op.add)
  const canUpdate = canPerform('card-master', op.update), canArchive = canPerform('card-master', op.delete)
  const canUsage = canPerform('table-sets', op.read) && canPerform('table-records', op.read) && canPerform('table-assignments', op.read)
  const [entries, setEntries] = useState<CardMasterEntry[]>([]), [selected, setSelected] = useState<CardMasterEntry | null>(null)
  const [number, setNumber] = useState(22), [standard, setStandard] = useState('1')
  const [sections, setSections] = useState<Section[] | null>(null), [changes, setChanges] = useState<Record<string, string>>({})
  const [code, setCode] = useState(''), [name, setName] = useState(''), [usage, setUsage] = useState<Usage | null>(null)
  const [usageError, setUsageError] = useState(''), [error, setError] = useState(''), [notice, setNotice] = useState('')
  const [search, setSearch] = useState(''), [showArchived, setShowArchived] = useState(false), [busy, setBusy] = useState(false)
  const [generation, setGeneration] = useState(0)
  const [usageGeneration, setUsageGeneration] = useState(0)
  const editable = selected ? canUpdate && !selected.archived : canAdd
  const dirty = code !== (selected?.code ?? '') || name !== (selected?.name ?? '') || Object.keys(changes).length > 0

  function reloadList() {
    if (canRead) void apiFetch(`${cardMasterEndpoint}?includeArchived=true`).then(cardMasterResponse<CardMasterEntry[]>).then(setEntries)
      .catch(err => setError(err instanceof Error ? err.message : 'Could not load the master list.'))
  }
  useEffect(reloadList, [canRead])

  useEffect(() => {
    if (selected ? !canRead : !canAdd) return
    const abort = new AbortController()
    const url = selected ? `${cardMasterEndpoint}/${selected.id}` : `${cardMasterEndpoint}/schema?tableNumber=${number}&standard=${standard}`
    setSections(null); setError('')
    void apiFetch(url, { signal: abort.signal }).then(cardMasterResponse<CardMasterDetails | { sections: Section[] }>).then(data => {
      if (abort.signal.aborted) return
      setSections(data.sections)
      if ('entry' in data) { setSelected(data.entry); setCode(data.entry.code); setName(data.entry.name); setChanges({}) }
    }).catch(err => { if (!abort.signal.aborted) setError(err instanceof Error ? err.message : 'Could not load the card editor.') })
    return () => abort.abort()
  }, [selected?.id, number, standard, generation, canRead, canAdd])

  useEffect(() => {
    setUsage(null); setUsageError('')
    if (!selected || !canUsage || !canRead) return
    const abort = new AbortController()
    void apiFetch(`${cardMasterEndpoint}/${selected.id}/usage`, { signal: abort.signal }).then(cardMasterResponse<Usage>).then(data => {
      if (!abort.signal.aborted) setUsage(data)
    }).catch(err => { if (!abort.signal.aborted) setUsageError(err instanceof Error ? err.message : 'Could not load configuration usage.') })
    return () => abort.abort()
  }, [selected?.id, selected?.revision, canUsage, canRead, generation, usageGeneration])

  function choose(entry: CardMasterEntry | null) {
    if (dirty && !window.confirm('Discard unsaved card definition changes?')) return
    setSelected(entry); setCode(entry?.code ?? ''); setName(entry?.name ?? ''); setChanges({}); setNotice('')
    if (entry) setNumber(entry.tableNumber)
    setGeneration(value => value + 1)
  }

  function changeStandard(value: string) {
    const main = new Set(sections?.[0]?.fields.map(field => field.key) ?? [])
    if (Object.keys(changes).some(key => !main.has(key)) && !window.confirm('Discard unsaved detail fields when changing the card standard?')) return
    if (selected) {
      setChanges(old => {
        const next = Object.fromEntries(Object.entries(old).filter(([key]) => main.has(key) && key !== 'card-0.format'))
        if (value !== selected.standard) next['card-0.format'] = value
        return next
      }); return
    }
    setChanges(old => Object.fromEntries(Object.entries(old).filter(([key]) => main.has(key) && key !== 'card-0.format')))
    setStandard(value)
  }

  function changeField(key: string, value: string) {
    setChanges(old => {
      const result = { ...old }
      const original = sections?.flatMap(section => section.fields).find(field => field.key === key)?.value
      if (value === original) delete result[key]
      else result[key] = value
      return result
    })
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!editable || !sections) return
    setBusy(true); setError(''); setNotice('')
    try {
      const response = await apiFetch(selected ? `${cardMasterEndpoint}/${selected.id}` : cardMasterEndpoint, {
        method: selected ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code, name, tableNumber: number, revision: selected?.revision,
          changes: selected ? changes : { ...changes, 'card-0.format': standard } }),
      })
      const data = await cardMasterResponse<CardMasterDetails>(response)
      setSections(data.sections); setChanges({}); setNotice('Card master definition saved. Existing configurations are unchanged until refreshed.')
      if (canRead) { setSelected(data.entry); setCode(data.entry.code); setName(data.entry.name); reloadList() }
      else { setCode(''); setName(''); setGeneration(value => value + 1) }
    } catch (err) { setError(err instanceof Error ? err.message : 'Could not save the card definition.') }
    finally { setBusy(false) }
  }

  async function archive(restore: boolean) {
    if (!selected || (!restore && !window.confirm('Remove this definition from the active Master List? Existing configuration snapshots will remain unchanged.'))) return
    setBusy(true); setError(''); setNotice('')
    try {
      const url = `${cardMasterEndpoint}/${selected.id}${restore ? '/restore' : ''}?revision=${selected.revision}${restore ? '' : '&confirm=true'}`
      await cardMasterResponse(await apiFetch(url, { method: restore ? 'POST' : 'DELETE' }))
      setNotice(restore ? 'Master definition restored.' : 'Master definition archived. Existing configurations were not modified.')
      setGeneration(value => value + 1); reloadList()
    } catch (err) { setError(err instanceof Error ? err.message : 'Could not update the master definition.') }
    finally { setBusy(false) }
  }

  const visible = entries.filter(entry => (showArchived || !entry.archived) && `${entry.code} ${entry.name} MT${entry.tableNumber}`.toLowerCase().includes(search.toLowerCase()))
  const familyChanging = !!selected && 'card-0.format' in changes
  return <div>
    <p><Link to="/table-sets">Table sets</Link></p><h1>Card Master List</h1>
    <p className="page-intro">Define reusable card acceptance configurations for older MT22 and later MT134 terminals. These are terminal settings, not card accounts. Draft configurations retain their own reviewed carrier and other target references.</p>
    {error && <p role="alert">{error}</p>}{notice && <p role="status">{notice}</p>}
    {canRead && <section className="table-config-panel"><h2>Saved card definitions</h2>
      <div className="table-config-selectors"><label>Find card definition<input type="search" value={search} onChange={event => setSearch(event.target.value)} /></label>
        <label><input type="checkbox" checked={showArchived} onChange={event => setShowArchived(event.target.checked)} /> Include archived</label>
        {canAdd && <button type="button" disabled={busy} onClick={() => choose(null)}>New card definition</button>}</div>
      {visible.length === 0 ? <p>No matching card definitions.</p> : <div className="table-set-versions"><table>
        <thead><tr><th>Card type</th><th>Name</th><th>Wire table</th><th>Range</th><th>Revision</th></tr></thead>
        <tbody>{visible.map(entry => <tr key={entry.id}><td><button type="button" disabled={busy} aria-pressed={selected?.id === entry.id} onClick={() => choose(entry)}>{entry.code}</button></td>
          <td>{entry.name}{entry.archived && ' (archived)'}</td><td>MT{entry.tableNumber}</td><td>{entry.low ?? 'Unknown'} to {entry.high ?? 'Unknown'}</td><td>{entry.revision}</td></tr>)}</tbody>
      </table></div>}
    </section>}
    {!selected && canAdd && <label>Card wire profile<select value={number} disabled={busy} onChange={event => {
      if (dirty && !window.confirm('Discard unsaved card definition changes?')) return
      setNumber(Number(event.target.value)); setStandard('1'); setChanges({}); setCode(''); setName('')
    }}><option value={22}>MT22 (20 positions)</option><option value={134}>MT134 (32 positions)</option></select></label>}
    {sections && <form className="native-table-editor" onSubmit={event => void save(event)}>
      <h2>{selected ? `Card definition ${selected.code}` : 'New card definition'}</h2>
      <fieldset disabled={!editable || busy} className="native-table-fields"><label>Card type ID<input value={code} required maxLength={4} onChange={event => setCode(event.target.value)} /></label>
        <label>Card name<input value={name} maxLength={80} onChange={event => setName(event.target.value)} /></label></fieldset>
      {familyChanging && <p>Save the new standard to open its corresponding detail fields.</p>}
      {sections.filter((_, index) => !familyChanging || index === 0).map(section => <fieldset className="native-table-fields" key={section.key} disabled={!editable || busy}>
        <legend>{section.label.replace('Card range 1', 'Card')}</legend>
        {section.fields.map(field => <CardMasterFieldInput key={field.key}
          field={field.key === 'card-0.format' ? { ...field, choices: field.choices?.filter(choice => choice.value !== 0 && choice.value !== 255) ?? null } : field}
          value={changes[field.key] ?? field.value ?? ''}
          onChange={value => field.key === 'card-0.format' ? changeStandard(value) : changeField(field.key, value)} />)}
      </fieldset>)}
      <div className="row-actions">{editable && <button type="submit" disabled={busy || !code.trim() || (!!selected && !dirty)}>Save card definition</button>}
        {selected && !selected.archived && canArchive && <button type="button" className="quiet" disabled={busy || dirty} onClick={() => void archive(false)}>Archive definition</button>}
        {selected?.archived && canUpdate && <button type="button" disabled={busy} onClick={() => void archive(true)}>Restore definition</button>}</div>
    </form>}
    {selected && canUsage && <section className="table-config-panel"><h2>Where this definition is used</h2>
      {usageError && <p role="alert">{usageError}</p>}{usage && <><p>Assigned phones: {usage.assignedPhones}. Assigned phones with an older definition revision: {usage.outdatedAssignedPhones}.</p>
        {usage.configurations.length === 0 ? <p>No linked configurations.</p> : <div className="table-set-versions"><table><thead><tr><th>Configuration</th><th>Status</th><th>Positions and revisions</th><th>Assigned phones</th></tr></thead>
          <tbody>{usage.configurations.map(row => <tr key={row.setId}><td><Link to={`/table-sets/${row.setId}`}>{row.name}</Link></td><td>{row.status}</td>
            <td>{row.positions.map(position => `${position.position}: revision ${position.appliedRevision}${position.outdated ? ' (older)' : ''}`).join(', ')}</td><td>{row.assignedPhones}</td></tr>)}</tbody></table></div>}
        <p>Open a draft configuration to refresh linked positions. Published configuration snapshots remain immutable.</p></>}
    </section>}
    {selected && !selected.archived && canRead && canUsage && <CardMasterRolloutPanel key={`${selected.id}:${selected.revision}`} entry={selected}
      dirty={dirty || busy} onApplied={() => setUsageGeneration(value => value + 1)} />}
  </div>
}
