export type FamilyOperatingMode = { expectedCode: number; expectedLabel: string; configuredCode: number | null;
  configuredLabel: string | null; status: 'matches' | 'differs' | 'missing' | 'invalid' | 'unverified'; message: string }

export function FamilyOperatingModeNotice({ mode }: { mode?: FamilyOperatingMode | null }) {
  if (!mode) return null
  return <div role="note" aria-label="Terminal family and payment mode" className="worksheet-note">
    <strong>Terminal family and MT26 payment mode</strong>
    <dl className="native-table-summary">
      <div><dt>Expected operating mode</dt><dd>{mode.expectedLabel} (code {mode.expectedCode})</dd></div>
      <div><dt>Saved MT26 operating mode</dt><dd>{mode.configuredLabel ? `${mode.configuredLabel} (code ${mode.configuredCode})`
        : mode.configuredCode != null ? `Unrecognized code ${mode.configuredCode}` : 'Not established'}</dd></div>
    </dl>
    <p>{mode.message}</p>
  </div>
}
