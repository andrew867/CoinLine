import { useEffect, useState } from 'react'

export function JsonPanel(props: { title: string; url: string }) {
  const [data, setData] = useState<unknown>(null)
  const [err, setErr] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let cancelled = false
    ;(async () => {
      try {
        setLoading(true)
        const r = await fetch(props.url, { headers: { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' } })
        if (!r.ok) throw new Error(String(r.status))
        const j = await r.json()
        if (!cancelled) {
          setData(j)
          setErr(null)
        }
      } catch (e) {
        if (!cancelled) setErr(e instanceof Error ? e.message : 'error')
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [props.url])

  return (
    <div>
      <h2>{props.title}</h2>
      {loading && <p>Loading…</p>}
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!loading && !err && Array.isArray(data) && data.length === 0 && (
        <p style={{ color: '#666' }}>No records.</p>
      )}
      {!loading && !err && !(Array.isArray(data) && data.length === 0) && (
        <pre style={{ background: '#f6f8fa', padding: 12, overflow: 'auto', maxHeight: '70vh' }}>
          {JSON.stringify(data, null, 2)}
        </pre>
      )}
    </div>
  )
}
