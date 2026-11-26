import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api/client'

type Flag = { name: string; asserted: boolean }
type CurrentStatus = {
  id: string; displayName: string; terminalIdHex: string; deactivatedAtUtc: string | null
  latestReportAtUtc: string | null; statusReportId: string | null; statusReportedAtUtc: string | null
  newerUnreadableReport: boolean; serialNumber: string | null; groups: { label: string; flags: Flag[] }[] | null
  hasStatusBits: boolean | null; unmappedSetBitCount: number | null
}
const utc = (value: string) => new Date(value).toISOString().replace('T', ' ').replace(/\.\d{3}Z$/, ' UTC')

export function PhoneStatusPanel({ terminalId, refresh, showReportLink }: { terminalId: string; refresh: number; showReportLink: boolean }) {
  const [status, setStatus] = useState<CurrentStatus | null>(null)
  const [error, setError] = useState('')
  useEffect(() => {
    const controller = new AbortController()
    let loading = false
    setStatus(null)
    setError('')
    async function load() {
      if (loading) return
      loading = true
      try {
        const response = await apiFetch(`/api/field-events/phones/${encodeURIComponent(terminalId)}/current-status`, { signal: controller.signal })
        if (!response.ok) throw new Error(`Could not read the latest phone status (HTTP ${response.status}).`)
        const value = await response.json() as CurrentStatus
        if (!controller.signal.aborted) { setStatus(value); setError('') }
      } catch (reason) {
        if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Could not read the latest phone status.')
      } finally { loading = false }
    }
    void load()
    const interval = window.setInterval(() => void load(), 20_000)
    return () => { controller.abort(); window.clearInterval(interval) }
  }, [terminalId, refresh])

  const asserted = status?.groups?.flatMap(group => group.flags).filter(flag => flag.asserted).length ?? 0
  return <section className="current-phone-status" aria-labelledby="current-phone-status-heading">
    <h3 id="current-phone-status-heading">Latest reported phone status</h3>
    <p>MT10 is a snapshot sent by the phone, not a live hardware check. Times below are Manager receipt times in UTC.</p>
    {error && <p role="alert">{error}{status && ' Showing the previous refresh.'}</p>}
    {!status && !error && <p role="status">Loading latest phone status…</p>}
    {status && <>
      <dl className="current-phone-status-header">
        <div><dt>Phone ID</dt><dd>{status.terminalIdHex}</dd></div>
        <div><dt>Reported serial number</dt><dd>{status.serialNumber ?? 'Not available'}</dd></div>
        <div><dt>Last valid status report</dt><dd>{status.statusReportedAtUtc ? utc(status.statusReportedAtUtc) : 'Not received'}</dd></div>
        <div><dt>Manager inventory</dt><dd>{status.deactivatedAtUtc ? 'Deactivated' : 'Active'}</dd></div>
      </dl>
      {status.newerUnreadableReport && <p className="current-phone-warning" role="note">
        {status.latestReportAtUtc && `${utc(status.latestReportAtUtc)}: `}A more recent MT10 could not be interpreted. {status.groups ? 'The named flags are from the previous valid report.' : 'No valid status snapshot is available.'}
      </p>}
      {!status.groups && !status.newerUnreadableReport && <p>Awaiting a valid status report from a matched NCC session.</p>}
      {status.groups && <>
        <div className="current-phone-summary">
          <strong>{asserted ? `${asserted} named ${asserted === 1 ? 'condition' : 'conditions'} reported` : 'No named conditions reported'}</strong>
          {!!status.unmappedSetBitCount && <span className="current-phone-warning">{status.unmappedSetBitCount} unmapped status {status.unmappedSetBitCount === 1 ? 'bit' : 'bits'} set. Their meaning is not verified.</span>}
          {showReportLink && status.statusReportId && <Link to={`/dlog/${status.statusReportId}`}>Open source status report</Link>}
        </div>
        <div className="current-phone-status-groups">
          {status.groups.map(group => <section key={group.label} aria-label={group.label}>
            <h4>{group.label}</h4>
            <ul>{group.flags.map(flag => <li key={flag.name} data-asserted={flag.asserted}>
              <span>{flag.name}</span><span>{flag.asserted ? 'Reported' : 'Not indicated'}</span>
            </li>)}</ul>
          </section>)}
        </div>
      </>}
    </>}
  </section>
}
