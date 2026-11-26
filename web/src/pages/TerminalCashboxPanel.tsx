import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet } from '../api/client'
import { EmptyHint, ErrorBanner, LoadingBlock } from '../components/AppStates'
import './CashboxQueryPage.css'

type Status = {
  boxNumber: string
  terminalTime?: string | null
  condition?: string
  unmappedSetBitCount?: number
  percentFull: number
  balanceMinorUnits: number
  coinCounts: number[]
}

type CoinSlot = { number: number; valueMinorUnits: number | null; validatorWindowEnabled: boolean; accepted: boolean }
type CoinConfiguration = {
  status: 'acknowledged' | 'missing' | 'ambiguous' | 'invalid'
  tableRevision?: number | null
  ackRecordedAtUtc?: string | null
  laterUncertainAttempt?: boolean
  downloadBatchId?: string | null
  slots?: CoinSlot[] | null
}

type Ledger = {
  statuses: { id: string; receivedAtUtc: string; status: Status; isCurrent: boolean }[]
  collections: {
    id: string
    receivedAtUtc: string
    terminalTime?: string | null
    boxNumber: string
    newBoxNumber: string
    totalMinorUnits: number
    removedBoxBalanceMinorUnits: number
    isSummed: boolean
    coinCounts: number[]
  }[]
  coinConfigurations?: Record<string, CoinConfiguration>
}

function amount(minorUnits: number): string {
  return (minorUnits / 100).toFixed(2)
}

const hostTime = (value: string) => new Date(value).toISOString().replace('T', ' ').replace(/\.\d{3}Z$/, ' UTC')
const phoneTime = (value?: string | null) => value ? `${value.replace('T', ' ')} (phone clock)` : 'Not available'

function CoinCounts({ counts, reference }: { counts: number[]; reference?: CoinConfiguration }) {
  const usable = reference?.status === 'acknowledged' && !reference.laterUncertainAttempt
  const slots = new Map<number, CoinSlot>(usable ? reference.slots?.map(slot => [slot.number, slot] as const) : [])
  const entries = counts.flatMap((count, index) => {
    const slot = slots.get(index + 1)
    const value = slot?.valueMinorUnits
    const active = slot?.validatorWindowEnabled && slot.accepted
    if (!count && !(active && value != null && value > 0)) return []
    const label = value != null && value > 0 ? ` (configured ${amount(value)}${active ? '' : ', not enabled for acceptance'})` : ''
    return [{ number: index + 1, text: `Slot ${index + 1}: ${count}${label}` }]
  })
  return <div className="cashbox-coins">
    {entries.length ? <ul className="cashbox-coin-slots" aria-label="Reported coin counts">{entries.map(entry =>
      <li key={entry.number}>{entry.text}</li>)}</ul> : <span>None</span>}
    {!reference && <small>Coin configuration reference unavailable.</small>}
    {reference?.status === 'missing' && <small>No matching MT50 acknowledgement before receipt.</small>}
    {reference?.status === 'ambiguous' && <small>Multiple MT50 acknowledgements share the same recorded time. Raw slots only.</small>}
    {reference?.status === 'invalid' && <small>The acknowledged MT50 has no verified coin-slot layout. Raw slots only.</small>}
    {reference?.status === 'acknowledged' && <small>
      MT50 revision {reference.tableRevision}, acknowledged {reference.ackRecordedAtUtc ? hostTime(reference.ackRecordedAtUtc) : 'at an unknown time'}.
      {reference.laterUncertainAttempt ? ' A later transfer lacks a confirmed acknowledgement. Raw slots only.' : ' Configured values are a receipt-time reference, not verified denominations of accumulated coins.'}
    </small>}
    {reference?.downloadBatchId && <Link to={`/downloads/${reference.downloadBatchId}`}>Source download</Link>}
  </div>
}

function Condition({ status }: { status: Status }) {
  return <><span>{status.condition ?? 'Condition not decoded'}</span>
    {!!status.unmappedSetBitCount && <small className="cashbox-unmapped">{status.unmappedSetBitCount} unmapped status {status.unmappedSetBitCount === 1 ? 'bit' : 'bits'} set</small>}</>
}

