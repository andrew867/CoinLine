export type RatePricing = { pricingMode?: number; initialChargeUsd?: number; ratePerMinuteUsd: number }

const money = (value: number) => `$${value.toFixed(2)}`

export function describeRatePricing(pricing: RatePricing) {
  const initial = pricing.initialChargeUsd ?? 0
  switch (pricing.pricingMode ?? 0) {
    case 1: return `${money(initial)} for the whole call`
    case 2: return `${money(pricing.ratePerMinuteUsd)} per started minute`
    case 3: return `${money(initial)} first minute, then ${money(pricing.ratePerMinuteUsd)}/minute`
    default: return `$${pricing.ratePerMinuteUsd}/minute (legacy estimate)`
  }
}

export function RatePlanPricing({ value, onChange }: { value: RatePricing; onChange: (value: RatePricing) => void }) {
  const mode = value.pricingMode ?? 0
  const initial = value.initialChargeUsd ?? 0
  return <div className="table-config-panel" aria-label="Call pricing">
    <label>Pricing structure
      <select value={mode} onChange={event => {
        const pricingMode = Number(event.target.value)
        onChange({ pricingMode, initialChargeUsd: pricingMode === 1 || pricingMode === 3
          ? mode === 1 || mode === 3 ? initial : value.ratePerMinuteUsd : 0,
        ratePerMinuteUsd: pricingMode === 1 ? 0 : mode === 1 ? initial : value.ratePerMinuteUsd })
      }}>
        <option value={1}>One charge for the whole call</option>
        <option value={2}>Same charge for every minute</option>
        <option value={3}>Different first minute and following minutes</option>
        {mode === 0 && <option value={0}>Existing per-minute rate (prorated estimate)</option>}
      </select>
    </label>
    {(mode === 1 || mode === 3) && <label>{mode === 1 ? 'Whole call charge (USD)' : 'First minute including connection (USD)'}
      <input type="number" min={0} max={655.35} step={0.01} required value={initial}
        onChange={event => onChange({ ...value, initialChargeUsd: Number(event.target.value) })} />
    </label>}
    {mode !== 1 && <label>{mode === 3 ? 'Each following minute (USD)' : 'Rate per minute (USD)'}
      <input type="number" min={0} max={mode === 0 ? undefined : 655.35} step={mode === 0 ? 'any' : 0.01} required value={value.ratePerMinuteUsd}
        onChange={event => onChange({ ...value, ratePerMinuteUsd: Number(event.target.value) })} />
    </label>}
    <small>{describeRatePricing(value)}</small>
  </div>
}
