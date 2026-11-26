import { useEffect, useState } from 'react'
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
}

function amount(minorUnits: number): string {
  return (minorUnits / 100).toFixed(2)
}

function counts(coinCounts: number[]): string {
  return coinCounts.flatMap((count, index) => count ? [`Slot ${index + 1}: ${count}`] : []).join(', ') || 'None'
}

const hostTime = (value: string) => new Date(value).toISOString().replace('T', ' ').replace(/\.\d{3}Z$/, ' UTC')
const phoneTime = (value?: string | null) => value ? `${value.replace('T', ' ')} (phone clock)` : 'Not available'

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
      {ledger && ledger.statuses.length === 0 && ledger.collections.length === 0 && <EmptyHint>No cashbox reports yet.</EmptyHint>}
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
        <p>Reported coin counts by slot: {counts(current.status.coinCounts)}.</p>
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
                <td>{counts(row.status.coinCounts)}</td><td>{phoneTime(row.status.terminalTime)}</td><td>{hostTime(row.receivedAtUtc)}</td>
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
                <td>{amount(row.removedBoxBalanceMinorUnits)}</td><td>{counts(row.coinCounts)}</td>
                <td>{phoneTime(row.terminalTime)}</td><td>{hostTime(row.receivedAtUtc)}</td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      )}
    </section>
  )
}