export function TerminalCashboxPanel({ terminalId, refresh = 0 }: { terminalId: string; refresh?: number }) {
  const [ledger, setLedger] = useState<Ledger | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    let current = true
    setLedger(null)
    setError('')
    void apiGet<Ledger>(`/api/terminals/${terminalId}/cashbox`)
      .then(result => { if (current) setLedger(result) })
      .catch(() => { if (current) setError('Could not load cashbox reports.') })
    return () => { current = false }
  }, [terminalId, refresh])

  const current = ledger?.statuses.find(row => row.isCurrent)
  const hasHistory = ledger && (ledger.statuses.length > 0 || ledger.collections.length > 0)
  return (
    <section aria-labelledby="cashbox-heading">
      <h2 id="cashbox-heading">Cashbox</h2>
      {error && <ErrorBanner message={error} />}
      {!ledger && !error && <LoadingBlock label="Loading cashbox reports…" />}
      {ledger && ledger.statuses.length === 0 && ledger.collections.length === 0 && <>
        <EmptyHint>No cashbox reports yet.</EmptyHint>
        <div className="cashbox-waiting" role="note">
          <strong>Current balance unknown.</strong> The manager has not received a cashbox status (MT38) or collection (MT51) report from this phone.
          A successfully uploaded coin call can still exist without a cashbox report. Its charge and collected amount do not establish the current box balance.
        </div>
      </>}
      {hasHistory && <p>Amounts use the phone’s currency. The cashbox data is reported by the terminal, not calculated from quoted charges or deposited amounts.</p>}
      {current && <section className="cashbox-summary" aria-label="Current reported cashbox">
        <h3>Current reported cashbox</h3>
        <div className="cashbox-metrics">
          <div><span>Reported balance</span><strong>{amount(current.status.balanceMinorUnits)}</strong></div>
          <div><span>Capacity</span><strong>{current.status.percentFull}%</strong></div>
          <div><span>Condition</span><strong><Condition status={current.status} /></strong></div>
          <div><span>Box number</span><strong>{current.status.boxNumber}</strong></div>
        </div>
        <dl className="cashbox-times"><div><dt>Manager received</dt><dd>{hostTime(current.receivedAtUtc)}</dd></div>
          <div><dt>Reported phone time</dt><dd>{phoneTime(current.status.terminalTime)}</dd></div></dl>
        <div className="cashbox-summary-coin-counts"><strong>Reported coin counts by slot</strong>
          <CoinCounts counts={current.status.coinCounts} reference={ledger?.coinConfigurations?.[current.id]} /></div>
      </section>}
      {hasHistory && !current && <p className="cashbox-waiting" role="note">No current box status is available. The last reported collection does not establish the value or condition of the replacement box.</p>}
      {ledger && ledger.statuses.length > 0 && (
        <div style={{ overflowX: 'auto' }}>
          <table>
            <caption>Latest status by box</caption>
            <thead><tr><th>Box</th><th>Report status</th><th>Condition</th><th>Balance</th><th>Full</th><th>Coin counts</th><th>Phone time</th><th>Received (UTC)</th></tr></thead>
            <tbody>{ledger.statuses.map(row => (
              <tr key={row.id}>
                <td><code>{row.status.boxNumber}</code></td><td>{row.isCurrent ? 'Current' : 'Previous'}</td><td><Condition status={row.status} /></td>
                <td>{amount(row.status.balanceMinorUnits)}</td><td>{row.status.percentFull}%</td>
                <td><CoinCounts counts={row.status.coinCounts} reference={ledger.coinConfigurations?.[row.id]} /></td><td>{phoneTime(row.status.terminalTime)}</td><td>{hostTime(row.receivedAtUtc)}</td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      )}
      {ledger && ledger.collections.length > 0 && (
        <div style={{ overflowX: 'auto' }}>
          <table>
            <caption>Reported collections</caption>
            <thead><tr><th>Removed box</th><th>New box</th><th>Reported total</th><th>Removed box balance</th><th>Removed box coins</th><th>Phone time</th><th>Received (UTC)</th></tr></thead>
            <tbody>{ledger.collections.map(row => (
              <tr key={row.id}>
                <td><code>{row.boxNumber}</code></td><td><code>{row.newBoxNumber}</code></td>
                <td>{amount(row.totalMinorUnits)}{row.isSummed && ' (combined)'}</td>
                <td>{amount(row.removedBoxBalanceMinorUnits)}</td><td><CoinCounts counts={row.coinCounts} reference={ledger.coinConfigurations?.[row.id]} /></td>
                <td>{phoneTime(row.terminalTime)}</td><td>{hostTime(row.receivedAtUtc)}</td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      )}
    </section>
  )
}
