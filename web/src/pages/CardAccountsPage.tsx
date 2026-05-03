import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPost } from '../api/client'
import { EmptyHint, ErrorBanner, LoadingBlock } from '../components/AppStates'

type Row = {
  id: string
  cardProductId: string
  terminalId: string | null
  panLast4Display: string
  credentialTokenMasked: string
  resolvedCardType: number
  credentialKind: number
  balance: number
}

type Product = { id: string; name: string; code: string }

export function CardAccountsPage() {
  const [rows, setRows] = useState<Row[]>([])
  const [products, setProducts] = useState<Product[]>([])
  const [productId, setProductId] = useState('')
  const [terminalId, setTerminalId] = useState('')
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [createErr, setCreateErr] = useState<string | null>(null)

  const load = () =>
    apiGet<Row[]>('/api/cards/accounts')
      .then(setRows)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))

  useEffect(() => {
    setLoading(true)
    setErr(null)
    void Promise.all([
      apiGet<Product[]>('/api/cards/products').then((p) => {
        setProducts(p)
        setProductId((prev) => prev || p[0]?.id || '')
      }),
      load(),
    ])
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setLoading(false))
  }, [])

  async function onCreate(e: FormEvent) {
    e.preventDefault()
    if (!productId) return
    setCreateErr(null)
    try {
      await apiPost<{ id: string }>('/api/cards/accounts', {
        cardProductId: productId,
        terminalId: terminalId.trim() || null,
        panLast4: '4242',
        balance: 0,
        credentialTokenRef: `e2e-${Date.now()}`,
      })
      setTerminalId('')
      await load()
    } catch (ex) {
      setCreateErr(ex instanceof Error ? ex.message : 'create failed')
    }
  }

  return (
    <div>
      <h1>Card accounts</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Responses mask opaque credential tokens. PAN fields are last-four display only (PCI scope).
      </p>
      <section style={{ marginBottom: 20, padding: 12, border: '1px solid #ddd', borderRadius: 6, maxWidth: 520 }}>
        <h2 style={{ marginTop: 0, fontSize: '1rem' }}>Create account (lab)</h2>
        {createErr && <ErrorBanner message={createErr} />}
        <form onSubmit={(e) => void onCreate(e)} style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          <label>
            Card product
            <select
              data-testid="card-account-product"
              value={productId}
              onChange={(e) => setProductId(e.target.value)}
              style={{ display: 'block', width: '100%' }}
            >
              {products.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name} ({p.code})
                </option>
              ))}
            </select>
          </label>
          <label>
            Terminal id (optional)
            <input
              data-testid="card-account-terminal"
              value={terminalId}
              onChange={(e) => setTerminalId(e.target.value)}
              placeholder="Guid"
              style={{ display: 'block', width: '100%', fontFamily: 'monospace', fontSize: 12 }}
            />
          </label>
          <button type="submit" data-testid="card-account-create">
            Create account
          </button>
        </form>
      </section>
      {loading && <LoadingBlock />}
      {!loading && err && <ErrorBanner message={err} />}
      {!loading && !err && rows.length === 0 && (
        <EmptyHint>No accounts yet — create one above or rely on seed data.</EmptyHint>
      )}
      {!loading && !err && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ borderBottom: '1px solid #ccc' }}>
              <th align="left">PAN display</th>
              <th align="left">Token (masked)</th>
              <th align="left">Type</th>
              <th align="right">Balance</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/card-accounts/${r.id}`}>{r.panLast4Display}</Link>
                </td>
                <td>{r.credentialTokenMasked || '—'}</td>
                <td>{r.resolvedCardType}</td>
                <td align="right">{r.balance}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
