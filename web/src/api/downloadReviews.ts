export type DownloadReviewReason = 'registration' | 'assignment' | 'inventory' | 'firmware' | 'family'
export type DownloadReview = { terminalId: string; tableSetId: string | null; reason: DownloadReviewReason }
const reviewKey = 'coinline.downloadReviews'
const preferenceKey = 'coinline.downloadReviewPrompt'
export const downloadReviewEvent = 'coinline:download-reviews'
const reasons: DownloadReviewReason[] = ['registration', 'assignment', 'inventory', 'firmware', 'family']

export function readDownloadReviews(): DownloadReview[] {
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(reviewKey) ?? '[]')
    if (!Array.isArray(value)) return []
    return value.filter((row): row is DownloadReview => row && typeof row.terminalId === 'string' &&
      (row.tableSetId === null || typeof row.tableSetId === 'string') && reasons.includes(row.reason))
  } catch { return [] }
}

function writeReviews(rows: DownloadReview[]) {
  try {
    if (rows.length) sessionStorage.setItem(reviewKey, JSON.stringify(rows))
    else sessionStorage.removeItem(reviewKey)
    window.dispatchEvent(new Event(downloadReviewEvent))
  } catch {
    // A browser storage failure must not change the result of an already saved manager record.
  }
}

export function rememberDownloadReview(review: DownloadReview) {
  const rows = readDownloadReviews().filter(item => item.terminalId !== review.terminalId)
  writeReviews([...rows, review])
}

export function dismissDownloadReview(terminalId: string) {
  writeReviews(readDownloadReviews().filter(item => item.terminalId !== terminalId))
}

export function resolveScheduledDownloadReview(terminalId: string, tableSetId: string) {
  writeReviews(readDownloadReviews().filter(item => item.terminalId !== terminalId || item.tableSetId !== tableSetId))
}

export function clearDownloadReviews() { writeReviews([]) }

export function readDownloadReviewPrompt(): boolean {
  try { return localStorage.getItem(preferenceKey) !== 'false' } catch { return true }
}

export function saveDownloadReviewPrompt(enabled: boolean) {
  localStorage.setItem(preferenceKey, String(enabled))
}
