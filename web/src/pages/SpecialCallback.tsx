import { useEffect, useState, type FormEvent } from 'react'
import { apiFetch, apiGet, apiPostRaw } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'

type Callback = {
  revision: number
  requestedAtUtc: string
  dueAtUtc: string | null
  terminalClockOffsetMinutes: number
  deliverAssignedTables: boolean
  programmedForUtc: string | null
  acknowledgedAtUtc: string | null
  returnedAtUtc: string | null
  cancelledAtUtc: string | null
}

function localInput(value: string | null | undefined) {
  if (!value) return ''
  const time = new Date(value)
  const local = new Date(time.getTime() - time.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 16)
}

export function SpecialCallback({ terminalId, terminalActive = true }: { terminalId: string; terminalActive?: boolean }) {
  const endpoint = `/api/terminals/${terminalId}/special-callback`
  const [callback, setCallback] = useState<Callback | null | undefined>(undefined)
  const [specific, setSpecific] = useState(false)
  const [due, setDue] = useState('')
  const [offset, setOffset] = useState('0')
  const [tables, setTables] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const canUpdate = canPerform('callbacks', op.update)
  const canRequest = canUpdate && terminalActive

  useEffect(() => {
    let current = true
    void apiGet<{ callback: Callback | null }>(endpoint).then(({ callback: saved }) => {
      if (!current) return
      setCallback(saved)
      setSpecific(!!saved?.dueAtUtc && !saved.returnedAtUtc && !saved.cancelledAtUtc)
      setDue(localInput(saved?.dueAtUtc))
      setOffset(String(saved?.terminalClockOffsetMinutes ?? 0))
      setTables(saved?.deliverAssignedTables ?? false)
    }).catch(() => { if (current) setError('Could not load special callback status.') })
    return () => { current = false }
  }, [endpoint])

  async function refresh() {
    setBusy(true)
    setError('')
    setMessage('')
    try { setCallback((await apiGet<{ callback: Callback | null }>(endpoint)).callback) }
    catch { setError('Could not refresh special callback status.') }
    finally { setBusy(false) }
  }

  async function update(event?: FormEvent) {
    event?.preventDefault()
    const cancel = event == null
    if (!canUpdate || (cancel ? !callback : !canRequest)) return
    const moment = specific && !cancel ? new Date(due) : null
    if (moment && (!Number.isFinite(moment.getTime()) || moment.getTime() <= Date.now())) {
      setError('Choose a future date and time.')
      return
    }

    setBusy(true)
    setError('')
    setMessage('')
    try {
      const response = cancel
        ? await apiPostRaw(`${endpoint}/cancel`, { revision: callback?.revision })
        : await apiFetch(endpoint, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({
          revision: callback?.revision ?? null, dueAtUtc: moment?.toISOString() ?? null,
          terminalClockOffsetMinutes: Number(offset), deliverAssignedTables: tables,
        }) })
      const result = await response.json().catch(() => null)
      if (!response.ok) throw new Error(result?.error ?? 'Could not save the callback request.')
      setCallback(result.callback)
      setMessage(cancel ? 'Callback cancelled.' : 'Callback queued for the phone’s next management call.')
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Could not save the callback request.')
    } finally {
      setBusy(false)
    }
  }

  const active = callback && !callback.returnedAtUtc && !callback.cancelledAtUtc
  const status = callback?.cancelledAtUtc ? 'Cancelled' : callback?.returnedAtUtc ? 'Callback received'
    : callback?.acknowledgedAtUtc ? 'Phone acknowledged' : callback ? 'Awaiting a management call' : 'No callback requested'

  return <section aria-labelledby="special-callback-heading" className="table-config-panel">
    <h2 id="special-callback-heading">Special callback</h2>
    {callback !== undefined && <dl className="native-table-summary">
      <div><dt>Status</dt><dd>{status}</dd></div>
      {callback?.programmedForUtc && <div><dt>Programmed for</dt><dd>{new Date(callback.programmedForUtc).toLocaleString()}</dd></div>}
      {callback?.returnedAtUtc && <div><dt>Received</dt><dd>{new Date(callback.returnedAtUtc).toLocaleString()}</dd></div>}
    </dl>}
    {!terminalActive && <p>Reactivate this phone before requesting another callback. Existing requests may still be cancelled.</p>}
    <form className="table-config-selectors" onSubmit={(event) => void update(event)}>
      <label>Callback timing
        <select value={specific ? 'time' : 'asap'} disabled={busy || !canRequest} onChange={(event) => setSpecific(event.target.value === 'time')}>
          <option value="asap">As soon as possible</option>
          <option value="time">At a specific time</option>
        </select>
      </label>
      {specific && <label>Date and time (your local time)
        <input type="datetime-local" value={due} required disabled={busy || !canRequest} onChange={(event) => setDue(event.target.value)} />
      </label>}
      <label>Terminal clock offset from UTC (minutes)
        <input type="number" min={-840} max={840} step={1} required value={offset} disabled={busy || !canRequest} onChange={(event) => setOffset(event.target.value)} />
      </label>
      <label className="native-table-toggle">
        <input type="checkbox" checked={tables} disabled={busy || !canRequest} onChange={(event) => setTables(event.target.checked)} />
        Deliver assigned tables on callback
      </label>
      <button type="submit" disabled={busy || !canRequest || callback === undefined}>{active ? 'Replace callback' : 'Request callback'}</button>
      <button type="button" className="quiet" disabled={busy} onClick={() => void refresh()}>Refresh status</button>
      {active && <button type="button" className="quiet" disabled={busy || !canUpdate} onClick={() => void update()}>Cancel callback</button>}
    </form>
    {active && callback.acknowledgedAtUtc && <p>A phone that has already acknowledged the request can still call after cancellation.</p>}
    {error && <p role="alert">{error}</p>}
    {message && <p role="status">{message}</p>}
  </section>
}
