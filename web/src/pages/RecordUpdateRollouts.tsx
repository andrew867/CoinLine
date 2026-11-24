import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPostRaw } from '../api/client'

type Rollout = {
  id: string
  revision: number
  status: 'Paused' | 'Completed' | 'Stopped'
  total: number
  processed: number
  changedCount: number
  skippedCount: number
  blockedPhone: { id: string; name: string; wireId: string } | null
  failureReason: string | null
}

export function RecordUpdateRollouts({ setId, refresh }: { setId: string; refresh: number }) {
  const endpoint = `/api/tables/sets/${setId}/record-updates/runs`
  const [runs, setRuns] = useState<Rollout[]>([])
  const [reload, setReload] = useState(0)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    let current = true
    void apiGet<Rollout[]>(endpoint).then(result => {
      if (current) {
        setRuns(result)
        setError('')
      }
    }).catch(() => { if (current) setError('Could not load paused record rollouts.') })
    return () => { current = false }
  }, [endpoint, refresh, reload])

  async function control(run: Rollout, decision: 'retry' | 'skip' | 'stop') {
    setBusy(true)
    setNotice('')
    setError('')
    try {
      const response = await apiPostRaw(`${endpoint}/${run.id}`, { revision: run.revision, decision })
      const result = await response.json().catch(() => null)
      if (!response.ok) {
        if (response.status === 409) setReload(value => value + 1)
        throw new Error(result?.error ?? 'Could not continue the rollout.')
      }
      const updated = result.run as Rollout
      setRuns(current => updated.status === 'Paused'
        ? current.map(item => item.id === updated.id ? updated : item)
        : current.filter(item => item.id !== updated.id))
      setReload(value => value + 1)
      setNotice(`Rollout ${updated.status.toLowerCase()}. ${updated.changedCount} updated, ${updated.skippedCount} skipped.`)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not continue the rollout.')
    } finally {
      setBusy(false)
    }
  }

  if (runs.length === 0 && !notice && !error) return null
  return (
    <section className="table-config-preview" aria-labelledby="record-update-rollouts">
      <h3 id="record-update-rollouts">Record rollouts</h3>
      {error && <p role="alert">{error}</p>}
      {notice && <p role="status">{notice}</p>}
      {runs.map(run => run.blockedPhone && <div key={run.id} className="table-config-preview">
        <p>{run.processed} of {run.total} phones processed. {run.changedCount} updated, {run.skippedCount} skipped.</p>
        <p>Paused at <Link to={`/terminals/${run.blockedPhone.id}`}>{run.blockedPhone.name}</Link> ({run.blockedPhone.wireId}).</p>
        {run.failureReason && <p>{run.failureReason}</p>}
        <div className="row-actions">
          <button type="button" disabled={busy} onClick={() => void control(run, 'retry')}>Retry phone</button>
          <button type="button" disabled={busy} onClick={() => void control(run, 'skip')}>Skip phone</button>
          <button type="button" disabled={busy} onClick={() => void control(run, 'stop')}>Stop rollout</button>
        </div>
      </div>)}
    </section>
  )
}
