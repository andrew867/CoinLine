import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiGet, apiPost } from '../api/client'

type VerSummary = { id: string; versionNumber: number; status: number; publishedAtUtc: string | null }
type Detail = {
  id: string
  name: string
  mode: number
  customerId: string | null
  publishedVersionId: string | null
  versions: VerSummary[]
}

type RuleRow = {
  id: string
  priority: number
  matchKind: number
  pattern: string
  outcome: number
  ratePerMinuteUsd: number
  expression: string
}

type VerDetail = {
  id: string
  versionNumber: number
  status: number
  rules: RuleRow[]
}

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

export function RatePlanDetail() {
  const { id } = useParams()
  const [d, setD] = useState<Detail | null>(null)
  const [planLoading, setPlanLoading] = useState(true)
  const [selVer, setSelVer] = useState<string | null>(null)
  const [verDetail, setVerDetail] = useState<VerDetail | null>(null)
  const [versionLoading, setVersionLoading] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  const [np, setNp] = useState(100)
  const [pat, setPat] = useState('888')
  const [out, setOut] = useState(0)
  const [rpm, setRpm] = useState(0.05)

  const load = () => {
    if (!id) return
    setPlanLoading(true)
    setErr(null)
    apiGet<Detail>(`/api/rate-plans/${id}`)
      .then(setD)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setPlanLoading(false))
  }

  useEffect(() => {
    load()
  }, [id])

  useEffect(() => {
    if (!id || !selVer) {
      setVerDetail(null)
      return
    }
    setVersionLoading(true)
    fetch(`/api/rate-plans/${id}/versions/${selVer}`, { headers })
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<VerDetail>
      })
      .then(setVerDetail)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setVersionLoading(false))
  }, [id, selVer])

  async function addVersion(e: FormEvent) {
    e.preventDefault()
    if (!id) return
    setErr(null)
    try {
      await apiPost<{ id: string }>(`/api/rate-plans/${id}/versions`, {})
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function publish(versionId: string) {
    if (!id) return
    if (!window.confirm('Publish this version? It becomes the active snapshot for quotes.')) return
    setErr(null)
    try {
      await apiPost(`/api/rate-plans/${id}/publish`, { ratePlanVersionId: versionId, confirm: true })
      load()
      setSelVer(versionId)
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function addRule(e: FormEvent) {
    e.preventDefault()
    if (!id || !selVer) return
    setErr(null)
    try {
      await apiPost(`/api/rate-plans/${id}/versions/${selVer}/rules`, {
        priority: np,
        matchKind: 0,
        pattern: pat,
        outcome: out,
        ratePerMinuteUsd: rpm,
        expression: '{}',
      })
      const r = await fetch(`/api/rate-plans/${id}/versions/${selVer}`, { headers })
      setVerDetail(await r.json())
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  if (!id) return null

  return (
    <div>
      <p>
        <Link to="/rate-plans">← Rate plans</Link>
      </p>
      {planLoading && <p role="status">Loading plan…</p>}
      {!planLoading && err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!planLoading && !d && !err && <p role="status">Plan not found.</p>}
      {d && (
        <>
          <h1>{d.name}</h1>
          <p style={{ color: '#a60', maxWidth: 720 }}>
            Editing rules is allowed only on <strong>draft</strong> versions. Publish to apply quotes. This UI does not claim
            firmware parity.
          </p>
          <p>
            Mode: {d.mode} · Published: {d.publishedVersionId ?? '—'}
          </p>
          <form onSubmit={addVersion}>
            <button type="submit">New draft version</button>
          </form>
        </>
      )}
      <h2>Versions</h2>
      {d && d.versions.length === 0 && <p role="status">No versions yet — create a draft.</p>}
      <ul>
        {d?.versions.map((v) => (
          <li key={v.id}>
            <button type="button" onClick={() => setSelVer(v.id)}>
              v{v.versionNumber}
            </button>{' '}
            status={v.status} {v.publishedAtUtc && ` published ${v.publishedAtUtc}`}
            {v.status === 0 && (
              <button type="button" onClick={() => publish(v.id)}>
                Publish
              </button>
            )}
          </li>
        ))}
      </ul>
      {selVer && versionLoading && <p role="status">Loading version rules…</p>}
      {selVer && verDetail && (
        <div>
          <h3>Rules (version {verDetail.versionNumber})</h3>
          {verDetail.status !== 0 && <p style={{ color: '#666' }}>Published — rules are read-only.</p>}
          <table cellPadding={6} style={{ borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th align="left">Pri</th>
                <th align="left">Pattern</th>
                <th align="left">Outcome</th>
                <th align="left">$/min</th>
              </tr>
            </thead>
            <tbody>
              {verDetail.rules.length === 0 ? (
                <tr>
                  <td colSpan={4}>
                    <em>No rules — add one below (draft only).</em>
                  </td>
                </tr>
              ) : (
                verDetail.rules.map((r) => (
                  <tr key={r.id}>
                    <td>{r.priority}</td>
                    <td>{r.pattern}</td>
                    <td>{r.outcome}</td>
                    <td>{r.ratePerMinuteUsd}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
          {verDetail.status === 0 && (
            <form onSubmit={addRule} style={{ marginTop: 12 }}>
              <label>
                Priority{' '}
                <input type="number" value={np} onChange={(e) => setNp(+e.target.value)} />
              </label>{' '}
              <label>
                Pattern{' '}
                <input value={pat} onChange={(e) => setPat(e.target.value)} />
              </label>{' '}
              <label>
                Outcome (0=rated,1=block,2=free,3=emergency){' '}
                <input type="number" value={out} onChange={(e) => setOut(+e.target.value)} />
              </label>{' '}
              <label>
                $/min{' '}
                <input type="number" step="0.01" value={rpm} onChange={(e) => setRpm(+e.target.value)} />
              </label>{' '}
              <button type="submit">Add rule</button>
            </form>
          )}
        </div>
      )}
    </div>
  )
}
