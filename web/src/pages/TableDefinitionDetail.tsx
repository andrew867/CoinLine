import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Def = { id: string; name: string; tableNumber: number; description: string | null }

export function TableDefinitionDetail() {
  const { id } = useParams()
  const [d, setD] = useState<Def | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    if (!id) return
    let c = false
    setLoading(true)
    setErr(null)
    setD(null)
    ;(async () => {
      try {
        const r = await fetch(`/api/tables/definitions/${id}`, { headers })
        if (r.status === 404) {
          if (!c) {
            setErr('not-found')
            setLoading(false)
          }
          return
        }
        if (!r.ok) throw new Error(String(r.status))
        const j = (await r.json()) as Def
        if (!c) {
          setD(j)
          setLoading(false)
        }
      } catch (e) {
        if (!c) {
          setErr(e instanceof Error ? e.message : 'error')
          setLoading(false)
        }
      }
    })()
    return () => {
      c = true
    }
  }, [id])

  if (!id) return <p>Missing id.</p>
  if (loading) return <p>Loading…</p>
  if (err === 'not-found') {
    return (
      <div>
        <p>
          <Link to="/table-definitions">← Definitions</Link>
        </p>
        <p style={{ color: '#666' }}>No definition with this id (404).</p>
      </div>
    )
  }
  if (err) return <p style={{ color: 'crimson' }}>{err}</p>
  if (!d) return <p style={{ color: '#666' }}>No data.</p>

  return (
    <div>
      <p>
        <Link to="/table-definitions">← Definitions</Link>
      </p>
      <h1>{d.name}</h1>
      <p>
        Table number: <strong>{d.tableNumber}</strong>
      </p>
      <p>Id: {d.id}</p>
      {d.description && <p>{d.description}</p>}
    </div>
  )
}
