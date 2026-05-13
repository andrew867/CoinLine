import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { apiGet, apiPost } from '../api/client'

type Plan = { id: string; name: string }

export function RatingQuoteTool() {
  const [plans, setPlans] = useState<Plan[]>([])
  const [planListLoading, setPlanListLoading] = useState(true)
  const [planId, setPlanId] = useState('')
  const [customerId, setCustomerId] = useState('')
  const [digits, setDigits] = useState('5551234')
  const [mode, setMode] = useState(1)
  const [minutes, setMinutes] = useState(1)
  const [quoteLoading, setQuoteLoading] = useState(false)
  const [out, setOut] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    setPlanListLoading(true)
    setErr(null)
    apiGet<Array<{ id: string; name: string }>>('/api/rate-plans')
      .then((r) => {
        setPlans(r)
        const local = r.find((p) => p.name === 'Local default')
        if (local) setPlanId(local.id)
        else if (r[0]) setPlanId(r[0].id)
      })
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setPlanListLoading(false))
  }, [])

  async function quote(e: FormEvent) {
    e.preventDefault()
    setErr(null)
    setQuoteLoading(true)
    setOut(null)
    try {
      const j = await apiPost<Record<string, unknown>>('/api/rating/quote', {
        dialedDigits: digits,
        mode,
        ratePlanId: planId || null,
        customerId: customerId.trim() || null,
        assumedDurationMinutes: minutes,
      })
      setOut(JSON.stringify(j, null, 2))
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    } finally {
      setQuoteLoading(false)
    }
  }

  return (
    <div>
      <h1>Rating quote (test)</h1>
      <p style={{ color: '#a60', maxWidth: 720 }}>
        Quotes use the <strong>published</strong> rate-plan version only. This tool is for lab validation — not an
        assertion of production rating parity with payphones or reference terminal firmware.
      </p>
      {planListLoading && <p role="status">Loading rate plans…</p>}
      {!planListLoading && plans.length === 0 && !err && (
        <p role="status">No rate plans available — create one via API or seed.</p>
      )}
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!planListLoading && plans.length > 0 && (
        <form onSubmit={quote} style={{ display: 'flex', flexDirection: 'column', gap: 8, maxWidth: 400 }}>
          <label>
            Rate plan
            <select value={planId} onChange={(e) => setPlanId(e.target.value)}>
              {plans.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            Customer id (optional Guid)
            <input
              data-testid="rating-quote-customer-id"
              value={customerId}
              onChange={(e) => setCustomerId(e.target.value)}
              placeholder="filters catalog scope in rating workflow"
              style={{ display: 'block', width: '100%', fontFamily: 'monospace', fontSize: 12 }}
            />
          </label>
          <label>
            Dialed digits
            <input value={digits} onChange={(e) => setDigits(e.target.value)} />
          </label>
          <label>
            Mode (0=unknown,1=real-time,2=set,3=table)
            <input type="number" value={mode} onChange={(e) => setMode(+e.target.value)} />
          </label>
          <label>
            Assumed minutes
            <input type="number" step="0.01" value={minutes} onChange={(e) => setMinutes(+e.target.value)} />
          </label>
          <button type="submit" data-testid="rating-quote-submit" disabled={quoteLoading}>
            {quoteLoading ? 'Quoting…' : 'Quote'}
          </button>
        </form>
      )}
      {out !== null && (
        <pre style={{ marginTop: 16, background: '#f6f6f6', padding: 12 }}>{out}</pre>
      )}
    </div>
  )
}
