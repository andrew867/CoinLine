import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPostRaw } from '../api/client'
import { TableDownloadScheduleList } from './TableDownloadScheduleList'

const scopes = {
  terminal: { label: 'Phone', path: '/api/terminals' },
  group: { label: 'Phone group', path: '/api/terminal-groups' },
  site: { label: 'Location', path: '/api/sites' },
  customer: { label: 'Customer', path: '/api/customers' },
} as const

type Scope = keyof typeof scopes
type Option = { id: string; name?: string; displayName?: string }
type Target = { id: string; displayName: string; terminalFamilyLabel: string | null; eligible: boolean; reason: string | null }
type SavedRecord = { tableNumber: number; definitionName: string; payloadLengthBytes: number; validationPassed: boolean }
type OptionalRecord = { tableNumber: number; name: string }
type RecordAction = '' | 'copy' | 'remove'
type UpdateTarget = Omit<Target, 'eligible'> & {
  baseSetName: string | null
  changes: { tableNumber: number; action: 'add' | 'replace' | 'keep' | 'remove' | 'absent'; required: boolean }[]
}
type UpdatePreview = { changeCount: number; setCount: number; previewToken: string; targets: UpdateTarget[] }
type UpdatedSet = { id: string; name: string }
const changeLabels = { add: 'Add', replace: 'Replace', keep: 'Keep', remove: 'Remove', absent: 'Already absent' }

