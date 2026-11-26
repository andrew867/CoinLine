import './FieldEventsPage.css'

export type StatusGroup = {
  label: string
  rawMask?: number
  flags: { name: string; asserted: boolean; statusIndex?: number }[]
  unresolvedFlags?: { statusIndex: number; mask: number; description: string }[]
}

export const statusProfileLabel = (profile: string) => profile === 'Later' ? 'Registered later profile'
  : profile === 'Earlier' ? 'Registered earlier profile' : 'Profile not established'

const hexByte = (value: number) => `0x${value.toString(16).toUpperCase().padStart(2, '0')}`

export function ReportedStatusGroups({ groups, profile, rawStatusHex }: {
  groups: StatusGroup[]; profile?: string | null; rawStatusHex?: string | null
}) {
  return <>
    {profile && <p className="page-intro"><strong>{statusProfileLabel(profile)}.</strong>{' '}
      {profile === 'Earlier' || profile === 'Later'
        ? 'Interpretation uses the current registered firmware profile, not an independently verified historical version.'
        : 'Only shared meanings are certain; profile-dependent and reserved bits are shown separately.'}</p>}
    {rawStatusHex && <p>Reported status masks: <code>{rawStatusHex.match(/.{1,2}/g)?.join(' ')}</code></p>}
    <div className="current-phone-status-groups">
      {groups.map(group => <section key={group.label} aria-label={group.label}>
        <h4>{group.label}</h4>
        {group.rawMask != null && <small>Group mask: <code>{hexByte(group.rawMask)}</code></small>}
        <ul>{group.flags.map(flag => <li key={flag.statusIndex ?? flag.name} data-asserted={flag.asserted}>
          <span>{flag.name}</span><span>{flag.asserted ? 'Reported' : 'Not indicated'}</span>
        </li>)}
          {group.unresolvedFlags?.map(bit => <li key={`unresolved-${bit.statusIndex}`} data-asserted="true">
            <span><strong>Status bit {bit.statusIndex} ({hexByte(bit.mask)})</strong><br />{bit.description}</span><span>Unresolved</span>
          </li>)}
        </ul>
      </section>)}
    </div>
  </>
}
