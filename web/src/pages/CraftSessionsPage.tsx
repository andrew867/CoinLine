import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { apiGet, apiPost } from '../api/client'

type SessionRow = {
  id: string
  terminalId: string
  technicianId: string
  operatorId: string
  fieldNotes: string
  startedAtUtc: string
  endedAtUtc: string | null
}

type TermOpt = { id: string; displayName: string }

export function CraftSessionsPage() {
  const navigate = useNavigate()
  const [rows, setRows] = useState<SessionRow[]>([])
  const [terms, setTerms] = useState<TermOpt[]>([])
  const [terminalId, setTerminalId] = useState('')
  const [technicianId, setTechnicianId] = useState('tech-field')
  const [operatorId, setOperatorId] = useState('ui@local')
  const [fieldNotes, setFieldNotes] = useState('')
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setErr(null)
    try {
      const [s, t] = await Promise.all([
        apiGet<SessionRow[]>('/api/craft/sessions'),
        apiGet<TermOpt[]>('/api/terminals'),
      ])
      setRows(s)
      setTerms(t)
      setTerminalId((prev) => prev || (t.length > 0 ? t[0].id : ''))
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'error')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  async function startSession(e: React.FormEvent) {
    e.preventDefault()
    if (!terminalId) return
    setErr(null)
    try {
      const created = await apiPost<{ id: string }>('/api/craft/sessions', {
        terminalId,
        technicianId,
        operatorId,
        fieldNotes: fieldNotes || null,
      })
      navigate(`/craft/${created.id}`)
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'error')
    }
  }

  return (
    <div>
      <h1>Craft sessions</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Technician craft workflows — commands run through host simulation unless certified for live NCC/DLOG.
      </p>

      <h2>Start session</h2>
      <form onSubmit={(e) => void startSession(e)} style={{ display: 'flex', flexWrap: 'wrap', gap: 12, alignItems: 'flex-end' }}>
        <label>
          Terminal
          <select
            value={terminalId}
            onChange={(e) => setTerminalId(e.target.value)}
            style={{ display: 'block', minWidth: 220 }}
          >
            {terms.map((t) => (
              <option key={t.id} value={t.id}>
                {t.displayName}
              </option>
            ))}
          </select>
        </label>
        <label>
          Technician id
          <input value={technicianId} onChange={(e) => setTechnicianId(e.target.value)} style={{ display: 'block' }} />
        </label>
        <label>
          Operator id
          <input value={operatorId} onChange={(e) => setOperatorId(e.target.value)} style={{ display: 'block' }} />
        </label>
        <label>
          Field notes
          <input value={fieldNotes} onChange={(e) => setFieldNotes(e.target.value)} style={{ display: 'block', minWidth: 200 }} />
        </label>
        <button type="submit" data-testid="craft-start-session" disabled={!terminalId || terms.length === 0}>
          Start craft session
        </button>
      </form>

      {!loading && terms.length === 0 && (
        <p role="status">No terminals available — create a terminal before starting a craft session.</p>
      )}

      {loading && <p role="status">Loading…</p>}
      {!loading && err && <p role="alert" style={{ color: 'crimson' }}>{err}</p>}

      <h2 style={{ marginTop: 24 }}>Sessions</h2>
      {!loading && !err && rows.length === 0 && <p role="status">No sessions yet.</p>}
      {!loading && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ borderBottom: '1px solid #ccc' }}>
              <th align="left">Started</th>
              <th align="left">Technician</th>
              <th align="left">Terminal</th>
              <th align="left">Notes</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/craft/${r.id}`}>{new Date(r.startedAtUtc).toLocaleString()}</Link>
                </td>
                <td>{r.technicianId}</td>
                <td>
                  <Link to={`/terminals/${r.terminalId}`}>{r.terminalId.slice(0, 8)}…</Link>
                </td>
                <td>{r.fieldNotes || '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
