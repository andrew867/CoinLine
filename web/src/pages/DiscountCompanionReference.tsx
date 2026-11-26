import { useEffect, useState } from 'react'
import { apiFetch } from '../api/client'
import type { DiscountZone } from './timeDiscountPreview'

type SavedField = { key: string; value: string | null; choices?: { value: number; label: string }[] | null }
type SavedRecord = { supported: true; profile: string; length: number; revision: number; sections: { fields: SavedField[] }[] }
type ReadState = { kind: 'loading' | 'unavailable' | 'error'; message?: string } | { kind: 'available'; record: SavedRecord }

function useSavedRecord(setId: string, number: number, revision: number, refresh: number, enabled = true) {
  const [state, setState] = useState<ReadState>({ kind: 'loading' })
  useEffect(() => {
    if (!enabled) return
    const controller = new AbortController()
    setState({ kind: 'loading' })
    void apiFetch(`/api/tables/sets/${encodeURIComponent(setId)}/records/${number}/fields`, { signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        const record = await response.json() as SavedRecord | { supported: false; reason?: string }
        if (!controller.signal.aborted) setState(record.supported ? { kind: 'available', record }
          : { kind: 'unavailable', message: record.reason })
      }).catch(error => {
        if (!controller.signal.aborted) setState({ kind: 'error', message: error instanceof Error ? error.message : 'Request failed' })
      })
    return () => controller.abort()
  }, [setId, number, revision, refresh, enabled])
  return state
}

const field = (record: SavedRecord, key: string) => record.sections.flatMap(section => section.fields).find(item => item.key === key)
function integer(value: string | null | undefined, min: number, max: number) {
  if (value == null || !/^-?\d+$/.test(value)) return null
  const parsed = Number(value)
  return Number.isSafeInteger(parsed) && parsed >= min && parsed <= max ? parsed : null
}
const modeNames = ['Signed cents adjustment', 'Percentage of normal charge', 'Replacement tariff', 'Inactive']

export function readDiscountCompanion(record: SavedRecord, index: number, zone: DiscountZone) {
  if (record.length !== 225 || record.profile !== 'Later packed profile' || !Number.isInteger(index) || index < 0 || index > 9) return null
  const key = `discount-${index}.${zone}`
  const mode = integer(field(record, `${key}Mode`)?.value, 0, 3)
  if (mode === null) return null
  const first = field(record, `${key}Initial`)?.value, later = field(record, `${key}Later`)?.value
  return mode === 0 ? { mode, first: integer(first, -128, 127), later: integer(later, -128, 127), rate: null }
    : mode === 1 ? { mode, first: integer(first, 0, 255), later: integer(later, 0, 255), rate: null }
      : mode === 2 ? { mode, first: null, later: null, rate: integer(first, 0, 127) }
        : { mode, first: null, later: null, rate: null }
}

function describeAmount(value: number | null, mode: number) {
  if (value === null) return 'Unavailable or invalid'
  return mode === 0 ? `${value > 0 ? '+' : ''}${value} cents` : `${value}% of normal charge`
}

function describePeriod(value: string | null | undefined) {
  const seconds = integer(value, -1, 32767)
  return seconds === null ? 'Unavailable or invalid' : seconds === -1 ? 'Unlimited' : `${seconds} s`
}

function TariffReference({ record, index }: { record: SavedRecord; index: number }) {
  if (record.length !== 1192) return <p>Saved MT73 is not a supported tariff layout.</p>
  const key = `rate-${index}`
  const classification = field(record, `${key}.classification`)
  if (!classification) return <p>MT73 rate {index} is not available in this saved record.</p>
  const code = integer(classification.value, 0, 255)
  const label = classification.choices?.find(choice => choice.value === code)?.label
  const charge = (name: string) => {
    const value = integer(field(record, `${key}.${name}`)?.value, 0, 65535)
    return value === null ? 'Unavailable or invalid' : `${value} cents`
  }
  return <section aria-label="Saved replacement tariff">
    <h4>MT73 rate {index} · revision {record.revision}</h4>
    <dl className="native-table-summary">
      <div><dt>Classification</dt><dd>{label ?? (code === null ? 'Unavailable' : `Stored code ${code}`)}</dd></div>
      <div><dt>Initial period</dt><dd>{describePeriod(field(record, `${key}.initialPeriod`)?.value)}</dd></div>
      <div><dt>Initial charge</dt><dd>{charge('initialCharge')}</dd></div>
      <div><dt>Additional period</dt><dd>{describePeriod(field(record, `${key}.additionalPeriod`)?.value)}</dd></div>
      <div><dt>Additional charge</dt><dd>{charge('additionalCharge')}</dd></div>
    </dl>
  </section>
}

function ReferenceStatus({ state, number }: { state: ReadState; number: number }) {
  if (state.kind === 'loading') return <p role="status">Loading saved MT{number}…</p>
  if (state.kind === 'error') return <p role="alert">Could not load saved MT{number}: {state.message}.</p>
  return <p>No compatible saved MT{number} reference is available in this set.{state.kind === 'unavailable' && state.message && ` ${state.message}`}</p>
}

export function DiscountCompanionReference({ setId, revision, index, zone }: { setId: string; revision: number; index: number; zone: DiscountZone }) {
  const [refresh, setRefresh] = useState(0)
  const discountState = useSavedRecord(setId, 93, revision, refresh)
  const discount = discountState.kind === 'available' ? readDiscountCompanion(discountState.record, index, zone) : null
  const rate = discount?.mode === 2 ? discount.rate : null
  const tariffState = useSavedRecord(setId, 73, revision, refresh, rate !== null)
  return <section className="discount-companion-reference" aria-label="Saved discount reference">
    <div className="row-actions"><h4>Saved discount row {index + 1} (MT93)</h4>
      <button type="button" className="quiet" onClick={() => setRefresh(value => value + 1)}>Refresh saved references</button></div>
    {discountState.kind !== 'available' ? <ReferenceStatus state={discountState} number={93} />
      : discount === null ? <p>The saved MT93 does not expose a compatible discount row for this layout.</p> : <>
        <p>MT93 revision {discountState.record.revision} · {modeNames[discount.mode]}</p>
        {discount.mode === 0 || discount.mode === 1 ? <dl className="native-table-summary">
          <div><dt>Initial period adjustment</dt><dd>{describeAmount(discount.first, discount.mode)}</dd></div>
          <div><dt>Additional period adjustment</dt><dd>{describeAmount(discount.later, discount.mode)}</dd></div>
        </dl> : discount.mode === 3 ? <p>This referenced zone is inactive. No charge adjustment or replacement tariff is selected by this row.</p>
          : rate === null ? <p>The stored replacement rate index cannot be interpreted.</p>
            : <><p>Replacement reference: MT73 rate {rate}.</p>
              {tariffState.kind === 'available' ? <TariffReference record={tariffState.record} index={rate} />
                : <ReferenceStatus state={tariffState} number={73} />}</>}
      </>}
    <p>These are the saved companion records in this set, not a calculated call charge. Verify the installed records, call policy and terminal rounding behavior separately.</p>
  </section>
}
