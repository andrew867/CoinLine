import { Link } from 'react-router-dom'
import { canPerform, permissionOperation as op } from '../api/consoleSession'

type RecordComparison = {
  tableNumber: number
  actualLength: number | null
  expectedLength: number | null
  status: 'match' | 'unknown' | 'conflict'
  layoutReviewRequired: boolean
  message: string
}

export type ProfileComparison = {
  profileId: string
  label: string
  completeCatalogue: boolean
  observedLengthsOnly: boolean
  matchingLengthCount: number
  unknownCount: number
  conflictCount: number
  layoutReviewCount: number
  records: RecordComparison[]
}

export function TableProfileComparison({ comparisons, draft }: { comparisons: ProfileComparison[]; draft: boolean }) {
  return <details className="table-config-panel">
    <summary><strong>Compare saved records with known terminal profiles</strong></summary>
    <p>No table profile is registered for this set. These comparisons use the saved record lengths and any explicitly
      confirmed field layouts. They cannot identify the terminal version, certify an INSTALL set, or prove that
      matching-length records have the same field meanings.</p>
    <p>Confirm the actual terminal version before choosing a profile.
      {canPerform('firmware-registry', op.read) && <> Review <Link to="/firmware/versions">registered firmware versions</Link>.</>}
      {' '}{draft ? 'Select and save the confirmed firmware target above.' :
        'Make an unassigned draft copy before changing the set target.'} Nothing is applied by this comparison.</p>
    {comparisons.map(profile => <details key={profile.profileId} className="table-profile-candidate">
      <summary>
        <strong>{profile.label}</strong>{' '}
        <span>{profile.matchingLengthCount} matching, {profile.unknownCount} unknown, {profile.conflictCount} conflicts</span>
        {profile.layoutReviewCount > 0 && <span> · {profile.layoutReviewCount} matching records need field layout review</span>}
      </summary>
      <p>{profile.observedLengthsOnly ? 'Observed lengths only. Field layouts are not established for this profile.' :
        profile.completeCatalogue ? 'Complete known record catalogue. This comparison does not check which records are required.' :
          'Partial known catalogue. An unlisted record is not necessarily unsupported.'}</p>
      <table>
        <thead><tr><th>Record</th><th>Saved bytes</th><th>Known bytes</th><th>Finding</th></tr></thead>
        <tbody>{profile.records.map(record => <tr key={record.tableNumber}>
          <td>{record.tableNumber > 0 ? `MT${record.tableNumber}` : 'Unknown type'}</td>
          <td>{record.actualLength?.toLocaleString() ?? 'Unknown'}</td>
          <td>{record.expectedLength?.toLocaleString() ?? 'Unknown'}</td>
          <td><strong>{record.status === 'conflict' ? 'Conflict' : record.status === 'unknown' ? 'Unknown' :
            record.layoutReviewRequired ? 'Layout review' : 'Length match'}</strong>. {record.message}</td>
        </tr>)}</tbody>
      </table>
    </details>)}
  </details>
}
