import { Link } from 'react-router-dom'

export type CollectionConfiguration = {
  statisticsRecordStatus: string
  callInRecordStatus: string
  statisticsIntervalDays: number | null
  statisticsUploadThreshold: number | null
  carrierStatisticsEnabled: boolean | null
  optionalCdrMode: string | null
  optionalCdrModeMask: number | null
  optionalCdrUploadThreshold: number | null
  optionalWindowStart: string | null
  optionalWindowDays: number | null
  optionalWindowHours: number | null
  normalCdrUploadThreshold: number | null
  findings: { tableNumber: number; message: string }[]
}

function recordStatus(value: string) {
  return value === 'configured' ? 'Included, recognized layout' : value === 'missing' ? 'Not included' : 'Unrecognized layout'
}

export function CollectionConfigurationPanel({ review, setId, allowLinks = false }: {
  review: CollectionConfiguration; setId?: string; allowLinks?: boolean
}) {
  const statistics = review.statisticsRecordStatus === 'configured'
  const callIn = review.callInRecordStatus === 'configured'
  return <section className="table-config-panel" aria-label="Collection configuration review">
    <h3>Call record and statistics collection</h3>
    <p>These are the saved configuration settings, not confirmation of what a phone has downloaded, activated or uploaded. Normal call records and optional logging are separate.</p>
    <dl className="native-table-summary">
      <div><dt>Collection policy (MT34)</dt><dd>{recordStatus(review.statisticsRecordStatus)}</dd></div>
      <div><dt>Normal call-in controls (MT35)</dt><dd>{recordStatus(review.callInRecordStatus)}</dd></div>
      {statistics && <>
        <div><dt>Call statistics interval</dt><dd>{review.statisticsIntervalDays == null ? 'Unknown' : review.statisticsIntervalDays === 0 ? 'Disabled (0)' : `${review.statisticsIntervalDays} day(s)`}</dd></div>
        <div><dt>Statistics count threshold</dt><dd>{review.statisticsUploadThreshold ?? 'Unknown'}</dd></div>
        <div><dt>Carrier statistics setting</dt><dd>{review.carrierStatisticsEnabled == null ? 'Unknown' : review.carrierStatisticsEnabled ? 'Enabled' : 'Disabled'}</dd></div>
        <div><dt>Optional CDR mode</dt><dd>{review.optionalCdrMode ?? 'Unknown'}{review.optionalCdrMode === 'Unrecognized combination' && review.optionalCdrModeMask != null ? ` (mask ${review.optionalCdrModeMask})` : ''}</dd></div>
        <div><dt>Optional CDR count threshold</dt><dd>{review.optionalCdrUploadThreshold ?? 'Unknown'}</dd></div>
        <div><dt>Optional logging window (phone-local)</dt><dd>{review.optionalWindowStart ?? 'Unknown start'}, {review.optionalWindowDays ?? '?'} day(s), {review.optionalWindowHours ?? '?'} hour(s){review.optionalCdrModeMask === 0 ? ' (optional mode disabled)' : ''}</dd></div>
      </>}
      {callIn && <div><dt>Normal CDR count threshold (outside optional window)</dt><dd>{review.normalCdrUploadThreshold ?? 'Unknown'}</dd></div>}
    </dl>
    {review.findings.length > 0 && <ul>{review.findings.map(finding => <li key={`${finding.tableNumber}:${finding.message}`}>MT{finding.tableNumber}: {finding.message}</li>)}</ul>}
    {setId && allowLinks && (statistics || callIn) && <p>
      {statistics && <><Link to={`/table-sets/${setId}?record=34#config-heading`}>Review MT34 collection policy</Link>{' '}</>}
      {callIn && <Link to={`/table-sets/${setId}?record=35#config-heading`}>Review MT35 normal call-in</Link>}
    </p>}
  </section>
}