export function TableSetAssignment({ setId, revision, versions, optionalRecords, codeOnly = false }: {
  setId: string
  revision: number
  versions: SavedRecord[]
  optionalRecords: OptionalRecord[]
  codeOnly?: boolean
}) {
  const [scope, setScope] = useState<Scope>('terminal')
  const [options, setOptions] = useState<Option[]>([])
  const [targetId, setTargetId] = useState('')
  const [targets, setTargets] = useState<Target[] | null>(null)
  const [feedback, setFeedback] = useState('')
  const [busy, setBusy] = useState(false)
  const [mode, setMode] = useState(codeOnly ? 'now' : 'assignment')
  const [availableAt, setAvailableAt] = useState('')
  const [scheduleRefresh, setScheduleRefresh] = useState(0)
  const [selectedRecords, setSelectedRecords] = useState(false)
  const [actions, setActions] = useState<Record<number, RecordAction>>({})
  const [updatePreview, setUpdatePreview] = useState<UpdatePreview | null>(null)
  const [resultSets, setResultSets] = useState<UpdatedSet[]>([])
  const choices = new Map((optionalRecords ?? []).map((record) => [record.tableNumber, { name: record.name, removable: true, copyable: false }]))
  for (const record of versions) {
    if (record.tableNumber === 94) continue
    choices.set(record.tableNumber, {
      name: record.definitionName, removable: choices.get(record.tableNumber)?.removable ?? false,
      copyable: record.validationPassed && record.payloadLengthBytes > 2
        && versions.filter((candidate) => candidate.tableNumber === record.tableNumber).length === 1,
    })
  }
  const actionEntries = Object.entries(actions)
  const copyRecords = actionEntries.filter(([, action]) => action === 'copy').map(([number]) => Number(number))
  const removeRecords = actionEntries.filter(([, action]) => action === 'remove').map(([number]) => Number(number))

  useEffect(() => {
    let current = true
    void apiGet<Option[]>(scopes[scope].path)
      .then((choices) => { if (current) setOptions(choices) })
      .catch(() => { if (current) setFeedback('Could not load assignment targets.') })
    return () => { current = false }
  }, [scope])

  async function preview(event: React.FormEvent) {
    event.preventDefault()
    if (selectedRecords) {
      await updateRecords(false)
      return
    }
    setTargets(null)
    setFeedback('')
    setBusy(true)
    try {
      const params = new URLSearchParams({ targetType: scope, targetId })
      const result = await apiGet<{ targets: Target[] }>(`/api/tables/sets/${setId}/assignment-preview?${params}`)
      setTargets(result.targets)
    } catch {
      setFeedback('Could not preview this assignment.')
    } finally {
      setBusy(false)
    }
  }

  async function updateRecords(apply: boolean) {
    setBusy(true)
    setFeedback('')
    setResultSets([])
    if (!apply) setUpdatePreview(null)
    try {
      const response = await apiPostRaw(`/api/tables/sets/${setId}/record-updates${apply ? '' : '/preview'}`, {
        targetType: scope, targetId, sourceRevision: revision, copyRecords, removeRecords,
        ...(apply ? { previewToken: updatePreview?.previewToken } : {}),
      })
      const result = await response.json().catch(() => null)
      if (!response.ok) {
        if (response.status === 409) setUpdatePreview(null)
        throw new Error(result?.error ?? 'Could not update the selected records.')
      }
      if (apply) {
        setFeedback(`Updated ${result.changed} ${result.changed === 1 ? 'phone assignment' : 'phone assignments'}.`)
        setResultSets(result.sets)
        setUpdatePreview(null)
      } else setUpdatePreview(result)
    } catch (error) {
      setFeedback(error instanceof Error ? error.message : 'Could not update the selected records.')
    } finally {
      setBusy(false)
    }
  }

  async function assign() {
    const scheduled = mode !== 'assignment'
    const selected = mode === 'later' ? new Date(availableAt) : null
    if (selected && (!Number.isFinite(selected.getTime()) || selected.getTime() <= Date.now())) {
      setFeedback('Choose a future date and time.')
      return
    }
    setBusy(true)
    setFeedback('')
    try {
      const response = await apiPostRaw(`/api/tables/sets/${setId}/${scheduled ? 'schedules' : 'assignments'}`, {
        targetType: scope, targetId, terminalIds: targets?.filter((target) => target.eligible).map((target) => target.id) ?? [],
        ...(scheduled ? { availableAtUtc: selected?.toISOString() ?? null } : {}),
      })
      const result = await response.json().catch(() => null)
      if (!response.ok) throw new Error(result?.error ?? 'Could not save the assignment.')
      if (scheduled) {
        setFeedback(`Scheduled for ${result.scheduled} phones. Already scheduled for ${result.alreadyScheduled}.`)
        setScheduleRefresh((value) => value + 1)
      } else
        setFeedback(`Updated ${result.changed} of ${result.targetCount} eligible phone assignments.`)
      setTargets(null)
    } catch (error) {
      setFeedback(error instanceof Error ? error.message : 'Could not save the assignment.')
    } finally {
      setBusy(false)
    }
  }

  const eligible = targets?.filter((target) => target.eligible).length ?? 0
  return (
    <section className="table-config-panel" aria-labelledby="table-assignment-heading">
      <h2 id="table-assignment-heading">{codeOnly ? 'Schedule firmware parameters' : 'Assign this set'}</h2>
      <p className="page-intro">{codeOnly ? 'The selected phones pick up these parameters on a routine call-in or callback.'
        : 'A group, location or customer assignment updates its current phones. The phones receive the set when they request a download.'}</p>
      <form className="table-config-selectors" onSubmit={(event) => void preview(event)}>
        {!codeOnly && <label>Assignment action
          <select value={selectedRecords ? 'records' : 'set'} disabled={busy} onChange={(event) => {
            setSelectedRecords(event.target.value === 'records')
            setTargets(null)
            setUpdatePreview(null)
            setFeedback('')
            setResultSets([])
          }}>
            <option value="set">Assign full set</option>
            <option value="records">Update selected records</option>
          </select>
        </label>}
        <label>{codeOnly ? 'Schedule by' : 'Assign by'}
          <select disabled={busy} value={scope} onChange={(event) => {
            setScope(event.target.value as Scope)
            setOptions([])
            setTargetId('')
            setTargets(null)
            setUpdatePreview(null)
            setResultSets([])
            setFeedback('')
          }}>
            {Object.entries(scopes).map(([value, { label }]) => <option value={value} key={value}>{label}</option>)}
          </select>
        </label>
        <label>{scopes[scope].label}
          <select disabled={busy} value={targetId} onChange={(event) => {
            setTargetId(event.target.value)
            setTargets(null)
            setUpdatePreview(null)
            setResultSets([])
            setFeedback('')
          }}>
            <option value="">Choose {scopes[scope].label.toLowerCase()}</option>
            {options.map((item) => <option value={item.id} key={item.id}>{item.name ?? item.displayName}</option>)}
          </select>
        </label>
        {selectedRecords && <fieldset className="scope-record-choices" disabled={busy}>
          <legend>Record actions</legend>
          {[...choices.entries()].sort(([first], [second]) => first - second).map(([number, choice]) =>
            (choice.copyable || choice.removable) && <label key={number} className="scope-record-choice">
              MT{number} · {choice.name}
              <select value={actions[number] ?? ''} onChange={(event) => {
                setActions({ ...actions, [number]: event.target.value as RecordAction })
                setUpdatePreview(null)
                setFeedback('')
                setResultSets([])
              }}>
                <option value="">Keep current</option>
                {choice.copyable && <option value="copy">Use source record</option>}
                {choice.removable && <option value="remove">Remove from assignment</option>}
              </select>
            </label>)}
          {removeRecords.length > 0 && <p>Removal affects future data sets. A phone can retain the already installed record.</p>}
        </fieldset>}
        <button type="submit" disabled={!targetId || busy || (selectedRecords && !copyRecords.length && !removeRecords.length)}>
          {selectedRecords ? 'Preview record update' : codeOnly ? 'Preview phones' : 'Preview assignment'}
        </button>
      </form>
      {updatePreview && <section className="table-config-preview" aria-labelledby="scope-record-review">
        <h3 id="scope-record-review">Record update review</h3>
        <p>{updatePreview.changeCount} phone assignments will change across {updatePreview.setCount} resulting sets.</p>
        <ul>{updatePreview.targets.map((phone) => <li key={phone.id}>
          <strong>{phone.displayName}</strong>{phone.terminalFamilyLabel && ` · ${phone.terminalFamilyLabel}`}
          {phone.baseSetName && ` · ${phone.baseSetName}`}
          {phone.reason ? <p>{phone.reason}</p> : <p>{phone.changes.map(({ tableNumber, action, required }) =>
            `${changeLabels[action]} MT${tableNumber}${required ? ' (required)' : ''}`).join(', ')}</p>}
        </li>)}</ul>
        {updatePreview.changeCount > 0 && <button type="button" disabled={busy} onClick={() => void updateRecords(true)}>
          Update {updatePreview.changeCount} {updatePreview.changeCount === 1 ? 'phone' : 'phones'}
        </button>}
      </section>}
      {targets && (
        <div className="table-config-preview">
          <strong>{eligible} eligible phones</strong>
          {targets.length > 0 && <ul>{targets.map((item) => <li key={item.id}>
            {item.displayName}{item.terminalFamilyLabel && ` · ${item.terminalFamilyLabel}`}{item.reason && ` · ${item.reason}`}
          </li>)}</ul>}
          <div className="table-config-selectors">
            <label>Delivery
              <select value={mode} disabled={busy} onChange={(event) => setMode(event.target.value)}>
                {!codeOnly && <option value="assignment">Assign for explicit table requests</option>}
                <option value="now">Prepare now for the next call-in</option>
                <option value="later">Prepare at a specific time</option>
              </select>
            </label>
            {mode === 'later' && <label>Available from (local time)
              <input type="datetime-local" value={availableAt} disabled={busy} onChange={(event) => setAvailableAt(event.target.value)} />
            </label>}
            <button type="button" disabled={!eligible || busy || (mode === 'later' && !availableAt)} onClick={() => void assign()}>
              {mode === 'assignment' ? 'Assign to' : 'Schedule for'} {eligible} {eligible === 1 ? 'phone' : 'phones'}
            </button>
          </div>
        </div>
      )}
      {feedback && <p role="status">{feedback}</p>}
      {resultSets.length > 0 && <ul aria-label="Resulting table sets">{resultSets.map((set) => <li key={set.id}>
        <Link to={`/table-sets/${set.id}`}>{set.name}</Link>
      </li>)}</ul>}
      <TableDownloadScheduleList setId={setId} refresh={scheduleRefresh} />
    </section>
  )
}
