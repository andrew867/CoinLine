export type RestrictedRoute = {
  slot: number
  destination: string | null
  classification: number
  classificationLabel: string | null
  dialable: boolean
  status: string
  trailingWildcards: number | null
  translated: boolean | null
  positionLabel?: string | null
  freeCall?: boolean | null
}
export type RestrictedRouteRecord = { number: number; availability: string; routes: RestrictedRoute[] }

const availability: Record<string, string> = {
  missing: 'Not included in this set. The terminal may retain an existing record.',
  ambiguous: 'More than one matching record is stored. Review the duplicate definitions.',
  unverified: 'The selected firmware does not establish a compatible field layout.',
  incompatible: 'The stored record does not match the verified later packed layout.',
}
const statuses: Record<string, string> = {
  configured_dialable: 'Configured dialable candidate',
  information_only: 'Information-only key',
  denied: 'Denied call classification',
  blank: 'Blank destination',
  unreadable_number: 'Unmapped number encoding',
  unreadable_wildcards: 'Unmapped wildcard count', navigation_only: 'Navigation only; no dialed destination',
}

export function RestrictedRouteReview({ records }: { records: RestrictedRouteRecord[] | null }) {
  const candidates = records?.flatMap(record => record.routes).filter(route => route.dialable).length ?? 0
  const graphical = records?.some(record => record.number >= 131) ?? false
  const recordName = (number: number) => number === 92 ? 'Call-Screening List' : number === 55 ? 'Quick Access Keys' : 'Graphical choice keys'
  return <section className="table-config-panel" aria-label="Permitted routes review">
    <h4>Configurable restricted access</h4>
    <p>Mark the permitted entries in Call-Screening List and Quick Access Keys using <strong>Allow in configurable restricted service</strong>.</p>
    {graphical && <p>On the verified graphical profile, use <strong>Allow during restricted service</strong> in the applicable graphical button section. The terminal uses mixed-panel buttons rather than their stored main-screen defaults.</p>}
    <p>This review reads the saved companion records. A marked entry is not proof that the terminal will permit, rate or connect a call.</p>
    <p>Legacy graphical terminal configuration guidance specifies free restricted routes. A marked paid route needs independent payment and device verification.</p>
    {records === null ? <p role="status">The saved companion-record review is unavailable. Check the firmware profile and open both records separately.</p> : <>
      <p role="status"><strong>{candidates}</strong> configured dialable {candidates === 1 ? 'candidate' : 'candidates'} in the included records.</p>
      {records.map(record => <section key={record.number} aria-label={record.number === 92 ? 'Screening restrictions' : record.number === 55 ? 'Quick-access restrictions' : `Graphical restrictions MT${record.number}`}>
        <h5>{recordName(record.number)} (MT{record.number})</h5>
        {record.availability !== 'included' ? <p>{availability[record.availability] ?? 'Record unavailable.'}</p> :
          record.routes.length === 0 ? <p>No entries marked as permitted.</p> :
            <div style={{ overflowX: 'auto' }}>
              <table cellPadding={6} style={{ width: '100%', borderCollapse: 'collapse' }}>
                <thead><tr><th scope="col">Position</th><th scope="col">Saved destination or pattern</th>
                  <th scope="col">Call classification</th><th scope="col">Free flag</th><th scope="col">Review</th></tr></thead>
                <tbody>{record.routes.map(route => <tr key={route.positionLabel ?? route.slot}>
                  <td>{route.positionLabel ?? `${record.number === 92 ? 'Entry' : 'Key'} ${route.slot}`}</td>
                  <td>{route.destination == null ? 'Unmapped' : route.destination || 'Blank'}
                    {route.trailingWildcards != null && route.trailingWildcards > 0 &&
                      <div><small>{route.trailingWildcards === 15 ? 'Unlimited trailing digits' : route.trailingWildcards > 15 ? 'Unmapped trailing digits' :
                        `${route.trailingWildcards} trailing wildcard digits`}</small></div>}
                    {route.translated && <div><small>Translation flag enabled</small></div>}</td>
                  <td>{route.classificationLabel ?? `Type ${route.classification}`}</td>
                  <td>{route.freeCall == null ? 'Not recorded' : route.freeCall ? 'Free' : 'Not free'}</td>
                  <td>{statuses[route.status] ?? 'Not established'}
                    {route.dialable && route.freeCall === false && <div><small>Review marked paid route</small></div>}</td>
                </tr>)}</tbody>
              </table>
            </div>}
      </section>)}
    </>}
  </section>
}
