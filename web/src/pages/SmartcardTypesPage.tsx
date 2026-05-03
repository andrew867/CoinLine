import { useEffect, useState } from 'react'
import { apiGet } from '../api/client'

type Row = {
  id: string
  code: string
  name: string
  atrProfile: number
  mapsToCardType: number
  notes: string
}

export function SmartcardTypesPage() {
  const [rows, setRows] = useState<Row[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    setLoading(true)
    setErr(null)
    apiGet<Row[]>('/api/smartcards/types')
      .then(setRows)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setLoading(false))
  }, [])

  return (
    <div>
      <h1>Smartcard types</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Aligns with firmware <code>card_type_info</code> / MT 93 surfaces — details are{' '}
        <code>HARDWARE_VALIDATION_REQUIRED</code> for production acceptance.
      </p>
      {loading && <p role="status">Loading…</p>}
      {!loading && err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!loading && !err && rows.length === 0 && (
        <p role="status">No smartcard types — check seed / migrations.</p>
      )}
      {!loading && !err && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ borderBottom: '1px solid #ccc' }}>
              <th align="left">Code</th>
              <th align="left">Name</th>
              <th align="left">ATR profile</th>
              <th align="left">Maps to card type</th>
              <th align="left">Notes</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>{r.code}</td>
                <td>{r.name}</td>
                <td>{r.atrProfile}</td>
                <td>{r.mapsToCardType}</td>
                <td style={{ maxWidth: 360 }}>{r.notes}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
