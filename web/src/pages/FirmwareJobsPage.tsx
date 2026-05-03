import type { FormEvent } from 'react'
import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPost } from '../api/client'

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

export function FirmwareJobsPage() {
  const [rows, setRows] = useState<Row[]>([])
  const [terms, setTerms] = useState<TermOpt[]>([])
  const [packages, setPackages] = useState<Pkg[]>([])
  const [terminalId, setTerminalId] = useState('')
  const [packageId, setPackageId] = useState('')
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

  async function startJob(e: FormEvent) {
    e.preventDefault()
    if (!terminalId || !packageId) return
    setJobErr(null)
    try {
      await apiPost<{ id: string }>(`/api/terminals/${terminalId}/firmware-jobs`, {
        firmwarePackageId: packageId,
        simulationMode: true,
      })
      await refresh()
    } catch (ex) {
      setJobErr(ex instanceof Error ? ex.message : 'start failed')
    }
  }

  return (
    <div>
      <h1>Firmware jobs</h1>
      <p style={{ fontSize: 14, color: '#444' }}>
        Simulation-first orchestration — use job detail to run simulation and operator approval (rollback notes).
      </p>
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
        <h2 style={{ marginTop: 0, fontSize: '1rem' }}>Start simulated job</h2>
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
          <button type="submit" data-testid="firmware-job-start">
            Queue simulation job
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
