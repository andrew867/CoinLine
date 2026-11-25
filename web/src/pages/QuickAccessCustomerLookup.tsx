import { useEffect, useRef, useState } from 'react'
import { apiFetch } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'

export const quickAccessCustomerEndpoint = '/api/quick-access-customers'
export type CustomerReference = { reference: string; name: string }
export type QuickAccessCustomerSummary = CustomerReference & { id: string; revision: number; archived: boolean }
type SearchPage<T> = { rows: T[]; total: number; page: number; pageSize: number }
type SearchRequest = { query: string; page: number; sort: 'reference' | 'name'; includeArchived: boolean; select?: 'first' | 'last' }

export async function quickAccessCustomerResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const data = await response.json().catch(() => null) as { error?: string } | null
    throw new Error(data?.error ?? `Customer directory request failed (HTTP ${response.status}).`)
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export function CustomerReferenceSearch<T extends CustomerReference>({ mode, selectedReference, includeArchived = false, refresh = 0,
  onSelect, disabled = false }: { mode: 'search' | 'lookup'; selectedReference?: string; includeArchived?: boolean; refresh?: number
  onSelect: (entry: T) => void; disabled?: boolean }) {
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState<'reference' | 'name'>('reference')
  const [request, setRequest] = useState<SearchRequest>({ query: '', page: 1, sort: 'reference', includeArchived })
  const [result, setResult] = useState<SearchPage<T> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const selectRef = useRef(onSelect)
  const selectedRequest = useRef<SearchRequest | null>(null)
  selectRef.current = onSelect
  useEffect(() => {
    const controller = new AbortController()
    setLoading(true); setError('')
    const { select, ...body } = request
    void apiFetch(`${quickAccessCustomerEndpoint}/${mode}`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: controller.signal,
      body: JSON.stringify({ ...body, includeArchived, page: request.includeArchived === includeArchived ? request.page : 1 }),
    }).then(quickAccessCustomerResponse<SearchPage<T>>).then(data => {
      if (controller.signal.aborted) return
      setResult(data)
      const row = select === 'first' ? data.rows[0] : select === 'last' ? data.rows.at(-1) : undefined
      if (row && selectedRequest.current !== request && request.includeArchived === includeArchived) {
        selectedRequest.current = request
        selectRef.current(row)
      }
    }).catch(err => {
      if (!controller.signal.aborted) { setResult(null); setError(err instanceof Error ? err.message : 'Could not search customers.') }
    }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [mode, request, refresh, includeArchived])
  const pages = result ? Math.max(1, Math.ceil(result.total / result.pageSize)) : 1
  const index = result?.rows.findIndex(row => row.reference === selectedReference) ?? -1
  const search = () => setRequest({ query: query.trim(), page: 1, sort, includeArchived })
  const go = (page: number, select?: SearchRequest['select']) => setRequest({ ...request, page, includeArchived, select })
  const move = (direction: -1 | 1) => {
    if (!result) return
    const row = result.rows[index + direction]
    if (row) onSelect(row)
    else if (result.page + direction >= 1 && result.page + direction <= pages)
      go(result.page + direction, direction === 1 ? 'first' : 'last')
  }
  const unavailable = disabled || loading
  return <div className="table-config-panel">
    <div className="table-config-selectors">
      <label>Customer reference or name<input type="search" value={query} maxLength={160} disabled={disabled}
        onChange={event => setQuery(event.target.value)} onKeyDown={event => {
          if (event.key === 'Enter') { event.preventDefault(); if (!unavailable) search() }
        }} /></label>
      <label>Sort customers by<select value={sort} disabled={disabled} onChange={event => setSort(event.target.value as typeof sort)}>
        <option value="reference">Reference</option><option value="name">Name</option>
      </select></label>
      <button type="button" disabled={unavailable} onClick={search}>Search customers</button>
    </div>
    {error && <p role="alert">{error}</p>}
    {loading && <p role="status">Loading customers…</p>}
    {result && !loading && <><p>{result.total} {result.total === 1 ? 'customer' : 'customers'} found.</p>
      {result.rows.length > 0 && <div className="table-set-versions"><table><thead><tr><th>Reference</th><th>Customer name</th>
        {mode === 'search' && <th>Status</th>}</tr></thead>
        <tbody>{result.rows.map(row => <tr key={row.reference}>
          <td><button type="button" className="quiet" aria-pressed={selectedReference === row.reference} disabled={disabled}
            onClick={() => onSelect(row)}>{mode === 'lookup' ? 'Use ' : ''}{row.reference}</button></td><td>{row.name}</td>
          {mode === 'search' && <td>{'archived' in row && row.archived ? 'Archived' : 'Active'}</td>}
        </tr>)}</tbody></table></div>}
      <div className="row-actions">
        <button type="button" className="quiet" disabled={unavailable || result.page <= 1} onClick={() => go(result.page - 1)}>Previous page</button>
        <span>Page {result.page} of {pages}</span>
        <button type="button" className="quiet" disabled={unavailable || result.page >= pages} onClick={() => go(result.page + 1)}>Next page</button>
        {mode === 'search' && <>
          <button type="button" className="quiet" disabled={unavailable || index < 0 || (index === 0 && result.page <= 1)} onClick={() => move(-1)}>Previous customer</button>
          <button type="button" className="quiet" disabled={unavailable || index < 0 || (index === result.rows.length - 1 && result.page >= pages)} onClick={() => move(1)}>Next customer</button>
        </>}
      </div>
    </>}
  </div>
}

export function QuickAccessCustomerLookup({ value, onSelect }: { value: string; onSelect: (value: string) => void }) {
  const [open, setOpen] = useState(false)
  const canRead = canPerform('quick-access-customers', op.read)
  const canLookup = canRead || canPerform('quick-access-customer-lookup', op.read)
  if (!canLookup) return null
  const numeric = Number(value)
  const reference = Number.isInteger(numeric) && numeric > 0 && numeric <= 999999 ? String(numeric).padStart(6, '0') : undefined
  return <div className="quick-access-customer-lookup">
    <div className="row-actions">
      <button type="button" className="quiet" onClick={() => setOpen(old => !old)} aria-expanded={open}>{open ? 'Close customer lookup' : 'Look up customer'}</button>
      {numeric > 0 && <button type="button" className="quiet" onClick={() => { onSelect('0'); setOpen(false) }}>Clear customer reference</button>}
      {canRead && <a href="/quick-access-customers" target="_blank" rel="noreferrer">Manage customer directory</a>}
    </div>
    {!reference && numeric > 0 && <p>This imported reference is outside the six-digit directory. It is preserved until changed.</p>}
    {open && <CustomerReferenceSearch mode="lookup" selectedReference={reference} onSelect={row => {
      onSelect(String(Number(row.reference))); setOpen(false)
    }} />}
  </div>
}
