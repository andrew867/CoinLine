import { useEffect, useState } from 'react'
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
type Target = { id: string; displayName: string; eligible: boolean; reason: string | null }

export function TableSetAssignment({ setId, codeOnly = false }: { setId: string; codeOnly?: boolean }) {
  const [scope, setScope] = useState<Scope>('terminal')
  const [options, setOptions] = useState<Option[]>([])
  const [targetId, setTargetId] = useState('')
  const [targets, setTargets] = useState<Target[] | null>(null)
  const [feedback, setFeedback] = useState('')
  const [busy, setBusy] = useState(false)
  const [mode, setMode] = useState(codeOnly ? 'now' : 'assignment')
  const [availableAt, setAvailableAt] = useState('')
  const [scheduleRefresh, setScheduleRefresh] = useState(0)

  useEffect(() => {
    let current = true
    void apiGet<Option[]>(scopes[scope].path)
      .then((choices) => { if (current) setOptions(choices) })
      .catch(() => { if (current) setFeedback('Could not load assignment targets.') })
    return () => { current = false }
  }, [scope])

  async function preview(event: React.FormEvent) {
    event.preventDefault()
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
        <label>{codeOnly ? 'Schedule by' : 'Assign by'}
          <select disabled={busy} value={scope} onChange={(event) => {
            setScope(event.target.value as Scope)
            setOptions([])
            setTargetId('')
            setTargets(null)
            setFeedback('')
          }}>
            {Object.entries(scopes).map(([value, { label }]) => <option value={value} key={value}>{label}</option>)}
          </select>
        </label>
        <label>{scopes[scope].label}
          <select disabled={busy} value={targetId} onChange={(event) => {
            setTargetId(event.target.value)
            setTargets(null)
            setFeedback('')
          }}>
            <option value="">Choose {scopes[scope].label.toLowerCase()}</option>
            {options.map((item) => <option value={item.id} key={item.id}>{item.name ?? item.displayName}</option>)}
          </select>
        </label>
        <button type="submit" disabled={!targetId || busy}>{codeOnly ? 'Preview phones' : 'Preview assignment'}</button>
      </form>
      {targets && (
        <div className="table-config-preview">
          <strong>{eligible} eligible phones</strong>
          {targets.length > 0 && <ul>{targets.map((item) => <li key={item.id}>{item.displayName}{item.reason && ` · ${item.reason}`}</li>)}</ul>}
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
      <TableDownloadScheduleList setId={setId} refresh={scheduleRefresh} />
    </section>
  )
}
