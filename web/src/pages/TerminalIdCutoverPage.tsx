import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, apiGet } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import './TableSetConfiguration.css'

const scopes = {
  all: 'All phones', terminal: 'Individual phone', group: 'Phone group',
  site: 'Location', customer: 'Customer', region: 'Region', 'time-area': 'Time area',
  'group-pattern': 'Group ID pattern',
} as const
type Scope = keyof typeof scopes
type ScopeOption = { id: string; name?: string; displayName?: string; code?: string }
type Target = {
  id: string; displayName: string; currentId: string; replacementId: string | null
  tableSetId: string | null; tableSetName: string | null; tableSetRevision: number | null
  accessRevision: number | null; accessLength: number | null
  sourceHash: string | null; replacementHash: string | null
  eligible: boolean; issues: string[]
}
type Preview = { targets: Target[]; eligibleCount: number; blockedCount: number; scheduled: false }

export function canReviewTerminalIdCutover() {
  return canPerform('phones', op.read) && canPerform('table-assignments', op.read) && canPerform('table-records', op.read)
}

export function TerminalIdCutoverPage() {
  const allowed = canReviewTerminalIdCutover()
  const [source, setSource] = useState('')
  const [replacement, setReplacement] = useState('')
  const [scope, setScope] = useState<Scope>('all')
  const [scopeValue, setScopeValue] = useState('')
  const [options, setOptions] = useState<ScopeOption[]>([])
  const [scopeError, setScopeError] = useState('')
  const [preview, setPreview] = useState<Preview | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const needsOptions = scope !== 'all' && scope !== 'group-pattern'

  useEffect(() => {
    if (!allowed || !needsOptions) return
    let active = true
    void apiGet<ScopeOption[]>('/api/tables/scope-options?targetType=' + scope)
      .then(data => { if (active) setOptions(data) })
      .catch(() => { if (active) setScopeError('Could not load inventory scope options.') })
    return () => { active = false }
  }, [allowed, needsOptions, scope])

  const valid = /^(?:[0-9]{6}|[0-9]{10})$/.test(source) &&
    /^[0-9]+$/.test(replacement) && replacement.length === source.length && replacement !== source &&
    (scope === 'all' || Boolean(scopeValue))

  function clearPreview() { setPreview(null); setError('') }

  async function review(event: FormEvent) {
    event.preventDefault()
    if (!valid || !allowed || busy) return
    clearPreview()
    setBusy(true)
    try {
      const query = new URLSearchParams({ source, replacement, scope })
      if (scope !== 'all') {
        const field = scope === 'region' ? 'regionCode' : scope === 'time-area' ? 'timeAreaCode'
          : scope === 'group-pattern' ? 'groupPattern' : 'targetId'
        query.set(field, scopeValue)
      }
      const response = await apiFetch('/api/terminals/identity-cutover/preview?' + query)
      if (!response.ok) {
        const detail = await response.json().catch(() => null)
        throw new Error(detail?.error || 'Could not review terminal IDs (HTTP ' + response.status + ').')
      }
      setPreview(await response.json() as Preview)
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Could not review terminal IDs.')
    } finally {
      setBusy(false)
    }
  }

  return <div>
    <p><Link to="/terminals">Back to phones</Link></p>
    <h1>Terminal ID cutover review</h1>
    <p className="page-intro">Review an exact change or an NPA-NXX prefix change across inventory. The last four digits remain unchanged for prefix changes.</p>
    {!allowed ? <p>Read permission for Phones, Table assignments and Table records is required.</p> : <>
      <section className="table-config-panel" aria-label="Cutover mapping">
        <h2>Proposed identity change</h2>
        <p>This is a read-only review. It does not reserve a new ID, schedule a download, edit inventory or change the phone.</p>
        <form className="table-config-selectors" onSubmit={event => void review(event)}>
          <label>Existing ID or prefix<input value={source} inputMode="numeric" maxLength={10} disabled={busy}
            onChange={event => { setSource(event.target.value); clearPreview() }} /></label>
          <label>Replacement ID or prefix<input value={replacement} inputMode="numeric" maxLength={10} disabled={busy}
            onChange={event => { setReplacement(event.target.value); clearPreview() }} /></label>
          <label>Inventory scope<select value={scope} disabled={busy} onChange={event => {
            setScope(event.target.value as Scope); setScopeValue(''); setOptions([]); setScopeError(''); clearPreview()
          }}>{Object.entries(scopes).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
          {needsOptions && <label>{scopes[scope]}<select value={scopeValue} disabled={busy}
            onChange={event => { setScopeValue(event.target.value); clearPreview() }}>
            <option value="">Choose {scopes[scope].toLowerCase()}</option>
            {options.map(option => <option key={option.id} value={option.id}>
              {option.name ?? option.displayName ?? option.id}{option.code ? ' (' + option.code + ')' : ''}
            </option>)}
          </select></label>}
          {scope === 'group-pattern' && <label>Group ID pattern<input value={scopeValue} maxLength={6} disabled={busy}
            onChange={event => { setScopeValue(event.target.value); clearPreview() }} /></label>}
          <button disabled={!valid || busy}>{busy ? 'Reviewing…' : 'Preview cutover'}</button>
        </form>
        {scopeError && <p role="alert">{scopeError}</p>}
        {error && <p role="alert">{error}</p>}
      </section>
      {preview && <section className="table-config-panel" aria-label="Cutover review results">
        <h2>Inventory and MT21 review</h2>
        <p role="status">{preview.eligibleCount} eligible, {preview.blockedCount} blocked. Nothing scheduled or changed.</p>
        {!preview.targets.length ? <p>No phones match this mapping and inventory scope.</p> : <div className="table-scroll"><table>
          <thead><tr><th>Phone</th><th>Current ID</th><th>Proposed ID</th><th>Published assignment / MT21</th><th>Review</th></tr></thead>
          <tbody>{preview.targets.map(row => <tr key={row.id}>
            <td><Link to={'/terminals/' + row.id}>{row.displayName}</Link></td>
            <td><code>{row.currentId}</code></td><td><code>{row.replacementId ?? 'Not mappable'}</code></td>
            <td>{row.tableSetId && row.tableSetName ? <Link to={'/table-sets/' + row.tableSetId}>{row.tableSetName}</Link> : 'Unassigned'}
              {row.accessRevision != null && <p>MT21 revision {row.accessRevision}, {row.accessLength} bytes</p>}
              {row.sourceHash && row.replacementHash && <details><summary>Record fingerprints</summary>
                <p>Current <code>{row.sourceHash}</code></p><p>Proposed <code>{row.replacementHash}</code></p>
              </details>}
            </td>
            <td>{row.eligible ? <strong>Eligible for planning</strong> : <><strong>Blocked</strong><ul>
              {row.issues.map(issue => <li key={issue}>{issue}</li>)}</ul></>}</td>
          </tr>)}</tbody>
        </table></div>}
        <p>Eligibility applies only to this inventory snapshot. Coordination and confirmation of a new NCC identity are required before an actual cutover.</p>
      </section>}
    </>}
  </div>
}
