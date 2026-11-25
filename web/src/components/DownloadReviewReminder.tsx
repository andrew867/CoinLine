import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { dismissDownloadReview, downloadReviewEvent, readDownloadReviewPrompt, readDownloadReviews, type DownloadReviewReason } from '../api/downloadReviews'

const reasonText: Record<DownloadReviewReason, string> = {
  registration: 'Phone registered',
  assignment: 'Table assignment changed',
  inventory: 'Inventory updated',
  firmware: 'Registered firmware changed',
  family: 'Terminal family changed',
}

function useReviews() {
  const [reviews, setReviews] = useState(readDownloadReviews)
  useEffect(() => {
    const update = () => setReviews(readDownloadReviews())
    window.addEventListener(downloadReviewEvent, update)
    return () => window.removeEventListener(downloadReviewEvent, update)
  }, [])
  return reviews
}

export function downloadSchedulePath(setId: string, phoneId: string) {
  return `/table-sets/${encodeURIComponent(setId)}?schedulePhone=${encodeURIComponent(phoneId)}#table-assignment-heading`
}

export function PhoneDownloadReview({ terminalId, tableSetId, canSchedule, canReadAssignment }: {
  terminalId: string; tableSetId: string | null; canSchedule: boolean; canReadAssignment: boolean
}) {
  const reviews = useReviews()
  const review = reviews.find(item => item.terminalId === terminalId)
  if (!review || !readDownloadReviewPrompt()) return null
  const matches = review.tableSetId == null || tableSetId === review.tableSetId
  return <section className="table-config-panel" aria-label="Download review">
    <h2>Review terminal download</h2>
    <p>{reasonText[review.reason]}. This is a Manager record change, not a confirmed terminal download.</p>
    {tableSetId && !matches ? <p>The current assignment differs from this reminder. Review the active assignment before scheduling.</p>
      : tableSetId && canSchedule ? <p><Link to={downloadSchedulePath(tableSetId, terminalId)}>Preview download for this phone</Link></p>
      : <p>{canReadAssignment && !tableSetId ? 'Review or assign a suitable table set before scheduling.' : 'Ask an operator with the required assignment and download access to review this phone.'}</p>}
    <button type="button" onClick={() => dismissDownloadReview(terminalId)}>Mark reviewed (no download needed)</button>
  </section>
}

export function DownloadReviewFooter({ canReadPhones }: { canReadPhones: boolean }) {
  const reviews = useReviews()
  useEffect(() => {
    if (!canReadPhones || reviews.length === 0) return
    function beforeUnload(event: BeforeUnloadEvent) {
      if (readDownloadReviews().length === 0) return
      event.preventDefault()
      event.returnValue = ''
    }
    window.addEventListener('beforeunload', beforeUnload)
    return () => window.removeEventListener('beforeunload', beforeUnload)
  }, [canReadPhones, reviews.length])
  if (!canReadPhones || reviews.length === 0) return null
  return <section className="table-config-panel" aria-label="Pending download reviews">
    <h2>Pending download reviews ({reviews.length})</h2>
    <p>Manager changes awaiting review in this browser tab. These are reminders, not queued downloads.</p>
    <ul>{reviews.map(review => <li key={review.terminalId}>
      <Link to={`/terminals/${encodeURIComponent(review.terminalId)}`}>Review phone {review.terminalId.slice(0, 8)}</Link>
      {' '}{reasonText[review.reason]}{' '}
      <button type="button" onClick={() => dismissDownloadReview(review.terminalId)}>Dismiss reminder</button>
    </li>)}</ul>
  </section>
}
