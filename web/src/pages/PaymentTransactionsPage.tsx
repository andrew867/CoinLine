import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { StatusBadge } from '../components/StatusBadge'
import './TableSetConfiguration.css'

type Row = {
  id: string; cardAccountId: string; amount: number; reconciliation: number; reportedCardType: number; createdAtUtc: string; source?: string
}
type Attempt = {
  id: string; terminalId: string | null; terminalDisplayName: string | null; nccSessionId: string | null
  createdAtUtc: string; messageType: number; outcome: string; provider: string; reason: string | null
  cardAccountId: string | null; quoteId: string | null; quotedAmountUsd: number | null; callSequence: number | null
  requestBytes?: number | null; bundledCallRecords?: number | null; suffixCdrTypes?: number[] | null
}

const outcomes: Record<string, string> = {
  approved: 'Approved', declined: 'Declined', provider_unavailable: 'No provider',
  provider_invalid_decision: 'Invalid provider decision', provider_failed: 'Provider failed',
  provider_timeout: 'Provider timed out', provider_http_error: 'Provider HTTP error',
  audit_failed: 'Decision could not be recorded', malformed_request: 'Malformed request', unknown: 'Outcome unavailable',
}
const providers: Record<string, string> = {
  synthetic_lab: 'Synthetic lab', trusted_provider: 'Trusted provider', none: 'Decline only', unrecorded: 'Not recorded',
}
const reasons: Record<string, string> = {
  fixture_not_recognized: 'Test fixture not recognized', binding_not_found: 'No matching phone and test route binding',
  account_not_eligible: 'Account is not an eligible test account', quote_missing: 'No recent matching quote',
  quote_not_eligible: 'Quote is not eligible', insufficient_balance: 'Insufficient test balance',
  quote_already_used: 'Quote already has another authorization decision', approved: 'Eligible test fixture and quote',
}
const cardTypes: Record<number, string> = { 0: 'Unknown', 1: 'Magstripe', 2: 'Smart card', 3: 'E-purse', 4: 'Mondex', 5: 'SmartCity', 6: 'Proton' }
const reconciliations: Record<number, string> = { 0: 'Unknown', 1: 'Pending', 2: 'Matched', 3: 'Exception' }

