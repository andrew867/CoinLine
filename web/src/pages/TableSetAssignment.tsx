import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPostRaw } from '../api/client'
import { TableDownloadScheduleList } from './TableDownloadScheduleList'

const scopes = {
  terminal: { label: 'Phone', path: '/api/terminals' },
  group: { label: 'Phone group', path: '/api/terminal-groups' },
  region: { label: 'Region', path: '/api/terminals/regions' },
  site: { label: 'Location', path: '/api/sites' },
  customer: { label: 'Customer', path: '/api/customers' },
} as const

type Scope = keyof typeof scopes
type Option = { id: string; name?: string; displayName?: string }
type Target = { id: string; displayName: string; terminalFamilyLabel: string | null; eligible: boolean; reason: string | null }
type SavedRecord = { tableNumber: number; definitionName: string; payloadLengthBytes: number; validationPassed: boolean }
type OptionalRecord = { tableNumber: number; name: string }
type RecordAction = '' | 'copy' | 'remove'
type AssignmentAction = 'set' | 'records' | 'schedule-records'
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
  const [regions, setRegions] = useState<Option[]>([])
  const [groupRegion, setGroupRegion] = useState('')
  const [targets, setTargets] = useState<Target[] | null>(null)
  const [feedback, setFeedback] = useState('')
  const [busy, setBusy] = useState(false)
  const [mode, setMode] = useState(codeOnly ? 'now' : 'assignment')
  const [availableAt, setAvailableAt] = useState('')
  const [scheduleRefresh, setScheduleRefresh] = useState(0)
  const [action, setAction] = useState<AssignmentAction>('set')
  const [actions, setActions] = useState<Record<number, RecordAction>>({})
  const [deliveryRecords, setDeliveryRecords] = useState<number[]>([])
  const [wireSelection, setWireSelection] = useState<number[] | null>(null)
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
  const selectedRecords = action === 'records'
  const partialSchedule = action === 'schedule-records'
  const schedulesOnly = codeOnly || partialSchedule
  const deliveryMode = schedulesOnly && mode === 'assignment' ? 'now' : mode
  const scopeSelection = {
    targetType: scope,
    targetId: scope === 'region' ? null : targetId,
    regionCode: (scope === 'region' ? targetId : scope === 'group' ? groupRegion : '') || undefined,
  }

  useEffect(() => {
    let current = true
    void Promise.all([
      apiGet<Option[]>(scopes[scope].path),
      scope === 'group' ? apiGet<Option[]>(scopes.region.path) : Promise.resolve([]),
    ]).then(([choices, groupRegions]) => {
      if (!current) return
      setOptions(choices)
      setRegions(groupRegions)
    })
      .catch(() => { if (current) setFeedback('Could not load assignment targets.') })
    return () => { current = false }
  }, [scope])

  function clearReview() {
    setTargets(null)
    setWireSelection(null)
    setUpdatePreview(null)
    setResultSets([])
    setFeedback('')
  }

  async function preview(event: React.FormEvent) {
    event.preventDefault()
    if (selectedRecords) {
      await updateRecords(false)
      return
    }
    setTargets(null)
    setWireSelection(null)
    setFeedback('')
    setBusy(true)
    try {
      const params = new URLSearchParams({ targetType: scope })
      if (scopeSelection.targetId) params.set('targetId', scopeSelection.targetId)
      if (scopeSelection.regionCode) params.set('regionCode', scopeSelection.regionCode)
      if (partialSchedule) deliveryRecords.forEach(number => params.append('recordNumbers', String(number)))
      const result = await apiGet<{ targets: Target[]; recordNumbers: number[] | null }>(`/api/tables/sets/${setId}/assignment-preview?${params}`)
      setTargets(result.targets)
      setWireSelection(result.recordNumbers)
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
        ...scopeSelection, sourceRevision: revision, copyRecords, removeRecords,
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
    const scheduled = deliveryMode !== 'assignment'
    const selected = deliveryMode === 'later' ? new Date(availableAt) : null
    if (selected && (!Number.isFinite(selected.getTime()) || selected.getTime() <= Date.now())) {
      setFeedback('Choose a future date and time.')
      return
    }
    setBusy(true)
    setFeedback('')
    try {
      const response = await apiPostRaw(`/api/tables/sets/${setId}/${scheduled ? 'schedules' : 'assignments'}`, {
        ...scopeSelection, terminalIds: targets?.filter((target) => target.eligible).map((target) => target.id) ?? [],
        ...(scheduled ? { availableAtUtc: selected?.toISOString() ?? null } : {}),
        ...(partialSchedule ? { recordNumbers: deliveryRecords } : {}),
      })
      const result = await response.json().catch(() => null)
      if (!response.ok) throw new Error(result?.error ?? 'Could not save the assignment.')
      if (scheduled) {
        setFeedback(`Scheduled for ${result.scheduled} ${result.scheduled === 1 ? 'phone' : 'phones'}. Already scheduled for ${result.alreadyScheduled}.`)
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
        : 'Preview the current phones in a scope before assigning a set or scheduling a download.'}</p>
      <form className="table-config-selectors" onSubmit={(event) => void preview(event)}>
        {!codeOnly && <label>Assignment action
          <select value={action} disabled={busy} onChange={(event) => {
            setAction(event.target.value as AssignmentAction)
            clearReview()
          }}>
            <option value="set">Assign full set</option>
            <option value="records">Update selected records</option>
            <option value="schedule-records">Schedule selected records</option>
          </select>
        </label>}
        <label>{schedulesOnly ? 'Schedule by' : 'Assign by'}
          <select disabled={busy} value={scope} onChange={(event) => {
            setScope(event.target.value as Scope)
            setOptions([])
            setTargetId('')
            setGroupRegion('')
            clearReview()
          }}>
            {Object.entries(scopes).map(([value, { label }]) => <option value={value} key={value}>{label}</option>)}
          </select>
        </label>
        <label>{scopes[scope].label}
          <select disabled={busy} value={targetId} onChange={(event) => {
            setTargetId(event.target.value)
            clearReview()
          }}>
            <option value="">Choose {scopes[scope].label.toLowerCase()}</option>
            {options.map((item) => <option value={item.id} key={item.id}>{item.name ?? item.displayName}</option>)}
          </select>
        </label>
        {scope === 'group' && <label>Within region
          <select disabled={busy} value={groupRegion} onChange={event => {
            setGroupRegion(event.target.value)
            clearReview()
          }}>
            <option value="">All regions</option>
            {regions.map(region => <option value={region.id} key={region.id}>{region.name}</option>)}
          </select>
        </label>}
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
        {partialSchedule && <fieldset className="scope-record-choices" disabled={busy}>
          <legend>Records to download</legend>
          {[...choices].filter(([, choice]) => choice.copyable).sort(([first], [second]) => first - second).map(([number, choice]) =>
            <label className="native-table-toggle" key={number}>
              <input type="checkbox" checked={deliveryRecords.includes(number)} onChange={event => {
                setDeliveryRecords(current => event.target.checked ? [...current, number] : current.filter(record => record !== number))
                clearReview()
              }} />
              MT{number} · {choice.name}
            </label>)}
        </fieldset>}
        <button type="submit" disabled={!targetId || busy || (selectedRecords && !copyRecords.length && !removeRecords.length)
          || (partialSchedule && !deliveryRecords.length)}>
          {selectedRecords ? 'Preview record update' : schedulesOnly ? 'Preview phones' : 'Preview assignment'}
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
          <strong>{eligible} eligible {eligible === 1 ? 'phone' : 'phones'}</strong>
          {wireSelection && <p>Records to send {wireSelection.map(number => `MT${number}`).join(', ')}</p>}
          {targets.length > 0 && <ul>{targets.map((item) => <li key={item.id}>
            {item.displayName}{item.terminalFamilyLabel && ` · ${item.terminalFamilyLabel}`}{item.reason && ` · ${item.reason}`}
          </li>)}</ul>}
          <div className="table-config-selectors">
            <label>Delivery
              <select value={deliveryMode} disabled={busy} onChange={(event) => setMode(event.target.value)}>
                {!schedulesOnly && <option value="assignment">Assign for explicit table requests</option>}
                <option value="now">Prepare now for the next call-in</option>
                <option value="later">Prepare at a specific time</option>
              </select>
            </label>
            {deliveryMode === 'later' && <label>Available from (local time)
              <input type="datetime-local" value={availableAt} disabled={busy} onChange={(event) => setAvailableAt(event.target.value)} />
            </label>}
            <button type="button" disabled={!eligible || busy || (deliveryMode === 'later' && !availableAt)} onClick={() => void assign()}>
              {deliveryMode === 'assignment' ? 'Assign to' : 'Schedule for'} {eligible} {eligible === 1 ? 'phone' : 'phones'}
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
