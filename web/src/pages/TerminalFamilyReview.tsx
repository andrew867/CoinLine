import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet } from '../api/client'
import { TerminalFamilySelect } from '../components/TerminalFamilySelect'

type Evidence = { number: number; length: number; format: string; screeningSlots: number[]; screeningFit?: string | null; screeningNote?: string | null }
type Requirement = { name: string; required: boolean; candidates: number[]; records: Evidence[]; status: string; note?: string | null }
type Review = {
  familyId: string; familyLabel: string; documented: boolean; screeningEntries: number | null
  note: string | null; tables: Requirement[]
}

const statusText: Record<string, string> = {
  present: 'Candidate present', missing: 'Missing from set', partial: 'Partial record group',
  invalid: 'Check record format', review: 'Candidate needs review', unmapped: 'Wire record unidentified',
}
const formatText: Record<string, string> = {
  verifiedLength: 'Emulated-terminal acknowledged length', catalogueLength: 'Reference catalogue length',
  recognized: 'Recognized editor layout', unverified: 'Unverified layout', unsupported: 'Absent from target catalogue',
  wrongLength: 'Wrong target length', invalid: 'Incomplete or duplicate record',
}

function recordRanges(numbers: number[]) {
  const groups: string[] = []
  for (let first = 0; first < numbers.length;) {
    let last = first
    while (numbers[last + 1] === numbers[last] + 1) last++
    groups.push(last === first ? `MT${numbers[first]}` : `MT${numbers[first]} through MT${numbers[last]}`)
    first = last + 1
  }
  return groups.join(', ')
}

export function TerminalFamilyReview({ setId, initialFamily, dataKey, recordDirty }: {
  setId: string; initialFamily: string; dataKey: string; recordDirty: boolean
}) {
  const [familyId, setFamilyId] = useState(initialFamily)
  const [review, setReview] = useState<Review | null>(null)
  const [error, setError] = useState('')
  useEffect(() => {
    if (!familyId) return
    let active = true
    setReview(null)
    setError('')
    void apiGet<Review>(`/api/tables/sets/${setId}/family-review?familyId=${encodeURIComponent(familyId)}`)
      .then(result => { if (active) setReview(result) })
      .catch(() => { if (active) setError('Could not load the terminal family checklist.') })
    return () => { active = false }
  }, [setId, familyId, dataKey])

  return <section className="table-config-panel" aria-labelledby="family-review-heading">
    <h2 id="family-review-heading">Terminal family checklist</h2>
    <div className="table-config-selectors">
      <TerminalFamilySelect value={familyId} onChange={setFamilyId} emptyLabel="Select a family to review" />
    </div>
    {familyId && error && <p role="alert">{error}</p>}
    {familyId && !review && !error && <p role="status">Loading family requirements…</p>}
    {familyId && review?.familyId === familyId && <>
      {review.note && <p>{review.note}</p>}
      {review.documented && <>
        <p>Maximizer lists {review.screeningEntries} screening entries for this family. The packed layout and segment
          counts depend on the installed firmware. The checklist describes a full installation. A partial download may omit records already on the phone.</p>
        <dl className="native-table-summary">
          <div><dt>Mandatory families with recognized candidates</dt><dd>{review.tables.filter(row => row.required && row.status === 'present').length}
            {' / '}{review.tables.filter(row => row.required).length}</dd></div>
          <div><dt>Feature-specific families with recognized candidates</dt><dd>{review.tables.filter(row => !row.required && row.status === 'present').length}
            {' / '}{review.tables.filter(row => !row.required).length}</dd></div>
        </dl>
        <div className="table-set-versions" role="region" aria-label="Family table requirements" tabIndex={0}>
          <table className="family-review-table">
            <thead><tr><th>Table family</th><th>Requirement</th><th>Candidate records</th><th>In this set</th></tr></thead>
            <tbody>{review.tables.map(row => <tr key={row.name}>
              <th scope="row">{row.name}{row.note && <small>{row.note}</small>}</th>
              <td>{row.required ? 'Mandatory' : 'Feature-specific'}</td>
              <td>{row.candidates.length ? recordRanges(row.candidates) : 'Unknown'}</td>
              <td><strong>{statusText[row.status] ?? row.status}</strong>
                {row.records.map(record => <div key={record.number}>
                  {!recordDirty ? <Link to={`/table-sets/${setId}?record=${record.number}&family=${encodeURIComponent(familyId)}#config-heading`}>
                    MT{record.number}</Link> : `MT${record.number}`}
                  {' · '}{record.length.toLocaleString()} B · {formatText[record.format] ?? record.format}
                  {!!record.screeningSlots.length && <small>Packed editor slots {record.screeningSlots.join(' or ')}</small>}
                  {record.screeningNote && <small>{record.screeningNote}</small>}
                </div>)}
              </td>
            </tr>)}</tbody>
          </table>
        </div>
      </>}
    </>}
  </section>
}
