import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ErrorBanner, LoadingBlock } from '../components/AppStates'
import { terminalStatusLabel } from '../components/StatusBadge'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Dash = {
  terminalsByStatus: { status: number; count: number }[]
  activeCraftSessions: number
  openNccSessions: number
  failedDownloads: number
  uploadAlerts: { failed: number; quarantined: number }
}

type Pending = { paymentCount: number; paymentTotal: number; callCount: number }
type SipModem = {
  listening?: boolean
  registered?: boolean
  assignedNumber?: string | null
  registrar?: string | null
  activeCalls?: number
  registrationNote?: string | null
}

type RecentAlarm = {
  id: string
  title: string
  summary: string | null
  terminalName: string | null
  capturedAtUtc: string
}

export function Dashboard() {
  const [dash, setDash] = useState<Dash | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [alarms, setAlarms] = useState<number | null>(null)
  const [recentAlarms, setRecentAlarms] = useState<RecentAlarm[]>([])
  const [sip, setSip] = useState<SipModem | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    let c = false
    fetch('/api/operator/dashboard', { headers })
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Dash>
      })
      .then((j) => {
        if (!c) setDash(j)
      })
      .catch((e) => {
        if (!c) setErr(e instanceof Error ? e.message : 'error')
      })
    fetch('/api/cards/reconciliation-pending', { headers })
      .then((r) => (r.ok ? (r.json() as Promise<Pending>) : null))
      .then((j) => {
        if (!c && j) setPending(j)
      })
      .catch(() => {})
    fetch('/api/field-events?take=5', { headers })
      .then((r) => (r.ok ? (r.json() as Promise<{ total: number; items?: RecentAlarm[] }>) : null))
      .then((j) => {
        if (!c && j) {
          setAlarms(j.total)
          setRecentAlarms(j.items ?? [])
        }
      })
      .catch(() => {})
    fetch('/api/sip-modem', { headers })
      .then((r) => (r.ok ? (r.json() as Promise<SipModem>) : null))
      .then((j) => {
        if (!c && j) setSip(j)
      })
      .catch(() => {})
    return () => {
      c = true
    }
  }, [])

  const phones = dash?.terminalsByStatus.reduce((n, x) => n + x.count, 0) ?? 0
  const money = (pending?.callCount ?? 0) + (pending?.paymentCount ?? 0)

  return (
    <div>
      <h1>Home</h1>
      <p className="page-intro">
        Host ledger is on. A phone that calls in is priced, and its tables are sent. Physical card writes stay off.
      </p>
      {sip?.listening && (
        <p className="page-intro">
          SIP audio modem is listening
          {sip.registered ? ', registered' : ''}
          {sip.assignedNumber ? ` for ${sip.assignedNumber}` : ''}.
          {typeof sip.activeCalls === 'number' ? ` Active calls: ${sip.activeCalls}.` : ''}{' '}
          <Link to="/status">Platform status</Link>
        </p>
      )}
      {err && <ErrorBanner message={err} />}
      {!dash && !err && <LoadingBlock label="Loading the fleet…" />}
      {dash && (
        <>
          <div className="stat-grid">
            <Link className="stat" to="/terminals">
              <strong>{phones}</strong>
              <span>Phones</span>
            </Link>
            <Link className="stat" to="/ncc-sessions">
              <strong>{dash.openNccSessions}</strong>
              <span>Phones on a call</span>
            </Link>
            <Link className="stat" to="/downloads?status=failed">
              <strong>{dash.failedDownloads}</strong>
              <span>Failed downloads</span>
            </Link>
            <Link className="stat" to="/field-events">
              <strong>{alarms ?? 0}</strong>
              <span>Alarms and cash</span>
            </Link>
            <Link className="stat" to="/card-reconciliation">
              <strong>{money}</strong>
              <span>Items waiting to reconcile</span>
            </Link>
          </div>
          <h2>Phones by status</h2>
          <ul>
            {dash.terminalsByStatus.map((x) => (
              <li key={x.status}>
                {terminalStatusLabel(x.status)}: <strong>{x.count}</strong>
              </li>
            ))}
          </ul>
          {recentAlarms.length > 0 && (
            <>
              <h2>Recent alarms and cash</h2>
              <ul>
                {recentAlarms.map((a) => (
                  <li key={a.id}>
                    <Link to={`/dlog/${a.id}`}>
                      {a.title}
                      {a.summary ? ` — ${a.summary}` : ''}
                    </Link>
                    {a.terminalName ? ` · ${a.terminalName}` : ''} ·{' '}
                    {new Date(a.capturedAtUtc).toLocaleString()}
                  </li>
                ))}
              </ul>
              <p className="page-intro">
                <Link to="/field-events">All alarms and cash</Link>
              </p>
            </>
          )}
          <p className="page-intro">
            Uploads needing a look: {dash.uploadAlerts.failed} failed, {dash.uploadAlerts.quarantined} quarantined.{' '}
            <Link to="/uploads">Open uploads</Link>. Technician sessions open: {dash.activeCraftSessions}.{' '}
            <Link to="/craft">Open craft</Link>.
          </p>
        </>
      )}
      {pending && (
        <p className="page-intro">
          Waiting on the books: {pending.callCount} calls and {pending.paymentCount} card payments
          {pending.paymentCount ? ` (${moneyLabel(pending.paymentTotal)})` : ''}.{' '}
          <Link to="/card-reconciliation">Reconcile</Link>
        </p>
      )}
    </div>
  )
}

function moneyLabel(amount: number): string {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(amount)
}
