import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiGet } from '../api/client'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' } as const

type CmdType = {
  id: string
  code: string
  displayName: string
  isDestructive: boolean
  defaultSimulationOnly: boolean
}

type SessionBlock = {
  id: string
  terminalId: string
  technicianId: string
  operatorId: string
  fieldNotes: string
  startedAtUtc: string
  endedAtUtc: string | null
}

type CmdRow = {
  id: string
  commandName: string
  status: number
  auditReason: string
  destructiveConfirmed: boolean
  simulationExecution: boolean
  requestHex: string
  responseHex: string | null
  createdAtUtc: string
  updatedAtUtc: string
  commandTypeCode: string | null
}

type AuditRow = { id: string; message: string; detailJson: string; occurredAtUtc: string }

type DiagRow = { id: string; category: string; payloadJson: string; recordedAtUtc: string }

type Detail = {
  session: SessionBlock
  commands: CmdRow[]
  craftAuditTrail: AuditRow[]
  craftDiagnostics: DiagRow[]
  hardwareValidationNotice?: string
}

/** Aligns with API command-type registry + name fallback (e.g. `ping` non-destructive). */
function inferCraftDestructive(types: CmdType[], commandName: string, commandTypeCode: string): boolean {
  const code = commandTypeCode.trim()
  if (code.length > 0) {
    const t = types.find((x) => x.code === code)
    return t?.isDestructive ?? true
  }
  const name = commandName.trim()
  const byName = types.find((x) => x.code === name)
  if (byName) return byName.isDestructive
  return name.toLowerCase() !== 'ping'
}

const statusLabel = (s: number) => {
  const m: Record<number, string> = {
    0: 'Queued',
    1: 'Sent',
    2: 'Succeeded',
    3: 'Failed',
    4: 'TimedOut',
    5: 'Running',
    6: 'Cancelled',
  }
  return m[s] ?? String(s)
}

