import { useState } from 'react'
import { StatusBadge } from '../components/StatusBadge'

type Reference = {
  sourceRecord: number; sourceSectionKey: string; sourceSection: string; sourceField: string; fieldKey: string
  role: string; address: number; target: string; targetState: string; advisory: string | null
}
export type GraphicalLinkReview = {
  records: { number: number; availability: string }[]
  references: Reference[]
}

const availability: Record<string, string> = {
  included: 'Included', missing: 'Not included', incompatible: 'Incompatible saved record', ambiguous: 'Duplicate records',
}
const roles: Record<string, string> = {
  enabled_context: 'Enabled context', disabled_context: 'Disabled context', mixed_default: 'Stored mixed-screen default',
  configured_reference: 'Configured link',
}
const states: Record<string, string> = {
  resolved: 'Resolved', builtin: 'Built-in application', no_selection: 'No screen selected',
  missing_record: 'Companion not included', incompatible_record: 'Incompatible companion', ambiguous_record: 'Duplicate companion',
  undefined_screen: 'Undefined screen', unknown_screen_kind: 'Unknown screen type', non_mixed_panel: 'Panel bits on non-mixed screen',
  unrecognized_address: 'Unrecognized address', noncanonical_builtin: 'Unverified built-in panel bits',
}

export function GraphicalLinkReviewPanel({ review, number, onOpenSection }: {
  review: GraphicalLinkReview | null; number: number; onOpenSection: (key: string) => void
}) {
  const [scope, setScope] = useState('current')
  const [issuesOnly, setIssuesOnly] = useState(true)
  const inScope = review?.references.filter(link => scope === 'all' || link.sourceRecord === number) ?? []
  const visible = inScope.filter(link => !issuesOnly || link.advisory != null)
  const issueCount = inScope.filter(link => link.advisory != null).length
  return <details className="graphical-simulator">
    <summary>Review saved graphical links</summary>
    <section className="table-config-panel" aria-label="Saved graphical link review">
      <h3>Saved graphical references</h3>
      <p>Check where the saved entry screens, loop links and choice keys point. This is a structural review, not a report of actual navigation or call behavior.</p>
      <p>The review uses saved records. Save pending changes before checking them here, or use the simulator to preview an edited record.</p>
      {review == null ? <p role="status">The saved link review is unavailable.</p> : <>
        <div className="row-actions">
          {review.records.map(record => <span key={record.number}>
            MT{record.number} <StatusBadge variant={record.availability === 'included' ? 'ok' : 'neutral'}
              label={availability[record.availability] ?? 'Unavailable'} />
          </span>)}
        </div>
        {review.records.some(record => record.number === 131 && record.availability !== 'included') &&
          <p>MT131 controls display contexts and rotation. If it is not in this set, the phone may retain its existing header.</p>}
        <div className="row-actions">
          <label>Review scope <select value={scope} onChange={event => setScope(event.target.value)}>
            <option value="current">Current record</option><option value="all">All graphical records</option>
          </select></label>
          <label><input type="checkbox" checked={issuesOnly} onChange={event => setIssuesOnly(event.target.checked)} /> Show only advisory findings</label>
        </div>
        <p role="status">{issueCount} advisory {issueCount === 1 ? 'finding' : 'findings'} in {inScope.length} saved {inScope.length === 1 ? 'reference' : 'references'}.</p>
        {visible.length === 0 ? <p>{inScope.length === 0 ? 'No configured references in this scope.' : 'No advisory findings in this scope.'}</p> :
          <div style={{ overflowX: 'auto' }}>
            <table cellPadding={6} style={{ borderCollapse: 'collapse', width: '100%' }}>
              <thead><tr><th scope="col">Source</th><th scope="col">Stored target</th><th scope="col">Review</th></tr></thead>
              <tbody>{visible.map(link => <tr key={`${link.sourceRecord}:${link.fieldKey}`}>
                <td>MT{link.sourceRecord} · {link.sourceSection}<div>{link.sourceField}</div>
                  <small>{roles[link.role] ?? 'Stored reference'}</small>
                  {link.sourceRecord === number && <div><button type="button" onClick={() => onOpenSection(link.sourceSectionKey)}>Open source section</button></div>}</td>
                <td>{link.target}<div><small>Address {link.address}</small></div></td>
                <td><StatusBadge label={states[link.targetState] ?? 'Unverified'} variant={link.advisory ? 'warn' : link.targetState === 'no_selection' ? 'neutral' : 'ok'} />
                  {link.advisory && <div><small>{link.advisory}</small></div>}</td>
              </tr>)}</tbody>
            </table>
          </div>}
      </>}
    </section>
  </details>
}
