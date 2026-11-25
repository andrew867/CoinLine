import { lazy, Suspense, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiGetRaw, apiPostRaw } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { TableDownloadScheduleList } from './TableDownloadScheduleList'

const RecordUpdateRollouts = lazy(async () => ({ default: (await import('./RecordUpdateRollouts')).RecordUpdateRollouts }))

const scopes = {
  terminal: { label: 'Phone', path: '/api/terminals' },
  group: { label: 'Phone group', path: '/api/terminal-groups' },
  'group-pattern': { label: 'Group ID pattern', path: null },
  region: { label: 'Region', path: '/api/terminals/regions' },
  'time-area': { label: 'Time area', path: '/api/terminals/time-areas' },
  'configuration-id': { label: 'Configuration ID', path: null },
  'terminal-id-range': { label: 'Terminal ID range', path: null },
  'software-version': { label: 'Current software version', path: null },
  site: { label: 'Location', path: '/api/sites' },
  customer: { label: 'Customer', path: '/api/customers' },
} as const

type Scope = keyof typeof scopes
type Option = { id: string; name?: string; displayName?: string; code?: string | null }
type Target = {
  id: string; displayName: string; terminalFamilyLabel: string | null; eligible: boolean; reason: string | null
  terminalIdHex?: string | null; replacesFirmwareScheduleId?: string | null
  firmwareStartAtUtc?: string | null; firmwareSlotEndAtUtc?: string | null; firmwareServerSlot?: number | null
}
export type FirmwareSchedule = { enabled: boolean; selectedBlocks: number; minimumTransferMinutes: number | null; startAtUtc: string | null }
type FirmwareAllocation = {
  previewToken: string; selectedBlocks: number; minimumTransferMinutes: number; activeServers: number; slotMinutes: number
  firstStartAtUtc: string | null; lastEndAtUtc: string | null
  existingReservations?: number; fixedTimeSchedules?: number
}
const utcTime = (value: string) => new Date(value).toISOString().slice(0, 16).replace('T', ' ') + ' UTC'
type SavedRecord = {
  tableNumber: number; definitionName: string; payloadLengthBytes: number; validationPassed: boolean; configurationId: string | null
}
type OptionalRecord = { tableNumber: number; name: string }
type RecordAction = '' | 'copy' | 'remove'
type AssignmentAction = 'set' | 'records' | 'schedule-records'
type UpdateTarget = Omit<Target, 'eligible'> & {
  baseSetName: string | null
  changes: { tableNumber: number; action: 'add' | 'replace' | 'keep' | 'remove' | 'absent'; required: boolean }[]
}
type UpdatePreview = {
  changeCount: number; setCount: number; previewToken: string; targets: UpdateTarget[]
  rolloutId: string | null; pendingCount: number
}
type UpdatedSet = { id: string; name: string }
const changeLabels = { add: 'Add', replace: 'Replace', keep: 'Keep', remove: 'Remove', absent: 'Already absent' }
const callInParameters = 35

