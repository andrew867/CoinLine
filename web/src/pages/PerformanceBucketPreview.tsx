import { bucketRange, performanceBucketPlan, performanceMeasurements } from './performanceBucketPlan'

export function PerformanceBucketPreview({ value }: { value: (key: string) => string | null | undefined }) {
  return <details className="performance-buckets">
    <summary>Performance histogram bucket ranges (MT37)</summary>
    <p>The stored start and interval are in 10 ms ticks. The phone counts elapsed time below the start in bucket 1, then in five adjacent intervals, and uses bucket 7 for the remaining times. The exact upper boundary belongs to the next bucket.</p>
    <p>These are the configured ranges, including staged edits, not measured counts or labels for previously uploaded reports. Actual firmware timing still needs bench verification.</p>
    {value('days') === '0' && <p>Performance collection is disabled. These ranges will apply only when collection is enabled.</p>}
    <div className="performance-bucket-grid">{performanceMeasurements.map(({ index, label }) => {
      const plan = performanceBucketPlan(value(`interval-${index}.start`), value(`interval-${index}.duration`))
      return <section key={index} aria-label={label} className="performance-bucket-measurement">
        <h4>{label}</h4>
        {plan.kind === 'invalid' ? <p role="status">Review the imported or staged timing values. Normal ranges cannot be calculated from negative, unavailable or out-of-range values.</p>
          : plan.kind === 'wrap' ? <p role="status">The boundary after five intervals exceeds 655.35 seconds and wraps in the terminal's 16-bit timer. Review the start and interval; normal ranges are not displayed.</p>
          : <>
            {plan.zeroWidth && <p>Zero interval: buckets 2 through 6 have empty ranges.</p>}
            <dl>{plan.buckets.map(bucket => <div key={bucket.number}>
              <dt>Bucket {bucket.number}</dt><dd>{bucketRange(bucket)}</dd>
            </div>)}</dl>
          </>}
      </section>
    })}</div>
  </details>
}
