import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiGet, apiPost, apiPostRaw } from '../api/client'

type Step = {
  id: string
  stepIndex: number
  name: string
  stepStatus: number
  succeeded: boolean
  detail: string
}

type Check = {
  id: string
  code: string
  passed: boolean
  detailJson: string
  evaluatedAtUtc: string
}

type Job = {
  id: string
  terminalId: string
  firmwarePackageId: string
  firmwareArtifactId: string | null
  status: number
  simulationMode: boolean
  safetyStateJson: string
  approvedAtUtc: string | null
  approvedByOperatorId: string
  cancelReason: string
  createdAtUtc: string
  steps: Step[]
  safetyChecks: Check[]
  rollBackPlan: { id: string; backupNotes: string; recoveryStepsJson: string } | null
  hardwareValidationNotice: string
}

export function FirmwareJobDetail() {
  const { id } = useParams<{ id: string }>()
  const [job, setJob] = useState<Job | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [actionMsg, setActionMsg] = useState<string | null>(null)
  const [rollbackNotes, setRollbackNotes] = useState(
    'Verified backup image and rollback steps on lab bench before production.',
  )
  const [cancelConfirmed, setCancelConfirmed] = useState(false)

  const refresh = useCallback(async () => {
    if (!id) return
    setErr(null)
    setLoading(true)
    try {
      setJob(await apiGet<Job>(`/api/firmware/jobs/${id}`))
    } catch (e) {
      setErr(
        e instanceof Error ? e.message : 'Could not load job — check id, API availability, and migrations.',
      )
    } finally {
      setLoading(false)
    }
  }, [id])

  useEffect(() => {
    void refresh()
  }, [refresh])

  async function runSimulate() {
    if (!id) return
    setActionMsg(null)
    const r = await apiPostRaw(`/api/firmware/jobs/${id}/simulate`)
    const j = await r.json().catch(() => ({}))
    if (!r.ok) setActionMsg(`Simulate failed: ${r.status} ${JSON.stringify(j)}`)
    else setActionMsg(`Simulation completed — status ${(j as { status?: number }).status ?? '?'}`)
    await refresh()
  }

  async function runApprove() {
    if (!id) return
    setActionMsg(null)
    try {
      await apiPost(`/api/firmware/jobs/${id}/approve`, { rollbackNotes })
      setActionMsg('Approved (rollback notes recorded).')
      await refresh()
    } catch (e) {
      setActionMsg(e instanceof Error ? e.message : 'approve failed')
    }
  }

  async function runCancel() {
    if (!id) return
    setActionMsg(null)
    if (!cancelConfirmed) {
      setActionMsg('Confirm cancellation — checked operators only (audit trail).')
      return
    }
    try {
      await apiPost(`/api/firmware/jobs/${id}/cancel`, { confirm: true, reason: 'Cancelled from UI' })
      setActionMsg('Cancelled.')
      await refresh()
    } catch (e) {
      setActionMsg(e instanceof Error ? e.message : 'cancel failed')
    }
  }

  if (!id) return <p>Missing id</p>
  if (loading) return <p role="status">Loading job…</p>
  if (err || !job)
    return (
      <div>
        <p>
          <Link to="/firmware/jobs">← Jobs</Link>
        </p>
        <p role="alert" style={{ color: 'crimson' }}>
          {err ?? 'Job not found.'}
        </p>
      </div>
    )

  const checksOk = job.safetyChecks.filter((c) => !c.passed && (c.code === 'checksum_registry' || c.code === 'compatibility'))

  return (
    <div>
      <p>
        <Link to="/firmware/jobs">← Jobs</Link>
      </p>
      <h1>Firmware job</h1>
      <p style={{ background: '#fff3cd', padding: 12, border: '1px solid #ffc107', borderRadius: 4, maxWidth: 900 }}>
        {job.hardwareValidationNotice}
      </p>
      <p style={{ fontSize: 14 }}>
        <strong>Simulation mode</strong>: {job.simulationMode ? 'yes' : 'no'} · <strong>Status</strong>: {job.status}
      </p>
      <p style={{ fontFamily: 'monospace', fontSize: 12 }}>
        Terminal {job.terminalId} · Package {job.firmwarePackageId}
      </p>

      <h2 style={{ fontSize: 17 }}>Safety checklist (audit)</h2>
      <p style={{ fontSize: 13, color: '#555' }}>
        Approval requires passing <code>checksum_registry</code> and <code>compatibility</code> checks, completed simulation,
        and rollback notes (min 10 chars after simulation completes).
      </p>
      {checksOk.length > 0 && (
        <p style={{ color: 'crimson' }}>Blocking failed checks — cannot approve until resolved.</p>
      )}
      {job.safetyChecks.length === 0 ? (
        <p role="status" style={{ color: '#666' }}>
          No safety checks recorded yet.
        </p>
      ) : (
        <ul>
          {job.safetyChecks.map((c) => (
            <li key={c.id}>
              <strong>{c.code}</strong> {c.passed ? '✓' : '✗'} — {c.detailJson}{' '}
              <span style={{ color: '#888' }}>({c.evaluatedAtUtc})</span>
            </li>
          ))}
        </ul>
      )}

      <h2 style={{ fontSize: 17 }}>Simulation result (steps)</h2>
      {job.steps.length === 0 ? (
        <p role="status" style={{ color: '#666' }}>
          No steps yet — run simulation to generate the host-side checklist (DLA/XMODEM step stays HARDWARE_VALIDATION_REQUIRED).
        </p>
      ) : (
        <ol>
          {job.steps
            .slice()
            .sort((a, b) => a.stepIndex - b.stepIndex)
            .map((s) => (
              <li key={s.id}>
                <strong>{s.name}</strong> [{s.stepStatus}] {s.succeeded ? 'ok' : '—'} — {s.detail}
              </li>
            ))}
        </ol>
      )}
      {job.safetyStateJson && (
        <pre style={{ fontSize: 11, background: '#f6f6f6', padding: 8, overflow: 'auto' }}>{job.safetyStateJson}</pre>
      )}

      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', marginTop: 16 }}>
        <button type="button" onClick={() => void runSimulate()}>
          Run simulation
        </button>
        <div>
          <label style={{ display: 'block', fontSize: 13 }}>Rollback / backup notes (approval)</label>
          <textarea
            value={rollbackNotes}
            onChange={(e) => setRollbackNotes(e.target.value)}
            rows={3}
            style={{ width: 420, fontFamily: 'inherit' }}
          />
          <button type="button" onClick={() => void runApprove()} style={{ display: 'block', marginTop: 6 }}>
            Approve (after simulation)
          </button>
        </div>
        <div>
          <label style={{ display: 'flex', gap: 8, alignItems: 'center', fontSize: 13 }}>
            <input
              type="checkbox"
              checked={cancelConfirmed}
              onChange={(e) => setCancelConfirmed(e.target.checked)}
            />
            Confirm cancel (audited)
          </label>
          <button type="button" onClick={() => void runCancel()} style={{ marginTop: 6 }}>
            Cancel job
          </button>
        </div>
      </div>
      {actionMsg && <p style={{ marginTop: 12 }}>{actionMsg}</p>}

      {job.rollBackPlan && (
        <div style={{ marginTop: 24 }}>
          <h3 style={{ fontSize: 15 }}>Rollback plan</h3>
          <p>{job.rollBackPlan.backupNotes}</p>
          <pre style={{ fontSize: 11 }}>{job.rollBackPlan.recoveryStepsJson}</pre>
        </div>
      )}
    </div>
  )
}
