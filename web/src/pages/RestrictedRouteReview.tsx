export type RestrictedRoute = {
  slot: number
  destination: string | null
  classification: number
  classificationLabel: string | null
  dialable: boolean
  status: string
  trailingWildcards: number | null
  translated: boolean | null
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
  unreadable_wildcards: 'Unmapped wildcard count',
}

export function RestrictedRouteReview({ records }: { records: RestrictedRouteRecord[] | null }) {
  const candidates = records?.flatMap(record => record.routes).filter(route => route.dialable).length ?? 0
  return <section className="table-config-panel" aria-label="Permitted routes review">
    <h4>Configurable restricted access</h4>
    <p>Mark the permitted entries in Call-Screening List and Quick Access Keys using <strong>Allow in configurable restricted service</strong>.</p>
    <p>This review reads the saved companion records. A marked entry is not proof that the terminal will permit, rate or connect a call.</p>
    {records === null ? <p role="status">The saved companion-record review is unavailable. Check the firmware profile and open both records separately.</p> : <>
      <p role="status"><strong>{candidates}</strong> configured dialable {candidates === 1 ? 'candidate' : 'candidates'} in the included records.</p>
      {records.map(record => <section key={record.number} aria-label={record.number === 92 ? 'Screening restrictions' : 'Quick-access restrictions'}>
        <h5>{record.number === 92 ? 'Call-Screening List (MT92)' : 'Quick Access Keys (MT55)'}</h5>
        {record.availability !== 'included' ? <p>{availability[record.availability] ?? 'Record unavailable.'}</p> :
          record.routes.length === 0 ? <p>No entries marked as permitted.</p> :
            <div style={{ overflowX: 'auto' }}>
              <table cellPadding={6} style={{ width: '100%', borderCollapse: 'collapse' }}>
                <thead><tr><th scope="col">Position</th><th scope="col">Saved destination or pattern</th>
                  <th scope="col">Call classification</th><th scope="col">Review</th></tr></thead>
                <tbody>{record.routes.map(route => <tr key={route.slot}>
                  <td>{record.number === 92 ? 'Entry' : 'Key'} {route.slot}</td>
                  <td>{route.destination == null ? 'Unmapped' : route.destination || 'Blank'}
                    {route.trailingWildcards != null && route.trailingWildcards > 0 &&
                      <div><small>{route.trailingWildcards === 15 ? 'Unlimited trailing digits' : route.trailingWildcards > 15 ? 'Unmapped trailing digits' :
                        `${route.trailingWildcards} trailing wildcard digits`}</small></div>}
                    {route.translated && <div><small>Translation flag enabled</small></div>}</td>
                  <td>{route.classificationLabel ?? `Type ${route.classification}`}</td>
                  <td>{statuses[route.status] ?? 'Not established'}</td>
                </tr>)}</tbody>
              </table>
            </div>}
      </section>)}
    </>}
  </section>
}
