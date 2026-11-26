import { useState } from 'react'
import { apiPostRaw } from '../api/client'

type Overlay = { message_type: number; record_length: number; segments: { offset: number; hex: string }[] }
type Preview = { changedBytes: number; resultSha256: string; canApply: boolean; previewToken: string;
  findings: { blocking: boolean; message: string }[] }
type Props = { endpoint: string; revision: number; sha256: string; profile: string; dirty: boolean; busy: boolean;
  onBusyChange: (value: boolean) => void; onApplied: () => void }

function readFile(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(new Error('Could not read the selected overlay file.'))
    reader.readAsText(file)
  })
}

function parseOverlay(text: string): Overlay {
  const parsed = JSON.parse(text) as Partial<Overlay> | null
  if (!parsed || parsed.message_type !== 62 || parsed.record_length !== 353 || !Array.isArray(parsed.segments)
    || parsed.segments.length !== 2) throw new Error('Choose a 353-byte MT62 ordinary-routing JSON overlay.')
  const seen = new Set<number>()
  const segments = parsed.segments.map(segment => {
    const length = segment?.offset === 1 ? 36 : segment?.offset === 32 ? 642 : 0
    if (!length || seen.has(segment.offset) || typeof segment.hex !== 'string' || segment.hex.length !== length
      || !/^[0-9a-f]+$/i.test(segment.hex)) throw new Error('The overlay must contain only the two permitted ordinary-routing segments.')
    seen.add(segment.offset)
    return { offset: segment.offset, hex: segment.hex }
  })
  return { message_type: 62, record_length: 353, segments }
}

export function NumberingPlanOverlayPanel({ endpoint, revision, sha256, profile, dirty, busy, onBusyChange, onApplied }: Props) {
  const [overlay, setOverlay] = useState<Overlay | null>(null)
  const [sourceProfile, setSourceProfile] = useState('')
  const [preview, setPreview] = useState<Preview | null>(null)
  const [error, setError] = useState('')
  const [reading, setReading] = useState(false)
  const available = !dirty && !busy && !reading
  const ready = available && overlay !== null && sourceProfile === profile

  async function choose(file?: File) {
    setOverlay(null); setPreview(null); setError(''); setSourceProfile('')
    if (!file) return
    setReading(true)
    try {
      if (file.size > 32768) throw new Error('The JSON overlay must be smaller than 32 KiB.')
      setOverlay(parseOverlay(await readFile(file)))
    } catch (problem) {
      setError(problem instanceof SyntaxError ? 'The file is not valid JSON.' : problem instanceof Error ? problem.message : 'Could not read this overlay.')
    } finally { setReading(false) }
  }

  async function submit(apply: boolean) {
    if (!ready || (apply && !preview?.canApply)) return
    const token = preview?.previewToken
    setPreview(null); setError(''); onBusyChange(true)
    try {
      const response = await apiPostRaw(`${endpoint}/numbering-overlay/${apply ? 'apply' : 'preview'}`,
        { revision, sha256, profile, sourceProfile, overlay, ...(apply ? { previewToken: token } : {}) })
      if (!response.ok) {
        const body = await response.json().catch(() => null) as { error?: string } | null
        throw new Error(body?.error ?? `Overlay request failed (HTTP ${response.status}).`)
      }
      if (apply) onApplied()
      else setPreview(await response.json() as Preview)
    } catch (problem) { setError(problem instanceof Error ? problem.message : 'Could not complete the overlay request.') }
    finally { onBusyChange(false) }
  }

  return <details className="native-spare-fields">
    <summary>Import ordinary numbering and dialing rules</summary>
    <p className="page-intro">Use a matching-profile MT62 JSON overlay to replace the ordinary dialing, display formatting and related settings.
      The existing site-specific bytes 19 through 31 stay intact. This changes only the draft record, not the assigned phones or downloads.</p>
    {dirty && <p role="status">Save or discard pending record edits before importing an overlay.</p>}
    <div className="table-config-selectors">
      <label>Numbering overlay file<input type="file" accept=".json,application/json" disabled={!available}
        onChange={event => void choose(event.target.files?.[0])} /></label>
      <label>Overlay source firmware layout<select value={sourceProfile} disabled={!available || !overlay}
        onChange={event => { setSourceProfile(event.target.value); setPreview(null); setError('') }}>
        <option value="">Choose the original layout</option>
        <option value="MTR 1.9 packed">MTR 1.9 packed</option>
        <option value="Later packed profile">Later packed profile</option>
      </select></label>
    </div>
    {sourceProfile && sourceProfile !== profile && <p role="status">The source layout must match {profile}.</p>}
    <div className="row-actions"><button type="button" disabled={!ready} onClick={() => void submit(false)}>Preview numbering overlay</button></div>
    {preview && <section aria-label="Numbering overlay preview">
      <p>{preview.changedBytes} of 353 bytes would change. Site-specific bytes remain unchanged.</p>
      <p>Result SHA-256: <code>{preview.resultSha256}</code></p>
      {preview.findings.length > 0 && <ul>{preview.findings.map((finding, index) =>
        <li key={index}>{finding.blocking ? 'Blocking: ' : 'Review: '}{finding.message}</li>)}</ul>}
      {preview.changedBytes === 0 && <p>The overlay already matches this record.</p>}
      {preview.canApply && <button type="button" disabled={!ready} onClick={() => void submit(true)}>Apply numbering overlay</button>}
    </section>}
    {error && <p className="native-table-feedback" role="alert">{error}</p>}
  </details>
}
