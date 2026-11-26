import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiPostRaw } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'

type Action = 'add' | 'delete'
type Target = { setId: string; name: string; status: string; reason?: string | null; pattern?: string | null;
  published: boolean; inUse: boolean; assignedPhones?: number | null }
type Preview = { previewToken: string; canApply: boolean; source: string; slot: number; sourcePattern?: string | null;
  changedCount: number; blockedCount: number; updatedDrafts: number; replacementDrafts: number; targets: Target[] }
type Result = { affectedCount: number; configurations: { sourceSetId: string; setId: string; name: string; replacement: boolean; revision: number }[] }

const statuses: Record<string, string> = { source: 'Source entry', excluded: 'Not in scope', blocked: 'Blocked', unchanged: 'Already vacant',
  'replacement-draft': 'Prepare replacement draft', 'update-draft': 'Update draft' }

export function ScreeningBulkPanel({ setId, number, revision, sha256, profile, slot, dirty, onApplied }: {
  setId: string; number: number; revision: number; sha256: string; profile: string; slot: number; dirty: boolean
  onApplied: (sourceChanged: boolean) => void
}) {
  const [action, setAction] = useState<Action>('add')
  const [includePublished, setIncludePublished] = useState(true)
  const [customerScope, setCustomerScope] = useState<'source' | 'all'>('source')
  const [preview, setPreview] = useState<Preview | null>(null)
  const [result, setResult] = useState<Result | null>(null)
  const [confirmed, setConfirmed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const endpoint = `/api/tables/sets/${setId}/records/${number}/screening-bulk`
  const mayOpenSets = canPerform('table-sets', op.read)

  useEffect(() => { setPreview(null); setConfirmed(false) }, [revision, sha256, profile, dirty])

  function clearPreview() { setPreview(null); setResult(null); setConfirmed(false); setError('') }
  async function run(apply: boolean) {
    if (dirty || busy || (apply && (!preview?.canApply || !confirmed))) return
    setBusy(true); setError(''); setResult(null)
    if (!apply) { setPreview(null); setConfirmed(false) }
    try {
      const response = await apiPostRaw(`${endpoint}${apply ? '' : '/preview'}`,
        { revision, sha256, profile, slot, action, includePublished, customerScope, previewToken: apply ? preview?.previewToken : undefined })
      const data = await response.json().catch(() => null)
      if (!response.ok) {
        if (response.status === 409) { setPreview(null); setConfirmed(false) }
        throw new Error(data?.error ?? `Could not ${apply ? 'apply' : 'preview'} the screening change.`)
      }
      if (!apply) setPreview(data as Preview)
      else {
        const applied = data as Result
        setResult(applied); setPreview(null); setConfirmed(false)
        onApplied(applied.configurations.some(row => row.setId === setId))
      }
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not complete the screening operation.') }
    finally { setBusy(false) }
  }

  return <details className="table-config-preview screening-bulk-panel">
    <summary>Change screening slot {slot} across configurations</summary>
    <p>Work on the same saved slot in configurations with this exact wire layout. Published configurations are never edited in place. No phone assignment or download is changed.</p>
    {dirty && <p role="note">Save or discard pending record edits before previewing a bulk operation.</p>}
    <div className="table-config-selectors">
      <label>Bulk screening operation<select value={action} disabled={busy} onChange={event => { setAction(event.target.value as Action); clearPreview() }}>
        <option value="add">Copy saved entry into vacant slots</option><option value="delete">Delete this slot across configurations</option></select></label>
      <label>Configuration scope<select value={includePublished ? 'all' : 'drafts'} disabled={busy} onChange={event => { setIncludePublished(event.target.value === 'all'); clearPreview() }}>
        <option value="all">All compatible configurations</option><option value="drafts">Editable drafts only</option></select></label>
      <label>Ownership scope<select value={customerScope} disabled={busy} onChange={event => { setCustomerScope(event.target.value as 'source' | 'all'); clearPreview() }}>
        <option value="source">Same customer ownership as source</option><option value="all">All customers and shared configurations</option></select></label>
      <button type="button" disabled={dirty || busy} onClick={() => void run(false)}>Preview bulk screening change</button>
    </div>
    {error && <p role="alert">{error}</p>}
    {preview && <section aria-label="Bulk screening preview">
      <h3>Preview for slot {preview.slot}</h3>
      <p>{preview.updatedDrafts} existing drafts to update; {preview.replacementDrafts} replacement drafts to prepare; {preview.blockedCount} blocked.</p>
      {action === 'add' && <p>Saved source: {preview.source}{preview.sourcePattern ? ` (${preview.sourcePattern})` : ''}.</p>}
      {action === 'add' && <p>Applicable slots copy assigned entry fields and description, retaining each destination’s reserved bits. Review local rate and carrier references before publishing a draft.</p>}
      {preview.blockedCount > 0 && <p role="alert">No configuration can be changed until every blocking entry is resolved.</p>}
      <div style={{ overflowX: 'auto' }}><table><caption>Matching configurations and exclusions</caption>
        <thead><tr><th>Configuration</th><th>Current pattern</th><th>Result</th><th>Details</th></tr></thead>
        <tbody>{preview.targets.map(target => <tr key={target.setId}>
          <td>{mayOpenSets ? <Link to={`/table-sets/${target.setId}?record=${number}`}>{target.name}</Link> : target.name}
            <small> {target.published ? 'Published' : 'Draft'}{target.assignedPhones != null && ` · ${target.assignedPhones} assigned phones`}</small></td>
          <td>{target.pattern || '—'}</td><td>{statuses[target.status] ?? target.status}</td><td>{target.reason ?? (target.inUse ? 'Original configuration remains in use.' : '')}</td>
        </tr>)}</tbody>
      </table></div>
      {preview.canApply && <div className="row-actions">
        <label><input type="checkbox" checked={confirmed} disabled={dirty || busy} onChange={event => setConfirmed(event.target.checked)} /> I reviewed all {preview.changedCount} changes.</label>
        <button type="button" disabled={dirty || busy || !confirmed} onClick={() => void run(true)}>Apply bulk screening change</button>
      </div>}
    </section>}
    {result && <section aria-label="Bulk screening result">
      <p role="status">Updated or prepared {result.affectedCount} draft configurations. No phones or downloads were changed.</p>
      <ul>{result.configurations.map(row => <li key={row.setId}>{mayOpenSets ? <Link to={`/table-sets/${row.setId}?record=${number}`}>{row.name}</Link> : row.name}
        {` (revision ${row.revision}${row.replacement ? ', replacement draft' : ''})`}</li>)}</ul>
    </section>}
  </details>
}