export function CraftSessionDetail() {
  const { sessionId } = useParams()
  const [data, setData] = useState<Detail | null>(null)
  const [types, setTypes] = useState<CmdType[]>([])
  const [err, setErr] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [submitErr, setSubmitErr] = useState<string | null>(null)

  const [commandName, setCommandName] = useState('ping')
  const [requestHex, setRequestHex] = useState('00')
  const [commandTypeCode, setCommandTypeCode] = useState('')
  const [auditReason, setAuditReason] = useState('')
  const [deferSimulation, setDeferSimulation] = useState(false)
  const [pendingDestructive, setPendingDestructive] = useState(false)

  useEffect(() => {
    if (!sessionId) return
    let c = false
    setLoading(true)
    setErr(null)
    ;(async () => {
      try {
        const [d, tt] = await Promise.all([
          apiGet<Detail>(`/api/craft/sessions/${sessionId}`),
          apiGet<CmdType[]>('/api/craft/command-types'),
        ])
        if (!c) {
          setData(d)
          setTypes(tt)
          setErr(null)
        }
      } catch (e) {
        if (!c) setErr(e instanceof Error ? e.message : 'error')
      } finally {
        if (!c) setLoading(false)
      }
    })()
    return () => {
      c = true
    }
  }, [sessionId])

  async function refresh() {
    if (!sessionId) return
    setErr(null)
    try {
      setData(await apiGet<Detail>(`/api/craft/sessions/${sessionId}`))
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'error')
    }
  }

  async function postCommand(e: React.FormEvent | null, explicitDestructiveConfirm: boolean) {
    e?.preventDefault()
    if (!sessionId) return
    setSubmitErr(null)

    const destructive = inferCraftDestructive(types, commandName, commandTypeCode)

    if (destructive && auditReason.trim().length < 3) {
      setSubmitErr('Audit reason required (min 3 characters) for destructive commands.')
      return
    }
    if (destructive && !explicitDestructiveConfirm) {
      setPendingDestructive(true)
      return
    }

    try {
      const r = await fetch(`/api/craft/sessions/${sessionId}/commands`, {
        method: 'POST',
        headers: { ...headers, 'Content-Type': 'application/json' },
        body: JSON.stringify({
          commandName,
          requestHex,
          commandTypeCode: commandTypeCode || null,
          confirmDestructive: destructive ? true : null,
          auditReason: destructive ? auditReason.trim() : null,
          simulationExecution: null,
          deferSimulation,
        }),
      })
      const txt = await r.text()
      if (!r.ok) {
        setSubmitErr(txt || `${r.status}`)
        return
      }
      setPendingDestructive(false)
      await refresh()
    } catch (ex) {
      setSubmitErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function simulateCommand(cmdId: string) {
    setSubmitErr(null)
    try {
      const r = await fetch(`/api/craft/commands/${cmdId}/simulate`, { method: 'POST', headers })
      if (!r.ok) {
        setSubmitErr(await r.text())
        return
      }
      await refresh()
    } catch (ex) {
      setSubmitErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function cancelCommand(cmdId: string) {
    setSubmitErr(null)
    try {
      const r = await fetch(`/api/craft/commands/${cmdId}/cancel`, {
        method: 'POST',
        headers: { ...headers, 'Content-Type': 'application/json' },
        body: JSON.stringify({ reason: 'operator_cancel' }),
      })
      if (!r.ok) {
        setSubmitErr(await r.text())
        return
      }
      await refresh()
    } catch (ex) {
      setSubmitErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  if (!sessionId) return <p role="status">Missing session id.</p>
  if (loading && !data) return <p role="status">Loading craft session…</p>
  if (err && !data)
    return (
      <div>
        <p>
          <Link to="/craft">← Craft sessions</Link>
        </p>
        <p role="alert" style={{ color: 'crimson' }}>
          {err}
        </p>
      </div>
    )
  if (!data) return <p role="status">Loading craft session…</p>

  const { session, commands, craftAuditTrail, craftDiagnostics, hardwareValidationNotice } = data

  return (
    <div>
      <p>
        <Link to="/craft">← Craft sessions</Link>
      </p>
      <h1>Craft session</h1>
      {hardwareValidationNotice && (
        <p style={{ background: '#fff9e6', border: '1px solid #e6d69a', padding: 10, maxWidth: 720 }} role="note">
          {hardwareValidationNotice}
        </p>
      )}
      <p>
        Technician <strong>{session.technicianId}</strong> · Operator <strong>{session.operatorId}</strong>
      </p>
      <p>
        Terminal{' '}
        <Link to={`/terminals/${session.terminalId}`}>
          <code>{session.terminalId}</code>
        </Link>
      </p>
      {session.fieldNotes && <p>Notes: {session.fieldNotes}</p>}
      <p>
        Started {new Date(session.startedAtUtc).toLocaleString()}
        {session.endedAtUtc && ` · Ended ${new Date(session.endedAtUtc).toLocaleString()}`}
      </p>

      <h2>Enqueue command</h2>
      <p style={{ color: '#555' }}>
        Registry types drive destructive checks. Simulation runs on the host unless you defer for cancel/step-through.
      </p>
      {submitErr && <pre style={{ color: 'crimson', whiteSpace: 'pre-wrap' }}>{submitErr}</pre>}
      <form onSubmit={(e) => void postCommand(e, false)} style={{ display: 'flex', flexDirection: 'column', gap: 8, maxWidth: 520 }}>
        <label htmlFor="craft-command-name">
          Command name
          <input
            id="craft-command-name"
            value={commandName}
            onChange={(e) => setCommandName(e.target.value)}
            style={{ display: 'block', width: '100%' }}
          />
        </label>
        <label htmlFor="craft-request-hex">
          Request (hex)
          <input
            id="craft-request-hex"
            value={requestHex}
            onChange={(e) => setRequestHex(e.target.value)}
            style={{ display: 'block', width: '100%' }}
          />
        </label>
        <label>
          Command type (registry)
          <select
            value={commandTypeCode}
            onChange={(e) => setCommandTypeCode(e.target.value)}
            style={{ display: 'block', width: '100%' }}
          >
            <option value="">— infer from name —</option>
            {types.map((t) => (
              <option key={t.id} value={t.code}>
                {t.code} — {t.displayName}
                {t.isDestructive ? ' (destructive)' : ''}
              </option>
            ))}
          </select>
        </label>
        {inferCraftDestructive(types, commandName, commandTypeCode) && (
          <label>
            Audit reason (required for destructive commands)
            <input value={auditReason} onChange={(e) => setAuditReason(e.target.value)} style={{ display: 'block', width: '100%' }} />
          </label>
        )}
        <label>
          <input type="checkbox" checked={deferSimulation} onChange={(e) => setDeferSimulation(e.target.checked)} /> Defer
          simulation (stay Queued — simulate or cancel)
        </label>
        <button type="submit">Enqueue command</button>
      </form>

      {pendingDestructive && (
        <div
          role="dialog"
          style={{
            marginTop: 16,
            padding: 16,
            border: '2px solid #c00',
            background: '#fff8f8',
            maxWidth: 480,
          }}
        >
          <p>
            <strong>Destructive craft operation</strong> — confirm you intend to enqueue this command with simulation-only
            execution on the host.
          </p>
          <button type="button" onClick={() => void postCommand(null, true)}>
            Confirm and enqueue
          </button>{' '}
          <button type="button" onClick={() => setPendingDestructive(false)}>
            Cancel
          </button>
        </div>
      )}

      <h2 style={{ marginTop: 28 }}>Command status</h2>
      <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
        <thead>
          <tr style={{ borderBottom: '1px solid #ccc' }}>
            <th align="left">When</th>
            <th align="left">Name</th>
            <th align="left">Type</th>
            <th align="left">Status</th>
            <th align="left">Actions</th>
          </tr>
        </thead>
        <tbody>
          {commands.length === 0 && (
            <tr>
              <td colSpan={5}>
                <span role="status">No commands yet — enqueue one above.</span>
              </td>
            </tr>
          )}
          {commands.map((c) => (
            <tr key={c.id} style={{ borderBottom: '1px solid #eee' }}>
              <td>{new Date(c.createdAtUtc).toLocaleString()}</td>
              <td>
                <Link to={`/craft/commands/${c.id}`}>{c.commandName}</Link>
              </td>
              <td>{c.commandTypeCode ?? '—'}</td>
              <td>{statusLabel(c.status)}</td>
              <td>
                {c.status === 0 && (
                  <>
                    <button type="button" onClick={() => void simulateCommand(c.id)}>
                      Simulate
                    </button>{' '}
                    <button type="button" onClick={() => void cancelCommand(c.id)}>
                      Cancel
                    </button>
                  </>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <h2 style={{ marginTop: 28 }}>Technician audit timeline</h2>
      {craftAuditTrail.length === 0 && <p role="status">No audit entries for this session yet.</p>}
      <ul style={{ lineHeight: 1.6 }}>
        {craftAuditTrail.map((a) => (
          <li key={a.id}>
            <span style={{ color: '#666' }}>{new Date(a.occurredAtUtc).toLocaleString()}</span> — <code>{a.message}</code>
            <pre style={{ fontSize: 12, margin: '4px 0 12px', whiteSpace: 'pre-wrap' }}>{a.detailJson}</pre>
          </li>
        ))}
      </ul>

      <h2>Craft diagnostics (session / terminal)</h2>
      {craftDiagnostics.length === 0 && (
        <p role="status">No craft diagnostics recorded for this session or terminal.</p>
      )}
      {craftDiagnostics.length > 0 && (
        <ul>
          {craftDiagnostics.map((d) => (
            <li key={d.id}>
              {d.category} @ {new Date(d.recordedAtUtc).toLocaleString()}
              <pre style={{ fontSize: 12 }}>{d.payloadJson}</pre>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
