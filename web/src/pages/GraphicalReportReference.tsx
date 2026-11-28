import { Link } from 'react-router-dom'
import type { GraphicalUsageScreen } from './GraphicalUsageConfigurationPanel'

export type GraphicalReportReference = {
  status: 'acknowledged' | 'missing' | 'invalid' | 'ambiguous' | 'restricted'
  recordNumber: number
  downloadBatchId: string | null
  tableRevision: number | null
  ackRecordedAtUtc: string | null
  laterUncertainAttempt: boolean
  collectionDurationDays: number | null
  screens: GraphicalUsageScreen[] | null
}

const statusLabels = {
  acknowledged: 'Acknowledged reference',
  missing: 'No retained acknowledgement',
  invalid: 'Retained record has an incompatible format',
  ambiguous: 'Multiple equally recent acknowledgements',
  restricted: 'Reference unavailable with current permissions',
}

export function graphicalScreenReference(references: GraphicalReportReference[] | null | undefined, screenNumber: number) {
  if (!Number.isInteger(screenNumber) || screenNumber < 1 || screenNumber > 24) return undefined
  const number = 131 + Math.floor((screenNumber - 1) / 8)
  const reference = references?.find(row => row.recordNumber === number && row.status === 'acknowledged')
  return reference?.screens?.find(screen => screen.screenNumber === screenNumber && screen.recordNumber === number)
}

export function GraphicalReportReferenceSummary({ references }: { references?: GraphicalReportReference[] | null }) {
  return <section className="performance-report-reference" aria-label="Acknowledged graphical configuration reference">
    <h3>Acknowledged graphical configuration reference</h3>
    <p>Each reference is the latest host-recorded successful MT14 for this record before the upload was received.
      It is not the current assignment or proof that the phone used these settings throughout its reporting period.</p>
    {!references?.length ? <p>No configuration reference is available.</p> : <ul>
      {references.map(reference => <li key={reference.recordNumber}>
        <strong>MT{reference.recordNumber}</strong>: {statusLabels[reference.status]}.
        {reference.downloadBatchId && <> <Link to={`/downloads/${reference.downloadBatchId}`}>Download evidence</Link>
          {reference.tableRevision != null && <>, revision {reference.tableRevision}</>}
          {reference.ackRecordedAtUtc && <>, {new Date(reference.ackRecordedAtUtc).toLocaleString()}</>}.</>}
        {reference.recordNumber === 131 && reference.collectionDurationDays != null &&
          <> Stored collection duration: {reference.collectionDurationDays} days{reference.collectionDurationDays === 0 ? ' (disabled)' : ''}.</>}
        {reference.laterUncertainAttempt && <strong> A later attempt has no confirmed application acknowledgement.</strong>}
      </li>)}
    </ul>}
  </section>
}

export function GraphicalOwnerComparison({ reported, expected }: { reported: number; expected: number }) {
  return <small className={`graphical-owner-comparison ${reported === expected ? 'owner-match' : 'owner-diff'}`}>
    Reference {expected}, {reported === expected ? 'matches' : 'differs'}
  </small>
}
