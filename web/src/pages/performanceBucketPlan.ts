export const performanceMeasurements = [
  { index: 0, label: 'Access to dial complete' },
  { index: 1, label: 'Dial complete to carrier' },
  { index: 3, label: 'Carrier to first packet' },
  { index: 4, label: 'User waiting time' },
] as const

export type PerformanceBucket = { number: number; lower: number; upper: number | null }
export type PerformanceBucketPlan = { kind: 'valid'; buckets: PerformanceBucket[]; zeroWidth: boolean }
  | { kind: 'invalid' } | { kind: 'wrap' }

function ticks(value: string | null | undefined) {
  if (value == null || !/^\d+$/.test(value)) return null
  const n = Number(value)
  return Number.isSafeInteger(n) && n <= 32767 ? n : null
}

export function performanceBucketPlan(startValue: string | null | undefined, intervalValue: string | null | undefined): PerformanceBucketPlan {
  const start = ticks(startValue), interval = ticks(intervalValue)
  if (start === null || interval === null) return { kind: 'invalid' }
  if (start + 5 * interval > 65535) return { kind: 'wrap' }
  return { kind: 'valid', zeroWidth: interval === 0, buckets: [
    { number: 1, lower: 0, upper: start },
    ...Array.from({ length: 5 }, (_, index) => ({ number: index + 2, lower: start + index * interval, upper: start + (index + 1) * interval })),
    { number: 7, lower: start + 5 * interval, upper: null },
  ] }
}

function seconds(value: number) {
  return `${Math.floor(value / 100)}.${String(value % 100).padStart(2, '0')}`
}

export function bucketRange(bucket: PerformanceBucket) {
  if (bucket.upper === null) return `${seconds(bucket.lower)} s or more`
  if (bucket.lower === bucket.upper) return 'Empty range'
  if (bucket.lower === 0) return `Below ${seconds(bucket.upper)} s`
  return `${seconds(bucket.lower)} s to below ${seconds(bucket.upper)} s`
}
