import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiGet } from '../api/client'

type Detail = {
  id: string
  name: string
  code: string
  defaultCardType: number
  allowNegativeBalance: boolean
  isTestFixtureCatalogEntry: boolean
}

export function CardProductDetail() {
  const { id } = useParams<{ id: string }>()
  const [row, setRow] = useState<Detail | null>(null)
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    if (!id) {
      setLoading(false)
      setErr('Missing product id.')
      return
    }
    setLoading(true)
    setErr(null)
    apiGet<Detail>(`/api/cards/products/${id}`)
      .then(setRow)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setLoading(false))
  }, [id])

  return (
    <div>
      <p>
        <Link to="/card-products">← Card products</Link>
      </p>
      <h1>Card product</h1>
      {loading && <p role="status">Loading…</p>}
      {!loading && err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!loading && !err && !row && <p role="status">Not found.</p>}
      {!loading && !err && row && (
        <dl style={{ display: 'grid', gridTemplateColumns: '160px 1fr', gap: 8 }}>
          <dt>Name</dt>
          <dd>{row.name}</dd>
          <dt>Code</dt>
          <dd>{row.code}</dd>
          <dt>Default card type</dt>
          <dd>{row.defaultCardType}</dd>
          <dt>Allow negative balance</dt>
          <dd>{row.allowNegativeBalance ? 'yes' : 'no'}</dd>
          <dt>Test fixture catalog</dt>
          <dd>{row.isTestFixtureCatalogEntry ? 'yes' : 'no'}</dd>
        </dl>
      )}
    </div>
  )
}
