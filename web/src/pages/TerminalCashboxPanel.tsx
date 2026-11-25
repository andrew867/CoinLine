import { useEffect, useState } from 'react'
import { apiGet } from '../api/client'
import { EmptyHint, ErrorBanner, LoadingBlock } from '../components/AppStates'

type Status = {
  boxNumber: string
  percentFull: number
  balanceMinorUnits: number
  coinCounts: number[]
}

type Ledger = {
  statuses: { id: string; receivedAtUtc: string; status: Status; isCurrent: boolean }[]
  collections: {
    id: string
    receivedAtUtc: string
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

export function TerminalCashboxPanel({ terminalId }: { terminalId: string }) {
  const [ledger, setLedger] = useState<Ledger | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    let current = true
    void apiGet<Ledger>(`/api/terminals/${terminalId}/cashbox`)
      .then(result => { if (current) setLedger(result) })
      .catch(() => { if (current) setError('Could not load cashbox reports.') })
    return () => { current = false }
  }, [terminalId])

  return (
    <section aria-labelledby="cashbox-heading">
      <h2 id="cashbox-heading">Cashbox</h2>
      {error && <ErrorBanner message={error} />}
      {!ledger && !error && <LoadingBlock label="Loading cashbox reports…" />}
      {ledger && ledger.statuses.length === 0 && ledger.collections.length === 0 && <EmptyHint>No cashbox reports yet.</EmptyHint>}
      {ledger && (ledger.statuses.length > 0 || ledger.collections.length > 0) && <p>Amounts use the phone’s currency.</p>}
      {ledger && ledger.statuses.length > 0 && (
        <div style={{ overflowX: 'auto' }}>
          <table>
            <caption>Latest status by box</caption>
            <thead><tr><th>Box</th><th>Status</th><th>Balance</th><th>Full</th><th>Coin counts</th><th>Received</th></tr></thead>
            <tbody>{ledger.statuses.map(row => (
              <tr key={row.id}>
                <td><code>{row.status.boxNumber}</code></td><td>{row.isCurrent ? 'Current' : 'Previous'}</td>
                <td>{amount(row.status.balanceMinorUnits)}</td><td>{row.status.percentFull}%</td>
                <td>{counts(row.status.coinCounts)}</td><td>{new Date(row.receivedAtUtc).toLocaleString()}</td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      )}
      {ledger && ledger.collections.length > 0 && (
        <div style={{ overflowX: 'auto' }}>
          <table>
            <caption>Reported collections</caption>
            <thead><tr><th>Removed box</th><th>New box</th><th>Reported total</th><th>Removed box balance</th><th>Removed box coins</th><th>Received</th></tr></thead>
            <tbody>{ledger.collections.map(row => (
              <tr key={row.id}>
                <td><code>{row.boxNumber}</code></td><td><code>{row.newBoxNumber}</code></td>
                <td>{amount(row.totalMinorUnits)}{row.isSummed && ' (combined)'}</td>
                <td>{amount(row.removedBoxBalanceMinorUnits)}</td><td>{counts(row.coinCounts)}</td>
                <td>{new Date(row.receivedAtUtc).toLocaleString()}</td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      )}
    </section>
  )
}
