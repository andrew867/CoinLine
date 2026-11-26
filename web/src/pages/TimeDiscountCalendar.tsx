import { useState } from 'react'
import { discountZones, formatDiscountWindow, previewTimeDiscount, type DiscountPayment, type DiscountValue, type DiscountZone } from './timeDiscountPreview'
import { DiscountCompanionReference } from './DiscountCompanionReference'

const zoneLabels: Record<DiscountZone, string> = { local: 'Local', intra: 'Intra-area', inter: 'Inter-area', international: 'International' }
const paymentLabels = ['Disabled', 'Coins', 'Cards', 'Coins and cards']

function localDate() {
  const today = new Date()
  return `${String(today.getFullYear()).padStart(4, '0')}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`
}

export function TimeDiscountCalendar({ value, setId, revision }: { value: DiscountValue; setId: string; revision: number }) {
  const [date, setDate] = useState(localDate)
  const [time, setTime] = useState('12:00')
  const [zone, setZone] = useState<DiscountZone>('local')
  const [payment, setPayment] = useState<DiscountPayment>('coin')
  const preview = previewTimeDiscount(value, date, time, zone, payment)
  return <details className="table-config-panel discount-calendar" open>
    <summary>Time-of-day and holiday schedule preview</summary>
    <p>Choose an example date and time on the phone’s local clock. The first time-matching period for that calendar day is selected, then its call-zone and payment fields are checked. The preview uses staged table edits and does not calculate a quote, change the table, or read the phone’s clock.</p>
    <div className="table-config-selectors">
      <label>Preview date<input type="date" value={date} onChange={event => setDate(event.target.value)} /></label>
      <label>Preview time<input type="time" value={time} onChange={event => setTime(event.target.value)} /></label>
      <label>Preview call zone<select value={zone} onChange={event => setZone(event.target.value as DiscountZone)}>
        {discountZones.map(item => <option key={item} value={item}>{zoneLabels[item]}</option>)}
      </select></label>
      <label>Preview payment<select value={payment} onChange={event => setPayment(event.target.value as DiscountPayment)}>
        <option value="coin">Coin</option><option value="card">Card</option>
      </select></label>
    </div>
    <div aria-live="polite">
      {preview.kind === 'unavailable' ? <p>{preview.message}</p> : <>
        <dl className="native-table-summary">
          <div><dt>Calendar category</dt><dd>{preview.day}{preview.holidaySlots.length > 0 ? ` (holiday ${preview.holidaySlots.join(', ')})` : ''}</dd></div>
          {preview.periods.map(period => <div key={period.number}><dt>Period {period.number}</dt><dd>{formatDiscountWindow(period.window)}</dd></div>)}
        </dl>
        {preview.invalidHolidaySlots.length > 0 && <p>Review invalid holiday entries: {preview.invalidHolidaySlots.join(', ')}. Their original values are retained.</p>}
        {preview.overlap && <p>These periods overlap. Period 1 takes precedence, even when its payment option does not apply.</p>}
        {preview.selection.kind !== 'matched' ? <p>{preview.selection.message}</p> : <>
          <dl className="native-table-summary">
            <div><dt>Selected schedule period</dt><dd>Period {preview.selection.period}</dd></div>
            <div><dt>Configured discount reference</dt><dd>{preview.selection.discountIndex === null || preview.selection.discountIndex > 9
              ? 'Unknown or unsupported index' : `MT93 discount ${preview.selection.discountIndex + 1}`}</dd></div>
            <div><dt>Configured payment</dt><dd>{preview.selection.paymentMask === null ? 'Unavailable' : paymentLabels[preview.selection.paymentMask]}</dd></div>
          </dl>
          <p>{preview.selection.eligible === true ? 'The selected MT152 period enables this payment and references a discount row.'
            : preview.selection.eligible === false ? 'The selected period does not enable this payment. Period 2 is not a fallback.'
              : 'The selected payment or discount reference cannot be interpreted from the current fields.'}</p>
          {preview.selection.eligible === true && preview.selection.discountIndex !== null && preview.selection.discountIndex <= 9 &&
            <DiscountCompanionReference setId={setId} revision={revision} index={preview.selection.discountIndex} zone={zone} />}
        </>}
      </>}
    </div>
    <p>Holiday dates recur by month and day. Overnight windows are evaluated against the selected day’s category, not carried from the preceding day. The upper time boundary is excluded. Verify the installed MT93 table and actual firmware behavior before relying on discounts.</p>
  </details>
}
