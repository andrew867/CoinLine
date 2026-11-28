import type { FormEvent } from 'react'
import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPost } from '../api/client'
import { FirmwareTransitionReviewPanel } from './FirmwareTransitionReviewPanel'
import { FirmwareConfigurationTransitionPanel } from './FirmwareConfigurationTransitionPanel'

type Row = {
  id: string
  terminalId: string
  firmwarePackageId: string
  firmwareArtifactId: string | null
  status: number
  simulationMode: boolean
  approvedAtUtc: string | null
}

type TermOpt = { id: string; displayName: string }
type Pkg = { id: string; name?: string; versionLabel?: string }
type ExecutionPolicy = { allowLiveFlashing: boolean; liveDlaEnabled: boolean; liveTransportConfigured?: boolean; incomingCodeServerListening?: boolean }
type ArtifactOptions = { id: string; primaryArtifactId: string | null; artifactChecksumHex: string; artifactSizeBytes: number;
  artifacts: { id: string; kind: string; sha256Hex: string; sizeBytes: number; payloadPresent?: boolean }[] }

export function FirmwareJobsPage() {
  const [rows, setRows] = useState<Row[]>([])
  const [terms, setTerms] = useState<TermOpt[]>([])
  const [packages, setPackages] = useState<Pkg[]>([])
  const [terminalId, setTerminalId] = useState('')
  const [packageId, setPackageId] = useState('')
  const [policy, setPolicy] = useState<ExecutionPolicy | null>(null)
  const [prepareLive, setPrepareLive] = useState(false)
  const [artifactOptions, setArtifactOptions] = useState<ArtifactOptions | null>(null)
  const [artifactId, setArtifactId] = useState('')
  const [artifactError, setArtifactError] = useState('')
  const [starting, setStarting] = useState(false)
  const [jobErr, setJobErr] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setErr(null)
    setLoading(true)
    try {
      const [jobs, t, pkgs] = await Promise.all([
        apiGet<Row[]>('/api/firmware/jobs'),
        apiGet<TermOpt[]>('/api/terminals'),
        apiGet<Pkg[]>('/api/firmware/packages'),
      ])
      setRows(jobs)
      setTerms(t)
      setPackages(pkgs)
      setTerminalId((prev) => prev || t[0]?.id || '')
      setPackageId((prev) => prev || pkgs[0]?.id || '')
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Failed to load jobs — check API / network.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  useEffect(() => {
    let active = true
    void apiGet<ExecutionPolicy>('/api/firmware/execution-policy').then(value => { if (active) setPolicy(value) })
      .catch(() => { if (active) setPolicy(null) })
    return () => { active = false }
  }, [])
  const allowPreparation = policy?.allowLiveFlashing === true && policy.liveDlaEnabled === true
  const liveSelected = allowPreparation && prepareLive
  const eligibleArtifacts = artifactOptions?.id === packageId ? (artifactOptions?.artifacts ?? []).filter(artifact =>
    artifact.payloadPresent === true && artifact.sizeBytes === artifactOptions.artifactSizeBytes &&
    artifact.sha256Hex.toLowerCase() === artifactOptions.artifactChecksumHex.toLowerCase()) : []
  const selectedArtifact = eligibleArtifacts.find(artifact => artifact.id === artifactId)

  useEffect(() => {
    setArtifactOptions(null); setArtifactId(''); setArtifactError('')
    if (!liveSelected || !packageId) return
    let active = true
    void apiGet<ArtifactOptions>(`/api/firmware/packages/${packageId}`).then(value => {
      if (!active) return
      if (!value || value.id !== packageId || !Array.isArray(value.artifacts)) throw new Error('Invalid package response.')
      setArtifactOptions(value)
      const primary = value.artifacts.find(artifact => artifact.id === value.primaryArtifactId && artifact.payloadPresent === true &&
        artifact.sizeBytes === value.artifactSizeBytes && artifact.sha256Hex.toLowerCase() === value.artifactChecksumHex.toLowerCase())
      setArtifactId(primary?.id ?? '')
    }).catch(reason => { if (active) setArtifactError(reason instanceof Error ? reason.message : 'Could not load registered artifacts.') })
    return () => { active = false }
  }, [packageId, liveSelected])

  async function startJob(e: FormEvent) {
    e.preventDefault()
    if (!terminalId || !packageId || starting || (liveSelected && !selectedArtifact)) return
    setJobErr(null)
    setStarting(true)
    try {
      await apiPost<{ id: string }>(`/api/terminals/${terminalId}/firmware-jobs`, {
        firmwarePackageId: packageId,
        ...(liveSelected && selectedArtifact ? { firmwareArtifactId: selectedArtifact.id } : {}),
        simulationMode: !liveSelected,
      })
      setPrepareLive(false)
      await refresh()
    } catch (ex) {
      setJobErr(ex instanceof Error ? ex.message : 'start failed')
    } finally { setStarting(false) }
  }

  return (
    <div>
      <h1>Firmware jobs</h1>
      <p style={{ fontSize: 14, color: '#444' }}>
        Simulation-first orchestration — use job detail to run simulation and operator approval (rollback notes).
      </p>
      <FirmwareTransitionReviewPanel phones={terms} />
      <FirmwareConfigurationTransitionPanel phones={terms} />
      {!loading && !err && terms.length === 0 && (
        <p role="status" style={{ color: '#a50', maxWidth: 720 }}>
          No terminals available — create a terminal before queueing jobs.
        </p>
      )}
      {!loading && !err && packages.length === 0 && (
        <p role="status" style={{ color: '#a50', maxWidth: 720 }}>
          No firmware packages registered — check seed data or registry APIs.
        </p>
      )}
      <section
        style={{ marginBottom: 20, padding: 12, border: '1px solid #ddd', borderRadius: 6, maxWidth: 520 }}
      >
        <h2 style={{ marginTop: 0, fontSize: '1rem' }}>Prepare a firmware job</h2>
        {jobErr && <p style={{ color: 'crimson' }}>{jobErr}</p>}
        <form onSubmit={(e) => void startJob(e)} style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          <label>
            Terminal
            <select
              data-testid="firmware-job-terminal"
              value={terminalId}
              onChange={(e) => setTerminalId(e.target.value)}
              style={{ display: 'block', width: '100%' }}
            >
              {terms.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.displayName}
                </option>
              ))}
            </select>
          </label>
          <label>
            Package
            <select
              data-testid="firmware-job-package"
              value={packageId}
              onChange={(e) => setPackageId(e.target.value)}
              style={{ display: 'block', width: '100%' }}
            >
              {packages.map((p) => (
                <option key={p.id} value={p.id}>
                  {(p.name ?? p.id.slice(0, 8)) + (p.versionLabel ? ` (${p.versionLabel})` : '')}
                </option>
              ))}
            </select>
          </label>
          {allowPreparation && <label><input type="checkbox" checked={liveSelected} disabled={starting} onChange={event => setPrepareLive(event.target.checked)} /> Prepare for live execution after simulation and approval</label>}
          {liveSelected && <p>This prepares a job only. No terminal contact or code transfer is started.{policy?.incomingCodeServerListening
            ? ' The incoming code server is listening for approved scheduled callbacks.'
            : policy?.liveTransportConfigured === false ? ' Live delivery can be configured separately.' : ''}</p>}
          {liveSelected && <>
            <label>Registered artifact<select value={artifactId} onChange={event => setArtifactId(event.target.value)} disabled={starting || !eligibleArtifacts.length}>
              <option value="">Choose a stored artifact</option>{eligibleArtifacts.map(artifact => <option key={artifact.id} value={artifact.id}>{artifact.kind} · {artifact.sizeBytes} B · {artifact.sha256Hex.slice(0, 12)}</option>)}
            </select></label>
            {artifactError ? <p role="alert">{artifactError}</p> : !artifactOptions ? <p role="status">Loading artifacts…</p> : !eligibleArtifacts.length ? <p role="status">No matching stored artifact. <Link to={`/firmware/packages/${packageId}`}>Open package</Link> and upload the primary file first.</p> : <p>Manager verifies the complete artifact again before any transfer.</p>}
          </>}
          <button type="submit" data-testid="firmware-job-start" disabled={starting || !terminalId || !packageId || (liveSelected && !selectedArtifact)}>
            {liveSelected ? 'Prepare live-capable job' : 'Queue simulation job'}
          </button>
        </form>
      </section>
      {loading && <p>Loading…</p>}
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!loading && !err && rows.length === 0 && (
        <p role="status" style={{ color: '#666' }}>
          No firmware jobs yet — queue one above or rely on seed data.
        </p>
      )}
      {!loading && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse', width: '100%', maxWidth: 1100 }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>Job</th>
              <th>Terminal</th>
              <th>Package</th>
              <th>Status</th>
              <th>Sim</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/firmware/jobs/${r.id}`}>{r.id.slice(0, 8)}…</Link>
                </td>
                <td style={{ fontFamily: 'monospace', fontSize: 11 }}>{r.terminalId}</td>
                <td style={{ fontFamily: 'monospace', fontSize: 11 }}>{r.firmwarePackageId}</td>
                <td>{r.status}</td>
                <td>{r.simulationMode ? 'yes' : 'no'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
