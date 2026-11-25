import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { apiFetch } from '../api/client'
import './FieldEventsPage.css'

const categories = {
  alarm: 'Alarms',
  maintenance: 'Maintenance',
  status: 'Phone status',
  attention: 'Requests',
  cash: 'Cash',
} as const
type Category = keyof typeof categories

type FieldEvent = {
  id: string
  terminalId: string | null
  terminalName: string | null
  title: string
  summary: string | null
  category: Category
  capturedAtUtc: string
}

type Terminal = {
  id: string
  displayName: string
  groupName: string | null
  onCall: boolean
  lastSessionAtUtc: string | null
}

type Snapshot = { items: FieldEvent[]; terminals: Terminal[]; updatedAt: Date }

const reportTime = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' })
const refreshTime = new Intl.DateTimeFormat(undefined, { timeStyle: 'short' })

async function readJson<T>(url: string, signal: AbortSignal): Promise<T> {
  const response = await apiFetch(url, { signal })
  if (!response.ok) throw new Error(`HTTP ${response.status}`)
  return response.json() as Promise<T>
}

export function FieldEventsPage() {
  const [params, setParams] = useSearchParams()
  const terminalId = params.get('terminalId') ?? ''
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null)
  const [category, setCategory] = useState<Category | 'all'>('all')
  const [query, setQuery] = useState('')
  const [error, setError] = useState(false)
  const [refresh, setRefresh] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    const url = terminalId ? `/api/field-events?terminalId=${encodeURIComponent(terminalId)}` : '/api/field-events'
    let loading = false

    async function load() {
      if (loading) return
      loading = true
      try {
        const [events, terminals] = await Promise.all([
          readJson<{ items: FieldEvent[] }>(url, controller.signal),
          readJson<Terminal[]>('/api/terminals', controller.signal),
        ])
        if (!controller.signal.aborted) {
          setSnapshot({ items: events.items, terminals, updatedAt: new Date() })
          setError(false)
        }
      } catch {
        if (!controller.signal.aborted) setError(true)
      } finally {
        loading = false
      }
    }

    void load()
    const interval = window.setInterval(() => void load(), 20_000)
    return () => {
      controller.abort()
      window.clearInterval(interval)
    }
  }, [terminalId, refresh])

  const terminal = snapshot?.terminals.find((phone) => phone.id === terminalId)
  const active = snapshot?.terminals.filter((phone) => phone.onCall).length
  const items = snapshot?.items ?? []
  const needle = query.trim().toLowerCase()
  const visible = items.filter((event) =>
    (category === 'all' || event.category === category) &&
    (!needle || `${event.summary ?? ''} ${event.title} ${event.terminalName ?? ''}`.toLowerCase().includes(needle)),
  )

  return (
    <div className="terminal-status-view">
      <header className="terminal-status-hero">
        <div>
          <h1>Terminal status</h1>
          <p>Alarms, maintenance and reports from your phones.</p>
        </div>
        <button type="button" onClick={() => setRefresh((count) => count + 1)}>Refresh</button>
      </header>

      {snapshot && (
        <div className="terminal-status-metrics" aria-label="Fleet overview">
          <Link to="/terminals"><span>Registered phones</span><strong>{snapshot.terminals.length}</strong></Link>
          <div><span>Active NCC links</span><strong>{active}</strong></div>
          <div><span>{terminal ? 'Reports for this phone' : 'Recent reports'}</span><strong>{items.length}</strong></div>
        </div>
      )}

      <section className="terminal-status-panel" aria-labelledby="phone-activity-heading">
        <div className="terminal-status-panel-heading">
          <h2 id="phone-activity-heading">Phone activity</h2>
          {snapshot && <span>Refreshed {refreshTime.format(snapshot.updatedAt)}</span>}
        </div>
        <div className="terminal-status-tools">
          <label>
            Phone
            <select
              value={terminalId}
              onChange={(event) => {
                setSnapshot(null)
                setError(false)
                setParams(event.target.value ? { terminalId: event.target.value } : {})
              }}
            >
              <option value="">All phones</option>
              {terminalId && !terminal && <option value={terminalId}>Selected phone</option>}
              {snapshot?.terminals.map((phone) => (
                <option key={phone.id} value={phone.id}>{phone.displayName}</option>
              ))}
            </select>
          </label>
          <label>
            Report type
            <select value={category} onChange={(event) => setCategory(event.target.value as Category | 'all')}>
              <option value="all">All reports</option>
              {Object.entries(categories).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
            </select>
          </label>
          <label>
            Search reports
            <input type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Name or code" />
          </label>
        </div>

        {terminal && (
          <div className="terminal-status-phone">
            <div>
              <Link to={`/terminals/${terminal.id}`}>{terminal.displayName}</Link>
              {terminal.groupName && <span>{terminal.groupName}</span>}
            </div>
            <p>
              {terminal.onCall ? 'NCC link active' : 'No active NCC link'}
              {terminal.lastSessionAtUtc && <> · Last contact {reportTime.format(new Date(terminal.lastSessionAtUtc))}</>}
            </p>
          </div>
        )}

        {error && <p className="terminal-status-state" role="status">Could not refresh phone activity. Try Refresh.</p>}
        {!snapshot && !error && <p className="terminal-status-state" role="status">Loading phone reports…</p>}
        {snapshot && visible.length === 0 && (
          <div className="terminal-status-state">
            <strong>{items.length ? 'No matching reports' : 'No reports received'}</strong>
            <p>{items.length ? 'Try another report type or search term.' : 'New reports will appear when a phone contacts CoinLine.'}</p>
          </div>
        )}
        {visible.length > 0 && (
          <ol className="terminal-status-events" aria-label="Recent phone reports">
            {visible.map((event) => (
              <li key={event.id}>
                <span className="terminal-status-marker" data-category={event.category} aria-hidden="true" />
                <div className="terminal-status-event-content">
                  <span className="terminal-status-event-type">{event.title}</span>
                  <Link to={`/dlog/${event.id}`}>{event.summary ?? 'Report details unavailable'}</Link>
                  {!terminalId && (event.terminalId
                    ? <Link className="terminal-status-source" to={`?terminalId=${encodeURIComponent(event.terminalId)}`}>{event.terminalName ?? 'Registered phone'}</Link>
                    : <span className="terminal-status-source">Unmatched phone</span>)}
                </div>
                <time dateTime={event.capturedAtUtc}>{reportTime.format(new Date(event.capturedAtUtc))}</time>
              </li>
            ))}
          </ol>
        )}
      </section>
    </div>
  )
}
