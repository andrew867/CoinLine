import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { apiFetch, apiGet, apiPostRaw } from '../api/client'
import { TableDownloadScheduleList } from './TableDownloadScheduleList'
import { DownloadPreparationPanel } from './DownloadPreparationPanel'
import { canPerform, permissionOperation as op } from '../api/consoleSession'


type Batch = {
  id: string
  terminalId: string
  tableSetId: string
  status: number
  scope: number
  retryCount: number
  completedAtUtc: string | null
  lastError: string | null
}

type Term = { id: string; displayName: string }
type SetRow = { id: string; name: string; status: number }
type RecordChoice = { tableDefinitionId: string; tableNumber: number; definitionName: string; payloadLengthBytes: number; validationPassed: boolean }

export function DownloadsPage() {
  const [search] = useSearchParams()
  const preTerm = search.get('terminal') ?? ''
  const failedOnly = search.get('status') === 'failed'
  const [rows, setRows] = useState<Batch[] | null>(null)
  const [terms, setTerms] = useState<Term[] | null>(null)
  const [sets, setSets] = useState<SetRow[] | null>(null)
  const [terminalId, setTerminalId] = useState(preTerm)
  const [tableSetId, setTableSetId] = useState('')
  const [records, setRecords] = useState<RecordChoice[] | null>(null)
  const [scope, setScope] = useState<'Full' | 'Partial'>('Full')
  const [selected, setSelected] = useState<string[]>([])
  const [queuedId, setQueuedId] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [recordsError, setRecordsError] = useState('')
  const [err, setErr] = useState<string | null>(null)
  const [preparationOpen, setPreparationOpen] = useState(false)
  const [scheduleRefresh, setScheduleRefresh] = useState(0)
  const canPrepare = canPerform('downloads', op.add)
  const canRead = canPerform('downloads', op.read)
  const canCreateSingle = canPrepare && canPerform('phones', op.read) && canPerform('table-sets', op.read)

  const load = () => {
    if (!canRead) return
    setErr(null)
    apiFetch('/api/downloads')
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Batch[]>
      })
      .then(setRows)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
  }

  useEffect(() => {
    load()
  }, [])

  useEffect(() => {
    if (!canCreateSingle) return
    let c = false
    async function safeJson<T>(path: string): Promise<T | null> {
      try {
        const r = await apiFetch(path)
        if (!r.ok) return null
        const ct = r.headers.get('content-type')
        if (!ct?.includes('application/json')) return null
        return (await r.json()) as T
      } catch {
        return null
      }
    }
    void safeJson<Term[]>('/api/terminals').then((t) => {
      if (!c && t) setTerms(t)
    })
    void safeJson<SetRow[]>('/api/tables/sets').then((s) => {
      if (!c && s) {
        setSets(s)
        const pub = s.find((x) => x.status === 1)
        if (pub && !tableSetId) setTableSetId(pub.id)
      }
    })
    return () => {
      c = true
    }
  }, [canCreateSingle])

  useEffect(() => {
    if (preTerm) setTerminalId(preTerm)
  }, [preTerm])

  useEffect(() => {
    if (!canCreateSingle || !tableSetId) return
    let current = true
    void apiGet<{ versions: RecordChoice[] }>(`/api/tables/sets/${tableSetId}`)
      .then(set => { if (current) setRecords(set.versions) })
      .catch(() => { if (current) setRecordsError('Could not load the records in this set.') })
    return () => { current = false }
  }, [tableSetId, canCreateSingle])

  const visibleRows = (rows ?? []).filter((b) => (failedOnly ? b.status === 3 : true))

  async function startDownload(e: React.FormEvent) {
    e.preventDefault()
    setErr(null)
    setQueuedId(null)
    setSaving(true)
    try {
      const response = await apiPostRaw(`/api/terminals/${terminalId}/downloads`, {
        tableSetId,
        scope,
        ...(scope === 'Partial' ? { partialTableDefinitionIds: selected } : {}),
      })
      const result = await response.json().catch(() => null)
      if (!response.ok) throw new Error(result?.error ?? `Could not queue the download (HTTP ${response.status}).`)
      setQueuedId(result.id)
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    } finally {
      setSaving(false)
    }
  }

  const choices = records?.filter(record => record.validationPassed && record.payloadLengthBytes > 2 && record.tableNumber !== 94) ?? []
  const includesFirmware = records?.some(record => record.tableNumber === 94) ?? false

  const statusLabel = (n: number) =>
    (
      {
        0: 'Pending',
        1: 'Running',
        2: 'Completed',
        3: 'Failed',
        4: 'Cancelled',
        5: 'Preparing',
      } as Record<number, string>
    )[n] ?? String(n)

  return (
    <div>
      <h1>Download batches</h1>
      <p style={{ color: '#555' }}>
        Create a batch for one phone’s next call-in. To set a time or target a group, schedule delivery from a published
        set under <Link to="/table-sets">Table sets</Link>.
      </p>
      {err && <p role="alert" style={{ color: 'crimson' }}>{err}</p>}
      {canPrepare && <div className="row-actions"><button type="button" aria-expanded={preparationOpen} onClick={() => setPreparationOpen(current => !current)}>
        {preparationOpen ? 'Close phone-list preparation' : 'Prepare downloads for multiple phones'}
      </button></div>}
      {canPrepare && preparationOpen && <DownloadPreparationPanel showBatchLinks={canRead} onPrepared={() => { load(); setScheduleRefresh(current => current + 1) }} />}
      {canRead && <TableDownloadScheduleList terminalId={preTerm || undefined} refresh={scheduleRefresh} />}
      {canCreateSingle && <>
      {terms && terms.length === 0 && (
        <p style={{ color: '#666' }}>
          No terminals yet — create one under <Link to="/terminals">Terminals</Link>.
        </p>
      )}
      {sets && sets.filter((s) => s.status === 1).length === 0 && (
        <p style={{ color: '#a50' }}>
          No published table sets.{' '}
          <Link to="/table-sets">
            <button type="button">Open table sets</button>
          </Link>
        </p>
      )}

      <h2>Start download</h2>
      <form onSubmit={startDownload} style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'flex-end', marginBottom: 24 }}>
        <label>
          Terminal
          <select
            required
            value={terminalId}
            onChange={(e) => setTerminalId(e.target.value)}
            style={{ display: 'block', minWidth: 200 }}
          >
            <option value="">—</option>
            {(terms ?? []).map((t) => (
              <option key={t.id} value={t.id}>
                {t.displayName}
              </option>
            ))}
          </select>
        </label>
        <label>
          Published table set
          <select
            required
            value={tableSetId}
            onChange={(e) => {
              setTableSetId(e.target.value)
              setRecords(null)
              setRecordsError('')
              setSelected([])
            }}
            style={{ display: 'block', minWidth: 200 }}
          >
            {(sets ?? [])
              .filter((s) => s.status === 1)
              .map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
          </select>
        </label>
        <label>
          Records
          <select value={scope} onChange={event => setScope(event.target.value as 'Full' | 'Partial')}>
            <option value="Full">Entire set</option>
            <option value="Partial">Selected records</option>
          </select>
        </label>
        {scope === 'Partial' && records && (
          <fieldset className="scope-record-choices" disabled={saving}>
            <legend>Records to download</legend>
            {choices.map(record => (
              <label className="native-table-toggle" key={record.tableDefinitionId}>
                <input type="checkbox" checked={selected.includes(record.tableDefinitionId)} onChange={event => {
                  setSelected(current => event.target.checked ? [...current, record.tableDefinitionId]
                    : current.filter(id => id !== record.tableDefinitionId))
                }} />
                MT{record.tableNumber} · {record.definitionName}
              </label>
            ))}
            {choices.length > 0 ? <p>Required records are included automatically.</p> : <p>No complete data records are available in this set.</p>}
          </fieldset>
        )}
        {includesFirmware && scope === 'Full' && <p>Use the firmware scheduler in <Link to={`/table-sets/${tableSetId}`}>this set</Link> for MT94.</p>}
        {recordsError && <p role="alert">{recordsError}</p>}
        <button type="submit" disabled={saving || !records || !terminalId || (scope === 'Full' ? includesFirmware : !selected.length)}>
          Create batch
        </button>
      </form>
      {queuedId && <p role="status"><Link to={`/downloads/${queuedId}`}>View queued batch</Link>. The phone picks it up on its next eligible call.</p>}

      </>}
      {canPrepare && !canCreateSingle && <p>Preparing a different published set for one phone requires phone and table set read access. You can prepare the existing assignments with the phone-list workflow above.</p>}
      {canRead && <>
      <h2>Recent batches</h2>
      <p className="page-intro">
        {failedOnly ? (
          <>
            Showing failed batches only.{' '}
            <Link to="/downloads">Show all</Link>
          </>
        ) : (
          <Link to="/downloads?status=failed">Show failed only</Link>
        )}
      </p>
      {!rows && <p>Loading…</p>}
      {rows && visibleRows.length === 0 && (
        <p style={{ color: '#666' }}>{failedOnly ? 'No failed download batches.' : 'No download batches yet.'}</p>
      )}
      {rows && visibleRows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>Id</th>
              <th>Status</th>
              <th>Scope</th>
              <th>Retries</th>
              <th>Last error</th>
              <th>Terminal</th>
            </tr>
          </thead>
          <tbody>
            {visibleRows.map((b) => (
              <tr key={b.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/downloads/${b.id}`}>{b.id.slice(0, 8)}…</Link>
                </td>
                <td>{statusLabel(b.status)}</td>
                <td>{b.scope === 1 ? 'Partial' : 'Full'}</td>
                <td>{b.retryCount}</td>
                <td style={{ maxWidth: 280, overflow: 'hidden', textOverflow: 'ellipsis' }}>
                  {b.lastError ?? '—'}
                </td>
                <td>
                  <code>{b.terminalId.slice(0, 8)}…</code>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      </>}
    </div>
  )
}
