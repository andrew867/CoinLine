import { useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { cardMasterEndpoint, cardMasterResponse, type CardMasterEntry } from './cardMasterApi'

type Preview = { previewToken: string; eligiblePhones: number; scheduledPhones: number; metadataOnlyPhones: number; skippedPhones: number;
  configurations: { setId: string; name: string; positions: number[]; records: number[]; phones: number; reason: string | null }[] }
type Result = { affectedPhones: number; scheduledPhones: number; metadataOnlyPhones: number; skippedPhones: number;
  sets: { id: string; name: string }[] }

export function CardMasterRolloutPanel({ entry, dirty, onApplied }: { entry: CardMasterEntry; dirty: boolean; onApplied: () => void }) {
  const [preview, setPreview] = useState<Preview | null>(null), [result, setResult] = useState<Result | null>(null)
  const [busy, setBusy] = useState(false), [error, setError] = useState('')
  const canApply = canPerform('card-master', op.update) && canPerform('table-sets', op.add | op.update)
    && canPerform('table-records', op.update) && canPerform('table-assignments', op.update) && canPerform('downloads', op.add)

  async function prepare(apply: boolean) {
    if (dirty || (apply && (!preview || !canApply))) return
    setBusy(true); setError(''); setResult(null)
    try {
      const response = await apiFetch(`${cardMasterEndpoint}/${entry.id}/rollout${apply ? '' : '/preview'}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ revision: entry.revision,
          previewToken: apply ? preview?.previewToken : undefined })
      })
      if (apply) { setResult(await cardMasterResponse<Result>(response)); setPreview(null); onApplied() }
      else setPreview(await cardMasterResponse<Preview>(response))
    } catch (err) { setError(err instanceof Error ? err.message : 'Could not prepare card definition updates.'); setPreview(null) }
    finally { setBusy(false) }
  }

  return <section className="table-config-panel" aria-label="Card definition rollout">
    <h3>Distribute this definition revision</h3>
    <p>Preview phones using an older revision. CoinLine will create new published configuration snapshots, retain their existing target references and prepare only changed card records and declared dependencies. Phones with conflicting work or a mapping that requires review are skipped.</p>
    {dirty && <p>Save the definition before reviewing its distribution.</p>}
    {error && <p role="alert">{error}</p>}
    <button type="button" disabled={busy || dirty} onClick={() => void prepare(false)}>Preview affected phones</button>
    {preview && <><p>Eligible phones: {preview.eligiblePhones}. Downloads to prepare: {preview.scheduledPhones}. Host metadata only: {preview.metadataOnlyPhones}. Skipped: {preview.skippedPhones}.</p>
      {preview.configurations.length > 0 && <div className="table-set-versions"><table><thead><tr><th>Assigned configuration</th><th>Positions</th><th>Records</th><th>Phones</th><th>Result</th></tr></thead>
        <tbody>{preview.configurations.map((row, index) => <tr key={`${row.setId}:${index}`}><td><Link to={`/table-sets/${row.setId}`}>{row.name}</Link></td>
          <td>{row.positions.join(', ')}</td><td>{row.records.length ? row.records.map(number => `MT${number}`).join(', ') : 'No wire changes'}</td>
          <td>{row.phones}</td><td>{row.reason ?? 'Ready to prepare'}</td></tr>)}</tbody></table></div>}
      {canApply && <button type="button" disabled={busy || dirty || preview.eligiblePhones === 0} onClick={() => void prepare(true)}>Confirm update and prepare downloads</button>}
      <p>The desired host assignment changes on confirmation. A queued download is not a phone acknowledgement; check Downloads after the next terminal call-in.</p></>}
    {result && <div role="status"><p>Updated {result.affectedPhones} phone assignments and prepared {result.scheduledPhones} downloads. {result.skippedPhones} skipped.</p>
      <p>{result.metadataOnlyPhones} phones needed no wire changes. <Link to="/downloads">View Downloads</Link>.</p>
      <ul>{result.sets.map(set => <li key={set.id}><Link to={`/table-sets/${set.id}`}>{set.name}</Link></li>)}</ul></div>}
  </section>
}