export function TableSetAssignment({ setId, revision, versions, optionalRecords, codeOnly = false, firmwareSchedule }: {
  setId: string
  revision: number
  versions: SavedRecord[]
  optionalRecords: OptionalRecord[]
  codeOnly?: boolean
  firmwareSchedule?: FirmwareSchedule | null
}) {
  const [scope, setScope] = useState<Scope>('terminal')
  const [options, setOptions] = useState<Option[]>([])
  const [targetId, setTargetId] = useState('')
  const [secondTarget, setSecondTarget] = useState('')
  const [regions, setRegions] = useState<Option[]>([])
  const [groupRegion, setGroupRegion] = useState('')
  const [targets, setTargets] = useState<Target[] | null>(null)
  const [feedback, setFeedback] = useState('')
  const [busy, setBusy] = useState(false)
  const [mode, setMode] = useState(codeOnly ? 'now' : 'assignment')
  const [availableAt, setAvailableAt] = useState('')
  const [allowFirmwareReschedule, setAllowFirmwareReschedule] = useState(false)
  const [allocateFirmwareWindow, setAllocateFirmwareWindow] = useState(false)
  const [firmwareWindow, setFirmwareWindow] = useState({
    startDate: new Date(Date.now() + 86400000).toISOString().slice(0, 10),
    dailyStart: '00:00', dailyEnd: '04:00', activeServers: '1', slotMinutes: '',
  })
  const [firmwareAllocation, setFirmwareAllocation] = useState<FirmwareAllocation | null>(null)
  const [scheduleRefresh, setScheduleRefresh] = useState(0)
  const [action, setAction] = useState<AssignmentAction>('set')
  const [actions, setActions] = useState<Record<number, RecordAction>>({})
  const [deliveryRecords, setDeliveryRecords] = useState<number[]>([])
  const [wireSelection, setWireSelection] = useState<number[] | null>(null)
  const [updatePreview, setUpdatePreview] = useState<UpdatePreview | null>(null)
  const [stopOnIssue, setStopOnIssue] = useState(false)
  const [beginWithTerminalId, setBeginWithTerminalId] = useState('')
  const [rolloutRefresh, setRolloutRefresh] = useState(0)
  const [resultSets, setResultSets] = useState<UpdatedSet[]>([])
  const canAssign = canPerform('table-assignments', op.update)
  const canSchedule = canPerform('downloads', op.add)
  const canReadRollouts = canPerform('table-records', op.read)
  const canUpdateRecords = canPerform('table-records', op.update)
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
  const hasRegionFilter = scope === 'group' || scope === 'group-pattern'
  const canTargetTimeArea = codeOnly || (partialSchedule && choices.get(callInParameters)?.copyable)
  const firmwareScope = scope === 'terminal-id-range' || scope === 'software-version'
  const configurationScope = scope === 'configuration-id'
  const namedRecords = configurationScope ? versions.filter(record => record.configurationId && choices.get(record.tableNumber)?.copyable) : []
  const configurationRecord = namedRecords.find(record => String(record.tableNumber) === targetId)
  const selectedDeliveryRecords = configurationRecord ? [configurationRecord.tableNumber] : deliveryRecords
  const prefix = scope === 'terminal-id-range' ? /^(\d{1,9})\*$/.exec(targetId)?.[1] : undefined
  const firmwareTarget = scope === 'terminal-id-range' ? {
    terminalIdFrom: prefix?.padEnd(10, '0') ?? targetId,
    terminalIdTo: prefix?.padEnd(10, '9') ?? (secondTarget || targetId),
  } : scope === 'software-version' ? {
    currentControlRom: targetId.trim(),
    currentControlSoftware: secondTarget.trim(),
  } : undefined
  const deliveryMode = schedulesOnly && mode === 'assignment' ? 'now' : mode
  const scopeSelection = {
    targetType: scope,
    targetId: firmwareScope || configurationScope || scope === 'region' || scope === 'group-pattern' || scope === 'time-area' ? null : targetId,
    regionCode: (scope === 'region' ? targetId : hasRegionFilter ? groupRegion : '') || undefined,
    groupPattern: scope === 'group-pattern' ? targetId.trim() : undefined,
    timeAreaCode: scope === 'time-area' ? targetId : undefined,
    configurationId: configurationRecord?.configurationId,
    firmwareTarget,
  }

  useEffect(() => {
    let current = true
    const path = scopes[scope].path
    void Promise.all([
      path ? apiGet<Option[]>(path) : Promise.resolve([]),
      hasRegionFilter ? apiGet<Option[]>(scopes.region.path) : Promise.resolve([]),
    ]).then(([choices, groupRegions]) => {
      if (!current) return
      setOptions(choices)
      setRegions(groupRegions)
    })
      .catch(() => { if (current) setFeedback('Could not load assignment targets.') })
    return () => { current = false }
  }, [scope, hasRegionFilter])

  function clearReview() {
    setTargets(null)
    setWireSelection(null)
    setUpdatePreview(null)
    setResultSets([])
    setFeedback('')
    setFirmwareAllocation(null)
  }

  function changeFirmwareWindow(field: keyof typeof firmwareWindow, value: string) {
    setFirmwareWindow(current => ({ ...current, [field]: value }))
    clearReview()
  }

  function changeScope(next: Scope) {
    if (scope === next) return
    setScope(next)
    setOptions([])
    setTargetId('')
    setSecondTarget('')
    setGroupRegion('')
    if (next === 'time-area' && partialSchedule) setDeliveryRecords([callInParameters])
    clearReview()
  }

  async function preview(event: React.FormEvent) {
    event.preventDefault()
    if (selectedRecords) {
      await updateRecords(false)
      return
    }
    setTargets(null)
    setWireSelection(null)
    setFirmwareAllocation(null)
    setFeedback('')
    setBusy(true)
    try {
      const params = new URLSearchParams({ targetType: scope })
      if (scopeSelection.targetId) params.set('targetId', scopeSelection.targetId)
      if (scopeSelection.regionCode) params.set('regionCode', scopeSelection.regionCode)
      if (scopeSelection.groupPattern) params.set('groupPattern', scopeSelection.groupPattern)
      if (scopeSelection.timeAreaCode) params.set('timeAreaCode', scopeSelection.timeAreaCode)
      for (const [key, value] of Object.entries(firmwareTarget ?? {})) {
        if (value) params.set(`firmwareTarget.${key}`, value)
      }
      if (scopeSelection.configurationId) params.set('configurationId', scopeSelection.configurationId)
      if (partialSchedule) selectedDeliveryRecords.forEach(number => params.append('recordNumbers', String(number)))
      if (codeOnly) params.set('allowFirmwareReschedule', String(allowFirmwareReschedule))
      if (codeOnly && allocateFirmwareWindow)
        for (const [key, value] of Object.entries(firmwareWindow))
          params.set(`firmwareWindow.${key}`, value)
      const response = await apiGetRaw(`/api/tables/sets/${setId}/assignment-preview?${params}`)
      const result = await response.json().catch(() => null)
      if (!response.ok) throw new Error(result?.error ?? 'Could not preview this assignment.')
      setTargets(result.targets)
      setWireSelection(result.recordNumbers)
      setFirmwareAllocation(result.firmwareAllocation ?? null)
    } catch (error) {
      setFeedback(error instanceof Error ? error.message : 'Could not preview this assignment.')
    } finally {
      setBusy(false)
    }
  }

  async function updateRecords(apply: boolean) {
    if (!canReadRollouts || (apply && !canUpdateRecords)) return
    setBusy(true)
    setFeedback('')
    setResultSets([])
    if (!apply) setUpdatePreview(null)
    try {
      const response = await apiPostRaw(`/api/tables/sets/${setId}/record-updates${apply ? '' : '/preview'}`, {
        ...scopeSelection, sourceRevision: revision, copyRecords, removeRecords, stopOnIssue,
        beginWithTerminalId: stopOnIssue ? beginWithTerminalId.trim() || undefined : undefined,
        ...(apply ? { previewToken: updatePreview?.previewToken, rolloutId: updatePreview?.rolloutId } : {}),
      })
      const result = await response.json().catch(() => null)
      if (!response.ok) {
        if (response.status === 409) setUpdatePreview(null)
        throw new Error(result?.error ?? 'Could not update the selected records.')
      }
      if (apply) {
        if (result.run) {
          setFeedback(`Rollout ${String(result.run.status).toLowerCase()}. ${result.run.changedCount} phone assignments updated.`)
          setRolloutRefresh(value => value + 1)
        } else setFeedback(`Updated ${result.changed} ${result.changed === 1 ? 'phone assignment' : 'phone assignments'}.`)
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
    if (scheduled ? !canSchedule : !canAssign) return
    const selected = deliveryMode === 'later' ? new Date(availableAt) : null
    if (selected && (!Number.isFinite(selected.getTime()) || selected.getTime() <= Date.now())) {
      setFeedback('Choose a future date and time.')
      return
    }
    if (firmwareAllocation?.firstStartAtUtc && selected &&
        selected.getTime() >= new Date(firmwareAllocation.firstStartAtUtc).getTime()) {
      setFeedback('Choose parameter preparation before the first allocated terminal start.')
      return
    }
    setBusy(true)
    setFeedback('')
    try {
      const response = await apiPostRaw(`/api/tables/sets/${setId}/${scheduled ? 'schedules' : 'assignments'}`, {
        ...scopeSelection, terminalIds: targets?.filter((target) => target.eligible).map((target) => target.id) ?? [],
        ...(scheduled ? { availableAtUtc: selected?.toISOString() ?? null } : {}),
        ...(partialSchedule ? { recordNumbers: selectedDeliveryRecords } : {}),
        ...(codeOnly ? {
          allowFirmwareReschedule,
          replacesFirmwareScheduleIds: targets?.filter(target => target.eligible && target.replacesFirmwareScheduleId)
            .map(target => target.replacesFirmwareScheduleId) ?? [],
          ...(allocateFirmwareWindow ? {
            firmwareWindow: {
              ...firmwareWindow, dailyStart: firmwareWindow.dailyStart + ':00', dailyEnd: firmwareWindow.dailyEnd + ':00',
              activeServers: Number(firmwareWindow.activeServers), slotMinutes: Number(firmwareWindow.slotMinutes),
            },
            firmwarePreviewToken: firmwareAllocation?.previewToken,
          } : {}),
        } : {}),
      })
      const result = await response.json().catch(() => null)
      if (!response.ok) throw new Error(result?.error ?? 'Could not save the assignment.')
      if (scheduled) {
        setFeedback(codeOnly
          ? `Scheduled firmware parameters for ${result.scheduled} ${result.scheduled === 1 ? 'phone' : 'phones'}. Replaced ${result.replaced} existing ${result.replaced === 1 ? 'schedule' : 'schedules'}. Skipped ${result.skipped} ${result.skipped === 1 ? 'phone' : 'phones'}.`
          : `Scheduled for ${result.scheduled} ${result.scheduled === 1 ? 'phone' : 'phones'}. Already scheduled for ${result.alreadyScheduled}.`)
        setScheduleRefresh((value) => value + 1)
      } else
        setFeedback(`Updated ${result.changed} of ${result.targetCount} eligible phone assignments.`)
      setTargets(null)
      setFirmwareAllocation(null)
    } catch (error) {
      setFeedback(error instanceof Error ? error.message : 'Could not save the assignment.')
    } finally {
      setBusy(false)
    }
  }

  const eligible = targets?.filter((target) => target.eligible).length ?? 0
  const replacements = targets?.filter(target => target.eligible && target.replacesFirmwareScheduleId).length ?? 0
  return (
    <section className="table-config-panel" aria-labelledby="table-assignment-heading">
      <h2 id="table-assignment-heading">{codeOnly ? 'Schedule firmware parameters' : 'Assign this set'}</h2>
      <p className="page-intro">{codeOnly ? 'The selected phones pick up these parameters on a routine call-in or callback.'
        : 'Preview the current phones in a scope before assigning a set or scheduling a download.'}</p>
      <form className="table-config-selectors" onSubmit={(event) => void preview(event)}>
        {!codeOnly && <label>Assignment action
          <select value={action} disabled={busy} onChange={(event) => {
            const next = event.target.value as AssignmentAction
            setAction(next)
            if ((scope === 'time-area' || scope === 'configuration-id') && next !== 'schedule-records') changeScope('terminal')
            else clearReview()
          }}>
            <option value="set">Assign full set</option>
            {canReadRollouts && <option value="records">Update selected records</option>}
            {canSchedule && <option value="schedule-records">Schedule selected records</option>}
          </select>
        </label>}
        <label>{schedulesOnly ? 'Schedule by' : 'Assign by'}
          <select disabled={busy} value={scope} onChange={(event) => changeScope(event.target.value as Scope)}>
            {Object.entries(scopes).filter(([value]) => {
              if (value === 'terminal-id-range' || value === 'software-version') return codeOnly
              if (value === 'configuration-id') return partialSchedule
              return value !== 'time-area' || canTargetTimeArea
            })
              .map(([value, { label }]) => <option value={value} key={value}>{label}</option>)}
          </select>
        </label>
        {scope === 'terminal-id-range' && <>
          <label>First terminal ID
            <input value={targetId} disabled={busy} inputMode="tel" maxLength={10} pattern={'[0-9]{10}|[0-9]{1,9}\\*'}
              aria-describedby="firmware-range-help" onChange={event => {
                setTargetId(event.target.value)
                clearReview()
              }} />
          </label>
          <label>Last terminal ID
            <input value={prefix ? prefix.padEnd(10, '9') : secondTarget} disabled={busy || !!prefix} inputMode="numeric"
              maxLength={10} pattern="[0-9]{10}" onChange={event => {
                setSecondTarget(event.target.value)
                clearReview()
              }} />
          </label>
          <p id="firmware-range-help">Use ten digits. Leave the last ID blank for one phone, or end a prefix with * to include every phone in that range.</p>
        </>}
        {scope === 'software-version' && <>
          <label>Current controller ROM
            <input value={targetId} disabled={busy} maxLength={7} onChange={event => {
              setTargetId(event.target.value)
              clearReview()
            }} />
          </label>
          <label>Current controller software
            <input value={secondTarget} disabled={busy} maxLength={4} onChange={event => {
              setSecondTarget(event.target.value)
              clearReview()
            }} />
          </label>
          <p>Enter the controller versions shown in the phone’s latest NCC software report.</p>
        </>}
        {configurationScope && <label>Configuration ID
          <select disabled={busy} value={targetId} onChange={event => { setTargetId(event.target.value); clearReview() }}>
            <option value="">Choose a named record</option>
            {namedRecords.map(record => <option value={record.tableNumber} key={record.tableNumber}>
              {record.configurationId} · MT{record.tableNumber} · {record.definitionName}
            </option>)}
          </select>
        </label>}
        {!firmwareScope && !configurationScope && (scope === 'group-pattern' ? <label>Group ID pattern
          <input value={targetId} disabled={busy} maxLength={6} placeholder="ABC*" onChange={event => {
            setTargetId(event.target.value)
            clearReview()
          }} />
        </label> : <label>{scopes[scope].label}
          <select disabled={busy} value={targetId} onChange={(event) => {
            setTargetId(event.target.value)
            clearReview()
          }}>
            <option value="">Choose {scopes[scope].label.toLowerCase()}</option>
            {options.map((item) => <option value={item.id} key={item.id}>
              {item.name ?? item.displayName}{item.code && ` (${item.code})`}
            </option>)}
          </select>
        </label>)}
        {hasRegionFilter && <label>Within region
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
          <label className="native-table-toggle">
            <input type="checkbox" checked={stopOnIssue} onChange={event => { setStopOnIssue(event.target.checked); clearReview() }} />
            Pause rollout at the first blocked phone
          </label>
          {stopOnIssue && <label>Begin with terminal ID
            <input value={beginWithTerminalId} pattern="[0-9A-Fa-f]{10}" maxLength={10}
              onChange={event => { setBeginWithTerminalId(event.target.value); clearReview() }} />
          </label>}
        </fieldset>}
        {partialSchedule && !configurationScope && <fieldset className="scope-record-choices" disabled={busy}>
          <legend>Records to download</legend>
          {[...choices].filter(([number, choice]) => choice.copyable && (scope !== 'time-area' || number === callInParameters))
            .sort(([first], [second]) => first - second).map(([number, choice]) =>
            <label className="native-table-toggle" key={number}>
              <input type="checkbox" checked={deliveryRecords.includes(number)} onChange={event => {
                setDeliveryRecords(current => event.target.checked ? [...current, number] : current.filter(record => record !== number))
                clearReview()
              }} />
              MT{number} · {choice.name}
            </label>)}
        </fieldset>}
        {codeOnly && <label className="native-table-toggle">
          <input type="checkbox" checked={allowFirmwareReschedule} disabled={busy}
            onChange={event => { setAllowFirmwareReschedule(event.target.checked); clearReview() }} />
          Allow rescheduling existing firmware parameters
        </label>}
        {codeOnly && firmwareSchedule?.enabled && (firmwareSchedule.minimumTransferMinutes ?? 0) > 0 && <label className="native-table-toggle">
          <input type="checkbox" checked={allocateFirmwareWindow} disabled={busy}
            onChange={event => { setAllocateFirmwareWindow(event.target.checked); clearReview() }} />
          Allocate terminal starts across a daily download window
        </label>}
        {codeOnly && allocateFirmwareWindow && <fieldset className="scope-record-choices" disabled={busy}>
          <legend>Firmware download window (UTC)</legend>
          <label>First date
            <input type="date" required value={firmwareWindow.startDate} onChange={event => changeFirmwareWindow('startDate', event.target.value)} />
          </label>
          <label>Daily start
            <input type="time" required value={firmwareWindow.dailyStart} onChange={event => changeFirmwareWindow('dailyStart', event.target.value)} />
          </label>
          <label>Daily end
            <input type="time" required value={firmwareWindow.dailyEnd} onChange={event => changeFirmwareWindow('dailyEnd', event.target.value)} />
          </label>
          <label>Active servers
            <input type="number" min={1} max={99} required value={firmwareWindow.activeServers}
              onChange={event => changeFirmwareWindow('activeServers', event.target.value)} />
          </label>
          <label>Minutes per phone
            <input type="number" min={firmwareSchedule?.minimumTransferMinutes ?? 1} max={1440} required
              value={firmwareWindow.slotMinutes} onChange={event => changeFirmwareWindow('slotMinutes', event.target.value)} />
          </label>
          <p>{firmwareSchedule?.selectedBlocks} selected 4 KiB blocks require at least {firmwareSchedule?.minimumTransferMinutes} minutes of raw modem transfer.
            Estimate the complete slot, including connection overhead and retries. Allocation repeats daily if more phones remain.</p>
        </fieldset>}
        <button type="submit" disabled={!targetId || busy || (scope === 'software-version' && !secondTarget.trim())
          || (selectedRecords && !copyRecords.length && !removeRecords.length)
          || (configurationScope && !configurationRecord) || (partialSchedule && !selectedDeliveryRecords.length)}>
          {selectedRecords ? 'Preview record update' : schedulesOnly ? 'Preview phones' : 'Preview assignment'}
        </button>
      </form>
      {updatePreview && <section className="table-config-preview" aria-labelledby="scope-record-review">
        <h3 id="scope-record-review">Record update review</h3>
        <p>{updatePreview.changeCount} phone assignments will change across {updatePreview.setCount} resulting sets.</p>
        {updatePreview.pendingCount > 0 && <p>{updatePreview.pendingCount} phones will wait for rollout recovery.</p>}
        <ul>{updatePreview.targets.map((phone) => <li key={phone.id}>
          <strong>{phone.displayName}</strong>{phone.terminalFamilyLabel && ` · ${phone.terminalFamilyLabel}`}
          {phone.terminalIdHex && ` · ${phone.terminalIdHex}`}
          {phone.baseSetName && ` · ${phone.baseSetName}`}
          {phone.reason ? <p>{phone.reason}</p> : <p>{phone.changes.map(({ tableNumber, action, required }) =>
            `${changeLabels[action]} MT${tableNumber}${required ? ' (required)' : ''}`).join(', ')}</p>}
        </li>)}</ul>
        {canUpdateRecords && (updatePreview.changeCount > 0 || (updatePreview.rolloutId && updatePreview.targets.length > 0)) &&
          <button type="button" disabled={busy} onClick={() => void updateRecords(true)}>
            {updatePreview.rolloutId ? 'Start rollout' : `Update ${updatePreview.changeCount} ${updatePreview.changeCount === 1 ? 'phone' : 'phones'}`}
        </button>}
      </section>}
      {targets && (
        <div className="table-config-preview">
          <strong>{eligible} eligible {eligible === 1 ? 'phone' : 'phones'}</strong>
          {codeOnly && <p>{replacements} existing {replacements === 1 ? 'schedule' : 'schedules'} to replace. {targets.length - eligible} {targets.length - eligible === 1 ? 'phone' : 'phones'} to skip.</p>}
          {firmwareAllocation && <p>
            {firmwareAllocation.selectedBlocks} selected blocks. {firmwareAllocation.activeServers} active server slots, {firmwareAllocation.slotMinutes} minutes per phone.
            {firmwareAllocation.firstStartAtUtc && firmwareAllocation.lastEndAtUtc &&
              <> First start {utcTime(firmwareAllocation.firstStartAtUtc)}. Last slot ends {utcTime(firmwareAllocation.lastEndAtUtc)}.</>}
            {!!firmwareAllocation.existingReservations && <> {firmwareAllocation.existingReservations} existing {firmwareAllocation.existingReservations === 1 ? 'reservation' : 'reservations'} accounted for.</>}
          </p>}
          {!!firmwareAllocation?.fixedTimeSchedules && <p role="note">
            {firmwareAllocation.fixedTimeSchedules} fixed-time {firmwareAllocation.fixedTimeSchedules === 1 ? 'schedule has' : 'schedules have'} no reserved duration in this code-server pool.
          </p>}
          {wireSelection && <p>Records to send {wireSelection.map(number => `MT${number}`).join(', ')}</p>}
          {targets.length > 0 && <ul>{targets.map((item) => <li key={item.id}>
            {item.displayName}{item.terminalIdHex && ` · ${item.terminalIdHex}`}
            {item.terminalFamilyLabel && ` · ${item.terminalFamilyLabel}`}{item.reason && ` · ${item.reason}`}
            {item.firmwareStartAtUtc && item.firmwareSlotEndAtUtc &&
              <div>{utcTime(item.firmwareStartAtUtc)} to {utcTime(item.firmwareSlotEndAtUtc)} · Server slot {item.firmwareServerSlot}</div>}
          </li>)}</ul>}
          <div className="table-config-selectors">
            <label>Delivery
              <select value={deliveryMode} disabled={busy} onChange={(event) => setMode(event.target.value)}>
                {!schedulesOnly && <option value="assignment">Assign for explicit table requests</option>}
                {canSchedule && <option value="now">Prepare now for the next call-in</option>}
                {canSchedule && <option value="later">Prepare at a specific time</option>}
              </select>
            </label>
            {deliveryMode === 'later' && <label>Prepare at (local time)
              <input type="datetime-local" value={availableAt} disabled={busy} onChange={(event) => setAvailableAt(event.target.value)} />
            </label>}
            <button type="button" disabled={(deliveryMode === 'assignment' ? !canAssign : !canSchedule) || !eligible || busy || (deliveryMode === 'later' && !availableAt) ||
              (allocateFirmwareWindow && !firmwareAllocation)} onClick={() => void assign()}>
              {deliveryMode === 'assignment' ? 'Assign to' : 'Schedule for'} {eligible} {eligible === 1 ? 'phone' : 'phones'}
            </button>
          </div>
        </div>
      )}
      {feedback && <p role="status">{feedback}</p>}
      {resultSets.length > 0 && <ul aria-label="Resulting table sets">{resultSets.map((set) => <li key={set.id}>
        <Link to={`/table-sets/${set.id}`}>{set.name}</Link>
      </li>)}</ul>}
      {!codeOnly && canReadRollouts && <Suspense fallback={null}><RecordUpdateRollouts setId={setId} refresh={rolloutRefresh} /></Suspense>}
      {canPerform('downloads', op.read) && <TableDownloadScheduleList setId={setId} refresh={scheduleRefresh} />}
    </section>
  )
}
