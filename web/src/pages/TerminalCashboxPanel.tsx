import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPostRaw } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
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
  initializations?: { id: string; recordedAtUtc: string; boxNumber: string; openingTime: string; isCurrent: boolean }[]
  canInitializeEmpty?: boolean
  expectedNewBoxNumber?: string | null
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
  const [localRefresh, setLocalRefresh] = useState(0)
  const [boxNumber, setBoxNumber] = useState('')
  const [openingClock, setOpeningClock] = useState('')
  const [confirmEmpty, setConfirmEmpty] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState('')

  useEffect(() => {
    let current = true
    setLedger(null)
    setError('')
    void apiGet<Ledger>(`/api/terminals/${terminalId}/cashbox`)
      .then(result => { if (current) setLedger(result) })
      .catch(() => { if (current) setError('Could not load cashbox reports.') })
    return () => { current = false }
  }, [terminalId, refresh, localRefresh])

  useEffect(() => { setBoxNumber(''); setOpeningClock(''); setConfirmEmpty(false); setSaveError('') }, [terminalId])

  const current = ledger?.statuses.find(row => row.isCurrent)
  const initialized = ledger?.initializations?.find(row => row.isCurrent)
  const hasHistory = ledger && (ledger.statuses.length > 0 || ledger.collections.length > 0)
  const actualBox = ledger?.expectedNewBoxNumber ?? boxNumber
  const wholeSecondClock = openingClock.replace(/\.0+$/, '')
  const normalizedClock = wholeSecondClock.length === 16 ? `${wholeSecondClock}:00` : wholeSecondClock
  async function initializeEmpty() {
    setSaving(true); setSaveError('')
    try {
      const response = await apiPostRaw(`/api/terminals/${terminalId}/cashbox/initialize-empty`, {
        boxNumber: actualBox.toUpperCase(), openingClock: normalizedClock, confirmEmpty,
      })
      if (!response.ok) {
        const payload = await response.json().catch(() => null) as { error?: string } | null
        throw new Error(payload?.error ?? `Could not initialize the empty box (${response.status}).`)
      }
      setBoxNumber(''); setOpeningClock(''); setConfirmEmpty(false); setLocalRefresh(value => value + 1)
    } catch (err) { setSaveError(err instanceof Error ? err.message : 'Could not initialize the empty box.') }
    finally { setSaving(false) }
  }
  return (
    <section aria-labelledby="cashbox-heading">
      <h2 id="cashbox-heading">Cashbox</h2>
      {error && <ErrorBanner message={error} />}
      {!ledger && !error && <LoadingBlock label="Loading cashbox reports…" />}
      {ledger && ledger.statuses.length === 0 && ledger.collections.length === 0 && <>
        <EmptyHint>No cashbox reports yet.</EmptyHint>
        {!initialized && <div className="cashbox-waiting" role="note">
          <strong>Current balance unknown.</strong> The manager has not received a cashbox status (MT38) or collection (MT51) report from this phone.
          A successfully uploaded coin call can still exist without a cashbox report. Its charge and collected amount do not establish the current box balance.
        </div>}
      </>}
      {initialized && <section className="cashbox-summary" aria-label="Operator-confirmed empty box">
        <h3>Operator-confirmed empty-box initialization</h3>
        <p>Starting balance 0.00 for box <code>{initialized.boxNumber}</code>, opened at {phoneTime(initialized.openingTime)}.</p>
        <p>Recorded {hostTime(initialized.recordedAtUtc)}. This is an operator assertion, not a terminal report or collection. It does not establish the present cashbox balance.</p>
      </section>}
      {ledger?.canInitializeEmpty && canPerform('cashbox', op.add) && <section className="cashbox-summary" aria-label="Initialize observed empty box">
        <h3>Initialize a known empty box</h3>
        <p>Only use this after physically verifying the new box is empty and observing its actual identifier and opening time on the phone. Do not estimate from a quote or CDR.</p>
        <form onSubmit={event => { event.preventDefault(); void initializeEmpty() }}>
          <label>Box identifier (8 hexadecimal digits) <input value={actualBox} readOnly={!!ledger.expectedNewBoxNumber} maxLength={8}
            pattern="[0-9A-Fa-f]{8}" required onChange={event => setBoxNumber(event.target.value)} /></label>{' '}
          <label>Observed phone opening clock <input type="datetime-local" step="1" value={openingClock} required onChange={event => setOpeningClock(event.target.value)} /></label>
          <p><label><input type="checkbox" checked={confirmEmpty} onChange={event => setConfirmEmpty(event.target.checked)} /> I personally verified the box is empty and these observed values are correct.</label></p>
          <button type="submit" disabled={saving || !confirmEmpty || !/^[0-9A-Fa-f]{8}$/.test(actualBox) || !openingClock}>{saving ? 'Recording…' : 'Record empty-box initialization'}</button>
          {saveError && <ErrorBanner message={saveError} />}
        </form>
      </section>}
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
      {hasHistory && !current && !initialized && <p className="cashbox-waiting" role="note">No current box status is available. The last reported collection does not establish the value or condition of the replacement box.</p>}
      {!!ledger?.initializations?.length && <table>
        <caption>Operator-entered box initializations</caption>
        <thead><tr><th>Box</th><th>Observed opening clock</th><th>Recorded (UTC)</th><th>Status</th></tr></thead>
        <tbody>{ledger.initializations.map(row => <tr key={row.id}><td><code>{row.boxNumber}</code></td><td>{phoneTime(row.openingTime)}</td>
          <td>{hostTime(row.recordedAtUtc)}</td><td>{row.isCurrent ? 'Current initialization' : 'Superseded'}</td></tr>)}</tbody>
      </table>}
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
