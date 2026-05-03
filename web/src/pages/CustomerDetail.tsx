import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { EmptyHint, ErrorBanner, LoadingBlock } from '../components/AppStates'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Console = {
  customer: { id: string; name: string; code: string; createdAtUtc?: string }
  sites: { id: string; name: string; code: string }[]
  terminals: { id: string; siteId: string; displayName: string; terminalIdHex: string; status: number }[]
  tableSets: { id: string; name: string; status: number; publishedAtUtc: string | null; publishGeneration: number }[]
  ratePlans: { id: string; name: string; mode: number; publishedVersionId: string | null }[]
  cardProducts: { id: string; name: string; code: string; defaultCardType: number }[]
  cardProductsNote: string | null
}

export function CustomerDetail() {
  const { id } = useParams<{ id: string }>()
  const [data, setData] = useState<Console | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    if (!id) return
    let c = false
    setErr(null)
    fetch(`/api/operator/customers/${id}/console`, { headers })
      .then((r) => {
        if (r.status === 404) throw new Error('Not found')
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Console>
      })
      .then((j) => {
        if (!c) setData(j)
      })
      .catch((e) => {
        if (!c) setErr(e instanceof Error ? e.message : 'error')
      })
    return () => {
      c = true
    }
  }, [id])

  if (!id) return <p>Missing id.</p>
  if (err) return <ErrorBanner message={err} />
  if (!data) return <LoadingBlock label="Loading customer console…" />

  const { customer } = data

  return (
    <div>
      <p>
        <Link to="/customers">← Customers</Link>
      </p>
      <h1>{customer.name}</h1>
      <p style={{ color: '#555' }}>
        Code <code>{customer.code}</code> · id <code>{customer.id}</code>
      </p>
      {data.cardProductsNote && (
        <p role="note" style={{ background: '#fff9e6', padding: 10, maxWidth: 720 }}>
          {data.cardProductsNote}
        </p>
      )}

      <h2>Sites</h2>
      {data.sites.length === 0 ? (
        <EmptyHint>No sites — add under Sites.</EmptyHint>
      ) : (
        <ul>
          {data.sites.map((s) => (
            <li key={s.id}>
              {s.name} (<code>{s.code}</code>)
            </li>
          ))}
        </ul>
      )}

      <h2>Terminals</h2>
      {data.terminals.length === 0 ? (
        <EmptyHint>No terminals on this customer’s sites.</EmptyHint>
      ) : (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>Name</th>
              <th>Hex id</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {data.terminals.map((t) => (
              <tr key={t.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/terminals/${t.id}`}>{t.displayName}</Link>
                </td>
                <td>
                  <code>{t.terminalIdHex}</code>
                </td>
                <td>{t.status}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h2>Table sets</h2>
      {data.tableSets.length === 0 ? (
        <EmptyHint>No table sets.</EmptyHint>
      ) : (
        <ul>
          {data.tableSets.map((ts) => (
            <li key={ts.id}>
              <Link to={`/table-sets/${ts.id}`}>{ts.name}</Link> · gen {ts.publishGeneration}{' '}
              {ts.publishedAtUtc ? `· published ${new Date(ts.publishedAtUtc).toLocaleString()}` : ''}
            </li>
          ))}
        </ul>
      )}

      <h2>Rate plans</h2>
      {data.ratePlans.length === 0 ? (
        <EmptyHint>No rate plans.</EmptyHint>
      ) : (
        <ul>
          {data.ratePlans.map((rp) => (
            <li key={rp.id}>
              <Link to={`/rate-plans/${rp.id}`}>{rp.name}</Link> · mode {rp.mode}
            </li>
          ))}
        </ul>
      )}

      <h2>Card products in use</h2>
      {data.cardProducts.length === 0 ? (
        <EmptyHint>None linked via terminals yet.</EmptyHint>
      ) : (
        <ul>
          {data.cardProducts.map((p) => (
            <li key={p.id}>
              <Link to={`/card-products/${p.id}`}>{p.name}</Link> ({p.code})
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
