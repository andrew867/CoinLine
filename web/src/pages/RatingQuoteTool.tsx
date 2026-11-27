import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { useLocation } from 'react-router-dom'
import { apiGet, apiPost } from '../api/client'
import { describeRatePricing, type RatePricing } from './RatePlanPricing'

type Plan = { id: string; name: string; mode: number; customerId: string | null; publishedVersionId: string | null }
type Quote = { amountUsd: number; allowed: boolean; blocked: boolean; freeCall: boolean; emergency: boolean; pricing: RatePricing | null }
const reviewDigits = (state: unknown): string | null => {
  if (!state || typeof state !== 'object') return null
  const value = state as { source?: unknown; dialedDigits?: unknown }
  return value.source === 'saved-configuration' && typeof value.dialedDigits === 'string' && /^[0-9]{1,16}$/.test(value.dialedDigits) ? value.dialedDigits : null
}

export function RatingQuoteTool() {
  const location = useLocation()
  const fromReview = reviewDigits(location.state)
  const [plans, setPlans] = useState<Plan[]>([])
  const [planListLoading, setPlanListLoading] = useState(true)
  const [planId, setPlanId] = useState('')
  const [customerId, setCustomerId] = useState('')
  const [digits, setDigits] = useState(fromReview ?? '5551234')
  const [mode, setMode] = useState(1)
  const [minutes, setMinutes] = useState(1)
  const [quoteLoading, setQuoteLoading] = useState(false)
  const [out, setOut] = useState<Quote | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    setPlanListLoading(true)
    setErr(null)
    let cancelled = false
    apiGet<Plan[]>('/api/rate-plans')
      .then((r) => {
        if (cancelled) return
        const published = r.filter(p => p.publishedVersionId != null)
        setPlans(published)
        const preferred = fromReview && published.length > 1 ? undefined : published.find(p => p.name === 'Local default') ?? published[0]
        setPlanId(preferred?.id ?? '')
        setMode(preferred?.mode ?? 1)
        setCustomerId(preferred?.customerId ?? '')
      })
      .catch((e) => { if (!cancelled) setErr(e instanceof Error ? e.message : 'Could not load published plans.') })
      .finally(() => { if (!cancelled) setPlanListLoading(false) })
    return () => { cancelled = true }
  }, [fromReview])

  function changePlan(id: string) {
    const plan = plans.find(p => p.id === id)
    setPlanId(id); setMode(plan?.mode ?? 1); setCustomerId(plan?.customerId ?? ''); setOut(null); setErr(null)
  }

  async function quote(e: FormEvent) {
    e.preventDefault()
    if (!planId || !digits.trim() || !Number.isFinite(minutes) || minutes <= 0) return
    setErr(null)
    setQuoteLoading(true)
    setOut(null)
    try {
      const j = await apiPost<Quote>('/api/rating/quote', {
        dialedDigits: digits,
        mode,
        ratePlanId: planId || null,
        customerId: customerId.trim() || null,
        assumedDurationMinutes: minutes,
      })
      setOut(j)
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    } finally {
      setQuoteLoading(false)
    }
  }

  return (
    <div>
      <h1>Price a call</h1>
      <p style={{ color: '#a60', maxWidth: 720 }}>
        Quotes use the <strong>published</strong> rate-plan version only. This tool is for lab validation, not an
        assertion of production rating parity with payphones or MTR212.
      </p>
      {fromReview && <p>Digits copied from the saved terminal configuration review. Select the published host plan to compare separately; this is not the terminal's offline price or selected route.</p>}
      {planListLoading && <p role="status">Loading rate plans…</p>}
      {!planListLoading && plans.length === 0 && !err && (
        <p role="status">No published rate plans yet. Create and publish one from Rate plans.</p>
      )}
      {err && <p role="alert" style={{ color: 'crimson' }}>{err}</p>}
      {!planListLoading && plans.length > 0 && (
        <form onSubmit={quote} style={{ display: 'flex', flexDirection: 'column', gap: 8, maxWidth: 400 }}>
          <label>
            Rate plan
            <select value={planId} disabled={quoteLoading} required onChange={(e) => changePlan(e.target.value)}>
              <option value="">Choose a published rate plan</option>
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
              disabled={quoteLoading} onChange={(e) => { setCustomerId(e.target.value); setOut(null); setErr(null) }}
              placeholder="filters catalog scope in rating workflow"
              style={{ display: 'block', width: '100%', fontFamily: 'monospace', fontSize: 12 }}
            />
          </label>
          <label>
            Dialed digits
            <input value={digits} required disabled={quoteLoading} onChange={(e) => { setDigits(e.target.value); setOut(null); setErr(null) }} />
          </label>
          <label>
            Rating mode
            <select value={mode} disabled={quoteLoading} onChange={(e) => { setMode(+e.target.value); setOut(null); setErr(null) }}>
              <option value={0}>Unknown</option><option value={1}>Real-time rated</option><option value={2}>Set rated</option><option value={3}>Table rated</option>
            </select>
          </label>
          <label>
            Assumed minutes
            <input type="number" step="0.01" min="0.01" value={minutes} required disabled={quoteLoading} onChange={(e) => { setMinutes(+e.target.value); setOut(null); setErr(null) }} />
          </label>
          <button type="submit" data-testid="rating-quote-submit" disabled={quoteLoading || !planId || !digits.trim() || !Number.isFinite(minutes) || minutes <= 0}>
            {quoteLoading ? 'Quoting…' : 'Quote'}
          </button>
        </form>
      )}
      {out !== null && (
        <section aria-label="Quote result">
          <h2>{out.allowed ? `Estimated charge: $${out.amountUsd.toFixed(2)}` : out.blocked ? 'Call blocked' : 'No rate available'}</h2>
          {out.pricing && <p>{describeRatePricing(out.pricing)}</p>}
          {out.freeCall && <p>Free call</p>}{out.emergency && <p>Emergency call</p>}
          <h3>Technical quote details</h3>
          <pre style={{ marginTop: 16, background: '#f6f6f6', padding: 12 }}>{JSON.stringify(out, null, 2)}</pre>
        </section>
      )}
    </div>
  )
}
