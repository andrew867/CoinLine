import { useEffect, useState, type FormEvent } from 'react'
import { apiFetch, apiGet } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'

export type TerminalLifecycle = {
  active: boolean
  deactivatedAtUtc: string | null
  deletionReviewDate: string | null
  reviewDue: boolean
  defaultDeletionReviewDate: string
  revision: number
}

export function TerminalLifecyclePanel({ terminalId, onChanged }: {
  terminalId: string; onChanged: (value: TerminalLifecycle) => void
}) {
  const path = `/api/terminals/${terminalId}/lifecycle`
  const canUpdate = canPerform('phone-lifecycle', op.update)
  const [state, setState] = useState<TerminalLifecycle | null>(null)
  const [reviewDate, setReviewDate] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  function accept(value: TerminalLifecycle) {
    setState(value)
    setReviewDate(value.deletionReviewDate ?? value.defaultDeletionReviewDate)
  }

  useEffect(() => {
    let current = true
    void apiGet<TerminalLifecycle>(path).then(value => { if (current) accept(value) })
      .catch(() => { if (current) setError('Could not load terminal lifecycle status.') })
    return () => { current = false }
  }, [path])

  async function save(active: boolean) {
    if (!state || !canUpdate) return
    setBusy(true)
    setNotice('')
    setError('')
    try {
      const response = await apiFetch(path, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ active, revision: state.revision, deletionReviewDate: active ? null : reviewDate || null }),
      })
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? `Could not save lifecycle status (HTTP ${response.status}).`)
      const value = await response.json() as TerminalLifecycle
      accept(value)
      onChanged(value)
      setNotice(active ? 'Phone reactivated.' : state.active ? 'Phone deactivated.' : 'Deletion review date updated.')
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Could not save lifecycle status.')
    } finally {
      setBusy(false)
    }
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    void save(false)
  }

  return <section className="table-config-panel" aria-labelledby="terminal-lifecycle-heading">
    <h2 id="terminal-lifecycle-heading">Deactivate or reactivate</h2>
    {error && <p role="alert">{error}</p>}
    {notice && <p role="status">{notice}</p>}
    {!state && !error && <p>Loading lifecycle status…</p>}
    {state && <>
      <p><strong>{state.active ? 'Active in Manager' : 'Inactive in Manager'}</strong></p>
      {!state.active && <dl>
        <dt>Deactivation date</dt>
        <dd>{state.deactivatedAtUtc?.slice(0, 10)}</dd>
        <dt>Deletion review date</dt>
        <dd>{state.deletionReviewDate}{state.reviewDue && ' (review due)'}</dd>
      </dl>}
      <p className="page-intro">Inactive phones are refused new NCC dialogues and excluded from new table rollouts.
        The default 45-day grace period is a deletion review date. CoinLine retains the records and does not delete them automatically.</p>
      <form className="row-actions" onSubmit={submit}>
        <label>Deletion review date
          <input type="date" value={reviewDate} disabled={busy || !canUpdate}
            onChange={event => setReviewDate(event.target.value)} />
        </label>
        <button type="submit" disabled={busy || !canUpdate || (!state.active && (!reviewDate || reviewDate === state.deletionReviewDate))}>
          {state.active ? 'Deactivate phone' : 'Update review date'}
        </button>
        {!state.active && <button type="button" disabled={busy || !canUpdate} onClick={() => void save(true)}>Reactivate phone</button>}
      </form>
    </>}
  </section>
}
