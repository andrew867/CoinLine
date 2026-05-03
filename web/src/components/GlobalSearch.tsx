import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type SearchResp = {
  query: string
  customers: { kind: string; id: string; name?: string; code?: string }[]
  sites: { kind: string; id: string; name?: string; code?: string; customerId?: string }[]
  terminals: { kind: string; id: string; displayName?: string; terminalIdHex?: string; siteId?: string }[]
  cardAccounts: { kind: string; id: string; cardProductId?: string; terminalId?: string | null }[]
}

export function GlobalSearch() {
  const [q, setQ] = useState('')
  const [open, setOpen] = useState(false)
  const [data, setData] = useState<SearchResp | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [searching, setSearching] = useState(false)

  useEffect(() => {
    if (!q.trim()) {
      setData(null)
      setSearching(false)
      return
    }
    let cancelled = false
    const t = setTimeout(() => {
      void (async () => {
        try {
          setErr(null)
          setSearching(true)
          const r = await fetch(`/api/operator/search?q=${encodeURIComponent(q.trim())}&limit=12`, { headers })
          if (!r.ok) throw new Error(String(r.status))
          const j = (await r.json()) as SearchResp
          if (!cancelled) setData(j)
        } catch (e) {
          if (!cancelled) setErr(e instanceof Error ? e.message : 'search failed')
        } finally {
          if (!cancelled) setSearching(false)
        }
      })()
    }, 220)
    return () => {
      cancelled = true
      clearTimeout(t)
    }
  }, [q])

  const hasRows =
    data &&
    (data.customers.length > 0 ||
      data.sites.length > 0 ||
      data.terminals.length > 0 ||
      data.cardAccounts.length > 0)

  return (
    <div style={{ marginBottom: 12, position: 'relative' }}>
      <label htmlFor="global-search" style={{ fontSize: 13, fontWeight: 600 }}>
        Search
      </label>
      <input
        id="global-search"
        data-testid="global-search-input"
        value={q}
        onChange={(e) => {
          setQ(e.target.value)
          setOpen(true)
        }}
        onFocus={() => setOpen(true)}
        placeholder="Customer, site, terminal, card account…"
        style={{ display: 'block', width: '100%', maxWidth: 420, padding: '6px 8px', marginTop: 4 }}
      />
      {open && q.trim() && (
        <div
          style={{
            position: 'absolute',
            zIndex: 20,
            background: '#fff',
            border: '1px solid #ccc',
            borderRadius: 4,
            marginTop: 4,
            padding: 8,
            maxWidth: 480,
            maxHeight: 320,
            overflow: 'auto',
            boxShadow: '0 4px 12px rgba(0,0,0,.08)',
          }}
        >
          {err && <p style={{ color: 'crimson', margin: 0 }}>{err}</p>}
          {searching && !err && <p role="status" style={{ margin: 0, color: '#666', fontSize: 13 }}>Searching…</p>}
          {!searching && !err && !hasRows && (
            <p style={{ margin: 0, color: '#666', fontSize: 13 }}>No matches.</p>
          )}
          {hasRows && (
            <ul style={{ margin: 0, paddingLeft: 18, fontSize: 13 }}>
              {data!.customers.map((c) => (
                <li key={`c-${c.id}`}>
                  <Link to={`/customers/${c.id}`} onClick={() => setOpen(false)}>
                    Customer · {c.name} ({c.code})
                  </Link>
                </li>
              ))}
              {data!.sites.map((s) => (
                <li key={`s-${s.id}`}>
                  Site · {s.name} ({s.code})
                </li>
              ))}
              {data!.terminals.map((t) => (
                <li key={`t-${t.id}`}>
                  <Link to={`/terminals/${t.id}`} onClick={() => setOpen(false)}>
                    Terminal · {t.displayName} · {t.terminalIdHex}
                  </Link>
                </li>
              ))}
              {data!.cardAccounts.map((a) => (
                <li key={`a-${a.id}`}>
                  <Link to={`/card-accounts/${a.id}`} onClick={() => setOpen(false)}>
                    Card account · {a.id.slice(0, 8)}…
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}
