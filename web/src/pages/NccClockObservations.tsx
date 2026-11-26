import { useEffect, useState } from 'react'
import { apiGet } from '../api/client'

type ClockOffer = { sequence: number; offeredClockUtc: string; retransmission: boolean }

export function NccClockObservations({ sessionId }: { sessionId: string }) {
  const [offers, setOffers] = useState<ClockOffer[] | null>(null)
  const [error, setError] = useState(false)
  useEffect(() => {
    let current = true
    setOffers(null)
    setError(false)
    void apiGet<{ clockOffers?: ClockOffer[] }>(`/api/ncc/sessions/${encodeURIComponent(sessionId)}?clockOnly=true`)
      .then(response => { if (current) setOffers(response.clockOffers ?? []) })
      .catch(() => { if (current) setError(true) })
    return () => { current = false }
  }, [sessionId])

  return <section aria-label="Clock synchronization observations">
    <h2>Clock synchronization observations</h2>
    <p>Exact calendar values offered by CoinLine in MT20. A transmitted value is not proof of the phone's stored clock.</p>
    {error && <p role="alert">Could not load the clock synchronization observations.</p>}
    {!offers && !error && <p role="status">Loading clock synchronization observations…</p>}
    {offers?.length === 0 && <p>No retained clock offer for this session. Older sessions may not contain this metadata.</p>}
    {!!offers?.length && <table>
      <caption>Host clock offers</caption>
      <thead><tr><th>Frame</th><th>Offered time (UTC)</th><th>Delivery</th></tr></thead>
      <tbody>{offers.map(offer => <tr key={offer.sequence}>
        <td>{offer.sequence}</td>
        <td><time dateTime={offer.offeredClockUtc}>{offer.offeredClockUtc.replace('T', ' ').replace(/Z$/, ' Z')}</time></td>
        <td>{offer.retransmission ? 'Retransmitted' : 'Original offer'}</td>
      </tr>)}</tbody>
    </table>}
  </section>
}