export function PaymentTransactionsPage() {
  const [rows, setRows] = useState<Row[] | null>(null)
  const [attempts, setAttempts] = useState<Attempt[] | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [attemptError, setAttemptError] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [filter, setFilter] = useState('all')
  const canReadPhones = canPerform('phones', op.read)
  const canReadAccounts = canPerform('card-accounts', op.read)

  useEffect(() => {
    let active = true
    setRows(null); setAttempts(null); setErr(null); setAttemptError(null)
    void apiGet<Row[]>('/api/cards/transactions')
      .then(value => { if (active) setRows(value) })
      .catch(error => { if (active) setErr(error instanceof Error ? error.message : 'Could not load payment history.') })
    void apiGet<Attempt[]>('/api/cards/authorization-attempts')
      .then(value => { if (active) setAttempts(value) })
      .catch(error => { if (active) setAttemptError(error instanceof Error ? error.message : 'Could not load authorization history.') })
    return () => { active = false }
  }, [refresh])

  const visibleAttempts = attempts?.filter(row => filter === 'all'
    || (filter === 'approved' && row.outcome === 'approved')
    || (filter === 'not-approved' && row.outcome !== 'approved' && row.outcome !== 'unknown')
    || (filter === 'unknown' && row.outcome === 'unknown')) ?? []

  return <div>
    <h1>Payment transactions</h1>
    <p className="page-intro">Authorization decisions and payment ledger entries are separate. An approval does not mean a call connected,
      a card was charged, or a payment was settled.</p>
    <button type="button" onClick={() => setRefresh(value => value + 1)}>Refresh payment history</button>
    <section className="table-config-panel" aria-labelledby="authorization-history-heading" style={{ marginTop: 18 }}>
      <h2 id="authorization-history-heading">Card authorization attempts</h2>
      <p>Redacted MT1 and MT2 requests from phones, including declines and older observations without a recorded decision.</p>
      <label>Authorization result
        <select value={filter} onChange={event => setFilter(event.target.value)} style={{ display: 'block' }}>
          <option value="all">All results</option><option value="approved">Approved</option>
          <option value="not-approved">Not approved</option><option value="unknown">Outcome unavailable</option>
        </select>
      </label>
      {attempts === null && !attemptError && <p role="status">Loading authorization attempts…</p>}
      {attemptError && <p role="alert" style={{ color: 'crimson' }}>Could not load authorization history: {attemptError}</p>}
      {attempts !== null && !attemptError && visibleAttempts.length === 0 &&
        <p role="status">{attempts.length === 0 ? 'No authorization attempts recorded.' : 'No attempts match this result filter.'}</p>}
      {visibleAttempts.length > 0 && <div role="region" aria-label="Authorization history" tabIndex={0} style={{ overflowX: 'auto', marginTop: 12 }}>
        <table cellPadding={8} style={{ borderCollapse: 'collapse', width: '100%' }}>
          <thead><tr><th scope="col">Received</th><th scope="col">Phone / session</th><th scope="col">Request</th>
            <th scope="col">Decision</th><th scope="col">Provider / reason</th><th scope="col">Test account</th><th scope="col">Quote</th></tr></thead>
          <tbody>{visibleAttempts.map(row => <tr key={row.id} style={{ borderBottom: '1px solid #eee' }}>
            <td>{new Date(row.createdAtUtc).toLocaleString()}</td>
            <td>{row.terminalId && canReadPhones
              ? <Link to={`/terminals/${row.terminalId}`}>{row.terminalDisplayName ?? 'Registered phone'}</Link>
              : row.terminalDisplayName ?? 'Unmatched phone'}
              {row.nccSessionId && <div><small>Session {row.nccSessionId}</small></div>}</td>
            <td>MT{row.messageType} {row.messageType === 1 ? 'Formatted' : 'Extended'}{row.callSequence != null && <div><small>Call {row.callSequence}</small></div>}
              {row.requestBytes != null && <div><small>{row.requestBytes} application bytes{row.bundledCallRecords != null && ` · ${row.bundledCallRecords} bundled call ${row.bundledCallRecords === 1 ? 'record' : 'records'}`}</small></div>}
              {row.outcome === 'malformed_request' && row.requestBytes != null && <div><small>Unrecognized request length or trailing record format</small></div>}
              {row.outcome === 'malformed_request' && row.suffixCdrTypes != null && <div><small>Recognized suffix CDR types: {row.suffixCdrTypes.length ? row.suffixCdrTypes.map(type => `MT${type}`).join(', ') : 'None'}</small></div>}</td>
            <td><StatusBadge label={outcomes[row.outcome] ?? 'Outcome unavailable'}
              variant={row.outcome === 'approved' ? 'ok' : row.outcome === 'unknown' ? 'neutral' : 'warn'} /></td>
            <td>{providers[row.provider] ?? 'Not recorded'}
              {row.reason && <div><small>{reasons[row.reason] ?? 'Reason unavailable'}</small></div>}</td>
            <td>{row.cardAccountId ? canReadAccounts
              ? <Link to={`/card-accounts/${row.cardAccountId}`}>{row.cardAccountId}</Link> : row.cardAccountId : 'Not matched'}</td>
            <td>{row.quotedAmountUsd == null ? 'Not recorded' : `USD ${row.quotedAmountUsd.toFixed(2)}`}
              {row.quoteId && <div><small>Quote {row.quoteId}</small></div>}</td>
          </tr>)}</tbody>
        </table>
      </div>}
    </section>
    <section className="table-config-panel" aria-labelledby="payment-ledger-heading">
      <h2 id="payment-ledger-heading">Payment ledger</h2>
      <p>Recorded payment amounts and reconciliation status. Authorizations alone do not add a ledger entry.</p>
      {rows === null && !err && <p role="status">Loading payment transactions…</p>}
      {err && <p role="alert" style={{ color: 'crimson' }}>Could not load payment ledger: {err}</p>}
      {rows !== null && !err && rows.length === 0 && <p role="status">No payment transactions recorded.</p>}
      {!!rows?.length && <div role="region" aria-label="Payment ledger entries" tabIndex={0} style={{ overflowX: 'auto' }}>
        <table cellPadding={8} style={{ borderCollapse: 'collapse', width: '100%' }}>
          <thead><tr><th scope="col">Created</th><th scope="col">Account</th><th scope="col">Amount</th>
            <th scope="col">Card type</th><th scope="col">Source</th><th scope="col">Reconciliation</th></tr></thead>
          <tbody>{rows.map(row => <tr key={row.id} style={{ borderBottom: '1px solid #eee' }}>
            <td>{new Date(row.createdAtUtc).toLocaleString()}</td>
            <td>{canReadAccounts ? <Link to={`/card-accounts/${row.cardAccountId}`}>{row.cardAccountId}</Link> : row.cardAccountId}</td>
            <td>{row.amount.toFixed(2)}</td><td>{cardTypes[row.reportedCardType] ?? 'Unknown'}</td>
            <td>{row.source === 'synthetic_cdr' ? 'Simulated call-detail posting' : 'Not classified'}</td>
            <td>{reconciliations[row.reconciliation] ?? 'Unknown'}</td>
          </tr>)}</tbody>
        </table>
      </div>}
    </section>
  </div>
}
