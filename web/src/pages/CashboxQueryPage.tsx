import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { apiGet } from '../api/client'
import { ErrorBanner, LoadingBlock } from '../components/AppStates'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { TerminalCashboxPanel } from './TerminalCashboxPanel'
import './CashboxQueryPage.css'

type Phone = { id: string; terminalIdHex: string; displayName: string }
type Options = { phones: Phone[]; total: number; page: number; pageSize: number }
export function CashboxQueryLink({ terminalId }: { terminalId: string }) {
  return canPerform('cashbox', op.read) ? <Link className="cashbox-query-link" to={`/cashbox?terminalId=${encodeURIComponent(terminalId)}`}>Query cashbox</Link> : null
}

export function CashboxQueryPage() {
  const [params] = useSearchParams()
  const locked = params.get('terminalId') ?? ''
  const [search, setSearch] = useState('')
  const [submitted, setSubmitted] = useState('')
  const [page, setPage] = useState(1)
  const [pageIntent, setPageIntent] = useState<'first' | 'last'>('first')
  const [searchRevision, setSearchRevision] = useState(0)
  const [options, setOptions] = useState<Options | null>(null)
  const [selected, setSelected] = useState('')
  const [refresh, setRefresh] = useState(0)
  const [error, setError] = useState('')
  useEffect(() => {
    let current = true
    setOptions(null)
    setSelected('')
    setError('')
    const endpoint = locked ? `/api/cashbox/phones?terminalId=${encodeURIComponent(locked)}`
      : `/api/cashbox/phones?query=${encodeURIComponent(submitted)}&page=${page}`
    void apiGet<Options>(endpoint).then(result => {
      if (!current) return
      setOptions(result)
      setSelected((pageIntent === 'last' && !locked ? result.phones.at(-1) : result.phones[0])?.id ?? '')
    }).catch(() => { if (current) setError('Could not find phones for the cashbox query.') })
    return () => { current = false }
  }, [locked, submitted, page, pageIntent, searchRevision])
  const index = options?.phones.findIndex(phone => phone.id === selected) ?? -1
  const phone = index >= 0 ? options?.phones[index] : undefined
  const position = options && index >= 0 ? (options.page - 1) * options.pageSize + index + 1 : 0
  function move(direction: -1 | 1) {
    if (!options || index < 0 || locked) return
    const next = index + direction
    if (next >= 0 && next < options.phones.length) setSelected(options.phones[next].id)
    else if (direction < 0 && options.page > 1) { setPageIntent('last'); setPage(options.page - 1) }
    else if (direction > 0 && position < options.total) { setPageIntent('first'); setPage(options.page + 1) }
  }
  return <div className="cashbox-query-page">
    <header><h1>Cashbox status</h1>
      <p className="page-intro">Read the last reported box condition, contents and collection history. This view does not contact a phone, clear its cashbox or change its records.</p></header>
    <section className="cashbox-query-controls" aria-labelledby="cashbox-query-heading">
      <h2 id="cashbox-query-heading">{locked ? 'Selected phone' : 'Find a phone'}</h2>
      {locked ? <div className="row-actions"><span>Query limited to the phone selected from Terminal status.</span> <Link to="/cashbox">Browse all cashboxes</Link></div>
        : <form className="row-actions" onSubmit={event => {
          event.preventDefault(); setSubmitted(search.trim()); setPage(1); setPageIntent('first'); setSearchRevision(value => value + 1)
        }}><label>Search by phone ID or name<input type="search" value={search} maxLength={100} onChange={event => setSearch(event.target.value)} /></label>
          <button type="submit">Find phones</button></form>}
      {error && <ErrorBanner message={error} />}
      {!options && !error && <LoadingBlock label="Finding phones…" />}
      {options && !options.phones.length && <p>No matching phones.</p>}
      {options && options.phones.length > 0 && <>
        {!locked && <div className="row-actions cashbox-find-nav">
          <label>Phone<select value={selected} onChange={event => setSelected(event.target.value)}>
            {options.phones.map(row => <option key={row.id} value={row.id}>{row.displayName} ({row.terminalIdHex})</option>)}
          </select></label>
          <button type="button" onClick={() => move(-1)} disabled={position <= 1}>Find previous</button>
          <button type="button" onClick={() => move(1)} disabled={position >= options.total}>Find next</button>
          <span role="status">Phone {position} of {options.total}, ordered by wire ID</span>
        </div>}
        {phone && <div className="cashbox-phone"><div><strong>{phone.displayName}</strong><span>{phone.terminalIdHex}</span></div>
          <button type="button" onClick={() => setRefresh(value => value + 1)}>Refresh reports</button></div>}
      </>}
    </section>
    {phone && <section className="cashbox-query-ledger" aria-label="Cashbox ledger"><TerminalCashboxPanel key={phone.id} terminalId={phone.id} refresh={refresh} /></section>}
  </div>
}
