import { useEffect, useState } from 'react'
import { EmptyHint, ErrorBanner, LoadingBlock } from '../components/AppStates'

type HealthJson = { status?: string; ts?: string }

export function StatusPage() {
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [health, setHealth] = useState<HealthJson | null>(null)
  const [live, setLive] = useState<HealthJson | null>(null)
  const [readyStatus, setReadyStatus] = useState<number | null>(null)
  const [readyBody, setReadyBody] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setErr(null)

    async function load() {
      try {
        const h = await fetch('/health')
        const l = await fetch('/health/live')
        const rd = await fetch('/ready')
        if (cancelled) return
        const hb = (await h.json()) as HealthJson
        const lb = (await l.json()) as HealthJson
        const rt = await rd.text()
        setHealth(hb)
        setLive(lb)
        setReadyStatus(rd.status)
        setReadyBody(rt.slice(0, 4000))
        if (!h.ok || !l.ok) throw new Error(`health ${h.status} / live ${l.status}`)
      } catch (e) {
        if (!cancelled) setErr(e instanceof Error ? e.message : 'error')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    void load()
    return () => {
      cancelled = true
    }
  }, [])

  return (
    <div>
      <h1>Platform status</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Read-only health endpoints for field troubleshooting. Correlate with API logs using{' '}
        <code>X-Correlation-Id</code>.
      </p>
      {loading && <LoadingBlock label="Fetching health and readiness…" />}
      {err && <ErrorBanner message={err} />}
      {!loading && !err && readyStatus != null && readyStatus >= 400 && (
        <p role="alert" style={{ color: '#a40', maxWidth: 720 }}>
          Readiness returned HTTP {readyStatus}. Database, payload storage, or worker heartbeat may be unhealthy — see
          detail below and operator runbooks.
        </p>
      )}
      {!loading && !err && (
        <div style={{ display: 'grid', gap: '1rem', maxWidth: 720 }}>
          <section>
            <h2>Liveness</h2>
            {health && live ? (
              <pre style={{ background: '#f6f8fa', padding: '0.75rem', overflow: 'auto' }}>
                {JSON.stringify({ health, live }, null, 2)}
              </pre>
            ) : (
              <EmptyHint>No liveness payload (unexpected).</EmptyHint>
            )}
          </section>
          <section>
            <h2>
              Readiness {readyStatus != null ? `(${readyStatus})` : ''}{' '}
              {readyStatus != null && readyStatus >= 400 ? (
                <span style={{ color: '#a40' }}>degraded</span>
              ) : null}
            </h2>
            {readyBody ? (
              <pre style={{ background: '#f6f8fa', padding: '0.75rem', overflow: 'auto', whiteSpace: 'pre-wrap' }}>
                {readyBody}
              </pre>
            ) : (
              <EmptyHint>No readiness body returned.</EmptyHint>
            )}
            {!loading &&
              !err &&
              readyStatus != null &&
              readyStatus < 400 &&
              readyBody &&
              (readyBody.includes('Healthy') || readyBody.includes('"status":"Healthy"')) && (
                <EmptyHint>Readiness checks reported healthy.</EmptyHint>
              )}
          </section>
        </div>
      )}
    </div>
  )
}
