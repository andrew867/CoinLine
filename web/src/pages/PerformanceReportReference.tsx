import { Link } from 'react-router-dom'
import { bucketRange, performanceBucketPlan, performanceMeasurements } from './performanceBucketPlan'

export type PerformanceReference = {
  status: 'acknowledged' | 'missing' | 'invalid' | 'ambiguous' | 'restricted'
  downloadBatchId: string | null
  tableRevision: number | null
  ackRecordedAtUtc: string | null
  laterUncertainAttempt: boolean
  timing: { measurementIndex: number; startTicks: number; intervalTicks: number }[] | null
}

export function PerformanceReferenceSummary({ reference }: { reference?: PerformanceReference | null }) {
  const status = reference?.status
  const hasSource = status === 'acknowledged' || status === 'invalid'
  return <section className="performance-report-reference" aria-label="MT32 timing reference">
    <h3>MT32 timing reference</h3>
    {reference && hasSource ? <>
      <p>Last retained terminal-acknowledged MT32 recorded before the host received this report.
        {reference.tableRevision != null && <> Revision {reference.tableRevision}.</>}
        {reference.ackRecordedAtUtc && <> Acknowledgement recorded <time dateTime={reference.ackRecordedAtUtc}>
          {new Date(reference.ackRecordedAtUtc).toLocaleString()}</time> (host clock).</>}
        {reference.downloadBatchId && <> <Link to={`/downloads/${reference.downloadBatchId}`}>View source download</Link>.</>}
      </p>
      <p>Reference ranges are not proven historical bucket boundaries. The phone clock, its configuration during the
        reporting period, and any unobserved changes may differ. The reported counts remain unchanged.</p>
      {status === 'invalid' && <p>The acknowledged record does not have the supported MT32 format. Ranges are unavailable.</p>}
      {reference.laterUncertainAttempt && <p role="status">A later download attempt before receipt did not retain an acknowledgement.
        The last acknowledged record might no longer describe the phone.</p>}
    </> : <p>
      {status === 'ambiguous' ? 'Multiple retained acknowledgements have the same latest recorded time. No single reference can be selected.'
        : status === 'restricted' ? 'Viewing this reference requires read access to Downloads and Table records.'
          : 'No retained acknowledged MT32 before receipt is available. Only the original bucket numbers and counts are shown.'}
    </p>}
  </section>
}

export function performanceReportRanges(reference: PerformanceReference | null | undefined, group: string) {
  if (reference?.status !== 'acknowledged') return null
  const measurement = performanceMeasurements.find(item => item.reportGroup === group)
  if (!measurement) return null
  const pair = reference.timing?.find(item => item.measurementIndex === measurement.index)
  const plan = performanceBucketPlan(pair?.startTicks.toString(), pair?.intervalTicks.toString())
  if (plan.kind !== 'valid')
    return { ranges: null, reason: plan.kind === 'wrap'
      ? 'Reference timing wraps the terminal timer; ranges are unavailable.'
      : 'Reference timing is unavailable or invalid; ranges are unavailable.' }
  return { ranges: Object.fromEntries(plan.buckets.map(bucket => [`Bucket ${bucket.number}`, bucketRange(bucket)])), reason: null }
}
