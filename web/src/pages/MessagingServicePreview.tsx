type Props = { value: (key: string) => string | null | undefined }

const calls = [['allowLocal', 'Local'], ['allowIntraArea', 'Intra-area'], ['allowInterArea', 'Inter-area'], ['allowInternational', 'International']]
const payments = [['allowCoin', 'Coins'], ['allowStoredValue', 'Stored-value cards'], ['allowCredit', 'Credit cards'], ['allowCallingCard', 'Calling cards']]

export function MessagingServicePreview({ value }: Props) {
  const isOn = (key: string) => { const field = value(key); return field != null && /^\d+$/.test(field) && Number(field) !== 0 }
  const enabled = isOn('enabled')
  const selected = (fields: string[][]) => fields.filter(([key]) => value(key) === 'true').map(([, label]) => label).join(', ') || 'None'
  const busy = Number(value('busyCycles')), ringing = Number(value('ringCycles'))
  return <section className="table-config-panel" aria-label="Audio-Gram configuration preview">
    <h3>Audio-Gram configuration preview</h3>
    <p>These parameters configure a compatible messaging service accessed through the switch. CoinLine distributes the record and receives terminal reports; it does not provide the messaging switch.</p>
    <dl className="native-table-summary" aria-live="polite">
      <div><dt>Service</dt><dd>{enabled ? 'Enabled' : 'Disabled'}</dd></div>
      <div><dt>Permitted calls</dt><dd>{selected(calls)}</dd></div>
      <div><dt>Permitted payments</dt><dd>{selected(payments)}</dd></div>
      <div><dt>Service statistics</dt><dd>{value('statistics') === '1' ? 'Collect MT69' : 'Disabled'}</dd></div>
      <div><dt>Service call records</dt><dd>{isOn('cdrs') ? 'Request collection' : 'Normal call filtering'}</dd></div>
    </dl>
    {enabled && (selected(calls) === 'None' || selected(payments) === 'None') && <p>No compatible call and payment combination is selected.</p>}
    {enabled && (busy < 2 || ringing <= busy) && <p>The firmware will use its internal offer-detection thresholds. Set Busy cycles to at least 2 and Unanswered ringing cycles above Busy cycles to use the configured counts.</p>}
  </section>
}
