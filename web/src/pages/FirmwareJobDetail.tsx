import { useCallback, useEffect, useRef, useState } from 'react'
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
  codeTransitionId?: string | null
  codeScheduleId?: string | null
  status: number
  simulationMode: boolean
  liveExecutionConfigured?: boolean
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

type CodeTransition = {
  id: string
  codeScheduleId: string
  sourceControllerRom: string
  sourceControllerSoftware: string
  targetControllerRom: string
  targetControllerSoftware: string
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
  const [codeTransitions, setCodeTransitions] = useState<CodeTransition[] | null>(null)
  const [transitionError, setTransitionError] = useState(false)
  const [codeTransitionId, setCodeTransitionId] = useState('')
  const [busy, setBusy] = useState(false)
  const actionInProgress = useRef(false)
  const liveAttempted = job?.steps.some(step => step.name.startsWith('live_')) ?? false
  const canSimulate = !!job && [0, 1, 2].includes(job.status)
  const canApprove = !!job && job.status === 4 && !job.approvedAtUtc
  const eligibleForExecution = !!job && !job.simulationMode && !!job.approvedAtUtc && job.status === 4 && !liveAttempted && !job.codeTransitionId
  const canExecute = eligibleForExecution && job?.liveExecutionConfigured !== false
  const canCancel = !!job && (![3, 4, 7].includes(job.status) || (job.status === 4 && !!job.codeTransitionId && !liveAttempted))
  const canChooseSchedule = canApprove && !job?.simulationMode

  useEffect(() => {
    setCodeTransitions(null)
    setCodeTransitionId('')
    setTransitionError(false)
    if (!id || !canChooseSchedule) return
    let current = true
    void apiGet<CodeTransition[]>(`/api/firmware/jobs/${id}/code-transitions`)
      .then(rows => { if (current) setCodeTransitions(rows) })
      .catch(() => { if (current) setTransitionError(true) })
    return () => { current = false }
  }, [id, canChooseSchedule])

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

  async function performAction(action: () => Promise<void>) {
    if (actionInProgress.current) return
    actionInProgress.current = true
    setBusy(true)
    setActionMsg(null)
    try { await action() }
    catch (e) {
      setActionMsg(e instanceof Error ? e.message : 'Job action failed.')
      await refresh()
    }
    finally { actionInProgress.current = false; setBusy(false) }
  }

  async function runSimulate() {
    if (!id || !canSimulate) return
    await performAction(async () => {
      const r = await apiPostRaw(`/api/firmware/jobs/${id}/simulate`)
      const j = await r.json().catch(() => ({}))
      if (!r.ok) setActionMsg(`Simulate failed: ${r.status} ${JSON.stringify(j)}`)
      else setActionMsg(`Simulation completed; status ${(j as { status?: number }).status ?? '?'}`)
      await refresh()
    })
  }

  async function runLive() {
    if (!id || !canExecute) return
    await performAction(async () => {
      const r = await apiPostRaw(`/api/firmware/jobs/${id}/execute-live`)
      const j = await r.json().catch(() => ({}))
      if (!r.ok) setActionMsg(`Live execute failed: ${r.status} ${JSON.stringify(j)}`)
      else setActionMsg(`Live execute finished; status ${(j as { status?: number }).status ?? '?'}`)
      await refresh()
    })
  }

  async function runApprove() {
    if (!id || !canApprove) return
    await performAction(async () => {
      await apiPost(`/api/firmware/jobs/${id}/approve`, { rollbackNotes, ...(codeTransitionId ? { codeTransitionId } : {}) })
      setActionMsg('Approved (rollback notes recorded).')
      await refresh()
    })
  }

  async function runCancel() {
    if (!id || !canCancel) return
    setActionMsg(null)
    if (!cancelConfirmed) {
      setActionMsg('Confirm cancellation — checked operators only (audit trail).')
      return
    }
    await performAction(async () => {
      await apiPost(`/api/firmware/jobs/${id}/cancel`, { confirm: true, reason: 'Cancelled from UI' })
      setActionMsg('Cancelled.')
      await refresh()
    })
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
      {job.codeTransitionId && <p role="status">
        Approval bound to terminal-initiated delivery. Transition {job.codeTransitionId}, schedule {job.codeScheduleId}.
        {job.status === 4 && !liveAttempted && ' Awaiting incoming code-server integration; no outbound execution is available for this job.'}
      </p>}

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
        <button type="button" disabled={busy || !canSimulate} onClick={() => void runSimulate()}>
          Run simulation
        </button>
        {eligibleForExecution ? (
          <button type="button" disabled={busy || !canExecute} onClick={() => void runLive()}>
            Execute live (dual-gated)
          </button>
        ) : null}
        {eligibleForExecution && !canExecute && <p role="status">Live delivery is not configured. This approval is retained until a transport is available.</p>}
        {job.status === 3 ? <p role="status">Live execution is claimed or its outcome is uncertain. Review the existing attempt; do not submit another transfer.</p> :
          liveAttempted ? <p role="status">A live attempt is retained for this job. Review its result before preparing a new job.</p> : null}
        <div>
          {canChooseSchedule && <div style={{ marginBottom: 8 }}>
            <label htmlFor="job-code-transition">Delivery configuration</label>
            <select id="job-code-transition" value={codeTransitionId} onChange={e => setCodeTransitionId(e.target.value)}
              disabled={busy || !codeTransitions} style={{ display: 'block', maxWidth: '100%' }}>
              <option value="">Manual / outbound approval (not a scheduled callback)</option>
              {codeTransitions?.map(plan => <option key={plan.id} value={plan.id}>
                {plan.sourceControllerRom} {plan.sourceControllerSoftware} to {plan.targetControllerRom} {plan.targetControllerSoftware}
              </option>)}
            </select>
            {transitionError && <small role="alert">Could not load matching staged transitions.</small>}
            {codeTransitions?.length === 0 && <small>No unlinked staged transition matches this package's target version and terminal.</small>}
          </div>}
          <label style={{ display: 'block', fontSize: 13 }}>Rollback / backup notes (approval)</label>
          <textarea
            value={rollbackNotes}
            onChange={(e) => setRollbackNotes(e.target.value)}
            disabled={busy || !canApprove}
            rows={3}
            style={{ width: 420, fontFamily: 'inherit' }}
          />
          <button type="button" disabled={busy || !canApprove} onClick={() => void runApprove()} style={{ display: 'block', marginTop: 6 }}>
            Approve (after simulation)
          </button>
        </div>
        <div>
          <label style={{ display: 'flex', gap: 8, alignItems: 'center', fontSize: 13 }}>
            <input
              type="checkbox"
              checked={cancelConfirmed}
              disabled={busy || !canCancel}
              onChange={(e) => setCancelConfirmed(e.target.checked)}
            />
            Confirm cancel (audited)
          </label>
          <button type="button" disabled={busy || !canCancel || !cancelConfirmed} onClick={() => void runCancel()} style={{ marginTop: 6 }}>
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
