import { useState } from 'react'

type Pricing = 'flat' | 'minute' | 'split'
const pricingLabels: Record<Pricing, string> = {
  flat: 'One charge for the whole call',
  minute: 'Same charge for every minute',
  split: 'Different first minute and following minutes',
}

const cents = (value: string) => /^\d+$/.test(value) && Number(value) <= 65535 ? Number(value) : null
const money = (value: number) => `$${(value / 100).toFixed(2)}`

type Props = {
  value: (key: string) => string | null | undefined
  editable: boolean
  onStage: (changes: Record<string, string>) => void
}

export function TerminalTariffPricing({ value, editable, onStage }: Props) {
  const initial = cents(value('initialCharge') ?? '')
  const extra = cents(value('additionalCharge') ?? '')
  const [pricing, setPricing] = useState<Pricing>(value('initialPeriod') === '-1' ? 'flat'
    : initial === extra ? 'minute' : 'split')
  const [first, setFirst] = useState(value('initialCharge') ?? '')
  const [additional, setAdditional] = useState(value('additionalCharge') ?? '')
  const firstCents = cents(first)
  const nextCents = pricing === 'flat' ? 0 : pricing === 'minute' ? firstCents : cents(additional)
  const valid = firstCents !== null && nextCents !== null

  return <section aria-label="Local-call pricing helper" className="table-config-panel">
    <h3>Local-call pricing</h3>
    <p>Choose a familiar pricing structure for this terminal tariff. Apply it to the selected row, then review and save the record. Other rate rows and header fields stay unchanged.</p>
    <label>Pricing structure
      <select value={pricing} disabled={!editable} onChange={event => setPricing(event.target.value as Pricing)}>
        {Object.entries(pricingLabels).map(([key, label]) => <option value={key} key={key}>{label}</option>)}
      </select>
    </label>
    <label>{pricing === 'flat' ? 'Whole call charge (cents)' : pricing === 'minute' ? 'Charge per minute (cents)' : 'First minute including connection (cents)'}
      <input type="number" min={0} max={65535} step={1} value={first} disabled={!editable} onChange={event => setFirst(event.target.value)} />
    </label>
    {pricing === 'split' && <label>Each following minute (cents)
      <input type="number" min={0} max={65535} step={1} value={additional} disabled={!editable} onChange={event => setAdditional(event.target.value)} />
    </label>}
    {valid && <p aria-label="Example pricing">1 minute: {money(firstCents!)}; 5 minutes: {money(firstCents! + 4 * nextCents!)}; 10 minutes: {money(firstCents! + 9 * nextCents!)}.</p>}
    <p className="page-intro">Applies {pricing === 'flat' ? 'Fixed local charge and an unlimited initial period' : 'Local measured service with 60-second periods'}. For toll, host-rated, or custom periods, edit the rate classification and fields below instead.</p>
    <button type="button" disabled={!editable || !valid} onClick={() => onStage({
      classification: pricing === 'flat' ? '2' : '1',
      initialPeriod: pricing === 'flat' ? '-1' : '60',
      initialCharge: String(firstCents),
      additionalPeriod: pricing === 'flat' ? '0' : '60',
      additionalCharge: String(nextCents),
    })}>Apply pricing to this row</button>
  </section>
}
