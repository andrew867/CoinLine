type ExchangeRows = {
  label: string
  areas: Map<string, Map<number, number | string>>
}

export type ExchangeSource = ExchangeRows & (
  { kind: 'rates' }
  | { kind: 'registry'; provinces?: Map<string, string> }
  | { kind: 'region'; regionKind: 'LATA' | 'LIR' }
)

type RegionalExchangeSource = Extract<ExchangeSource, { kind: 'region' }>
export type ExchangeFile = {
  name: string
  source: ExchangeSource
  homeExchange?: string
  cnaRegions?: { name: string; source: RegionalExchangeSource }
}

export type LocalDialingSegment = {
  area: string | null
  storedArea: string | null
  maxRate: number
  fields: { key: string; value: string | null }[]
}

type ImportOptions = {
  area: string
  centers: ReadonlySet<string>
  selectedRate: string
  otherRate: string
  unlistedRate: string
}

export const firstExchange = 200
export const exchangeCount = 800

export function readExchangeFile(file: Blob, signal: AbortSignal) {
  return readDelimitedFile(file, signal, parseExchangeSource)
}

export function readCnaRegionFile(file: Blob, signal: AbortSignal, source: ExchangeSource) {
  return readDelimitedFile(file, signal, chunks => parseCnaRegionSource(source, chunks))
}

async function readDelimitedFile<T>(file: Blob, signal: AbortSignal,
  parse: (chunks: AsyncIterable<string>) => Promise<T>): Promise<T> {
  if (file.size > 64 * 1024 * 1024) throw new Error('Choose an extracted text file of 64 MiB or less.')
  const reader = file.stream().pipeThrough(new TextDecoderStream('utf-8', { fatal: true })).getReader()
  const cancel = () => { void reader.cancel().catch(() => {}) }
  signal.addEventListener('abort', cancel, { once: true })
  async function* chunks() {
    for (;;) {
      signal.throwIfAborted()
      const next = await reader.read()
      if (next.done) return
      yield next.value
    }
  }
  try {
    return await parse(chunks())
  } finally {
    signal.removeEventListener('abort', cancel)
    cancel()
    reader.releaseLock()
  }
}

type Row = { line: number; cells: string[] }

export async function parseExchangeSource(chunks: AsyncIterable<string>): Promise<ExchangeSource> {
  const records = delimitedRows(chunks)
  const header = await records.next()
  if (header.done) throw new Error('Choose a text file with a header and exchange data.')
  const columns = header.value.cells.map(value => value.toLowerCase().replace(/[^a-z0-9]/g, ''))
  const column = (name: string) => columns.indexOf(name)
  const combined = column('npanxx')
  const areaColumn = column('npa')
  const exchangeColumn = Math.max(column('nxx'), column('cocodenxx'))
  const rateColumn = column('rateindex')
  const serviceColumn = column('inservice')
  const useColumn = column('use')
  const statusColumn = column('status')
  const rateCenterColumn = column('ratecenter')
  const exchangeAreaColumn = column('exchangearea')
  const provinceColumn = column('province')
  const lataColumn = column('lata')
  const lirColumn = column('lir')
  const regionColumn = Math.max(lataColumn, lirColumn)
  if (rateColumn < 0 && lataColumn >= 0 && lirColumn >= 0) {
    await records.return(undefined)
    throw new Error('Choose one region column, LATA or LIR.')
  }
  const hasCodes = combined >= 0 || (areaColumn >= 0 && exchangeColumn >= 0)
  const usReport = useColumn >= 0 && serviceColumn >= 0 && rateCenterColumn >= 0
  const cnaReport = statusColumn >= 0 && exchangeAreaColumn >= 0
  const label = rateColumn >= 0 ? 'Rate-index spreadsheet'
    : regionColumn >= 0 ? (lataColumn >= 0 ? 'U.S. LATA crosswalk' : 'Canadian LIR crosswalk')
    : usReport ? 'NANPA utilized codes' : cnaReport ? 'CNA code status' : null
  if (!hasCodes || !label) {
    await records.return(undefined)
    throw new Error('Use extracted exchange data or a CSV with NPA, NXX and one of RateIndex, LATA or LIR.')
  }

  const areas = new Map<string, Map<number, number | string>>()
  const provinces = label === 'CNA code status' && provinceColumn >= 0 ? new Map<string, string>() : undefined
  for await (const { line, cells } of records) {
    if (rateColumn < 0 && useColumn >= 0 && serviceColumn >= 0 &&
      (cells[useColumn]?.toUpperCase() !== 'AS' || cells[serviceColumn]?.toLowerCase() !== 'yes')) continue
    if (rateColumn < 0 && statusColumn >= 0 && cells[statusColumn]?.toLowerCase() !== 'in service') continue

    const [area, exchange] = combined >= 0 ? (cells[combined] ?? '').split('-') : [cells[areaColumn], cells[exchangeColumn]]
    if (!isCode(area) || !isCode(exchange) || (combined >= 0 && cells[combined] !== `${area}-${exchange}`)) {
      throw new Error(`Line ${line} needs a three-digit NPA and NXX that each start with 2 to 9.`)
    }
    const nxx = Number(exchange)
    const value = rateColumn >= 0 ? readRate(cells[rateColumn], 127, `Rate index on line ${line}`)
      : regionColumn >= 0 ? cells[regionColumn]?.toUpperCase()
      : cells[label === 'NANPA utilized codes' ? rateCenterColumn : exchangeAreaColumn]
    if (rateColumn < 0 && regionColumn >= 0 && (!value || (lataColumn >= 0 && !/^[0-9]{3,5}$/.test(String(value))))) {
      throw new Error(`Line ${line} needs a valid ${lataColumn >= 0 ? 'LATA' : 'LIR'}.`)
    }
    if (value === undefined || value === '') throw new Error(`Line ${line} is missing the exchange area or rate center.`)
    let entries = areas.get(area)
    if (!entries) {
      entries = new Map()
      areas.set(area, entries)
    }
    const previous = entries.get(nxx)
    const prefix = `${area}-${nxx}`
    const province = cells[provinceColumn]?.toUpperCase()
    if ((previous !== undefined && previous !== value) ||
      (province && provinces?.has(prefix) && provinces.get(prefix) !== province)) {
      throw new Error(`Line ${line} conflicts with another entry for ${prefix}.`)
    }
    entries.set(nxx, value)
    if (province) provinces?.set(prefix, province)
  }
  if (!areas.size) throw new Error('The file contains no eligible exchanges.')
  if (rateColumn >= 0) return { kind: 'rates', label, areas }
  if (regionColumn >= 0) return { kind: 'region', regionKind: lataColumn >= 0 ? 'LATA' : 'LIR', label, areas }
  return { kind: 'registry', label, areas, provinces }
}

export async function parseCnaRegionSource(source: ExchangeSource, chunks: AsyncIterable<string>): Promise<RegionalExchangeSource> {
  if (source.kind !== 'registry' || !source.provinces) throw new Error('Load a CNA code-status CSV first.')
  const records = delimitedRows(chunks)
  const header = await records.next()
  const columns = header.done ? [] : header.value.cells.map(cell => cell.toLowerCase().replace(/[^a-z0-9]/g, ''))
  const provinceColumn = columns.indexOf('province')
  const complexColumn = columns.indexOf('npacomplex')
  const exchangeColumn = columns.indexOf('exchangearea')
  const lirColumn = columns.indexOf('lir')
  if ([provinceColumn, complexColumn, exchangeColumn, lirColumn].includes(-1)) {
    await records.return(undefined)
    throw new Error('Use the CNA Canadian Exchange Area Listing CSV with an LIR column.')
  }

  const regions = new Map<string, string>()
  for await (const { cells, line } of records) {
    const [province, complex, exchange, lir] = [provinceColumn, complexColumn, exchangeColumn, lirColumn].map(i => cells[i])
    // CNA publishes a date-only row and leaves LIR blank for exchanges without a listed region.
    if (!lir) continue
    const codes = complex?.split('/').map(code => code.trim()) ?? []
    if (!province || !exchange || !codes.length || codes.some(code => !isCode(code))) {
      throw new Error(`Line ${line} needs a province, NPA complex and exchange area.`)
    }
    for (const code of codes) {
      const key = cnaExchangeKey(province, code, exchange)
      const previous = regions.get(key)
      if (previous && previous !== lir) throw new Error(`Line ${line} has conflicting LIRs for ${province} ${code} ${exchange}.`)
      regions.set(key, lir)
    }
  }

  const areas = new Map<string, Map<number, string>>()
  for (const [area, entries] of source.areas) {
    const matched = new Map<number, string>()
    for (const [exchange, name] of entries) {
      const province = source.provinces.get(`${area}-${exchange}`)
      const lir = province && regions.get(cnaExchangeKey(province, area, String(name)))
      if (lir) matched.set(exchange, lir)
    }
    areas.set(area, matched)
  }
  if (![...areas.values()].some(entries => entries.size > 0)) {
    throw new Error('No in-service prefixes matched an LIR in the CNA Exchange Area Listing.')
  }
  return { kind: 'region', regionKind: 'LIR', label: 'CNA code status and LIRs', areas }
}

function cnaExchangeKey(province: string, area: string, exchange: string) {
  return `${province.toUpperCase()}|${area}|${exchange.normalize('NFKC').replace(/[‘’]/g, "'").replace(/\s+/g, ' ').toLowerCase()}`
}

export function homeExchangeRegion(source: RegionalExchangeSource, home: string) {
  const match = /^([2-9]\d{2})[- ]?([2-9]\d{2})$/.exec(home.trim())
  if (!match) throw new Error('Enter the home prefix as NPA-NXX.')
  const [, area, exchange] = match
  const region = source.areas.get(area)?.get(Number(exchange))
  if (typeof region !== 'string') throw new Error(`No ${source.regionKind} is listed for ${area}-${exchange}.`)
  return { area, region }
}

export function planLocalDialingImport(source: ExchangeSource, options: ImportOptions, segment: LocalDialingSegment) {
  const entries = source.areas.get(options.area)
  if (!entries) throw new Error('Choose an area code in the file.')
  const missing = exchangeCount - entries.size
  if (missing && options.area !== segment.storedArea && options.unlistedRate === '')
    throw new Error('Choose a rate index for unlisted exchanges when setting a different area code.')
  const filler = missing && options.unlistedRate !== '' ? readRate(options.unlistedRate, segment.maxRate, 'Unlisted exchange rate index') : null
  const edits: Record<string, string> = { areaCode: options.area }
  const changed: { exchange: number; from: string | null; to: number }[] = []
  segment.fields.forEach((field, index) => {
    const exchange = firstExchange + index
    const entry = entries.get(exchange)
    let rate = filler
    if (typeof entry === 'number') {
      rate = readRate(String(entry), segment.maxRate, `Rate index for ${exchange}`)
    } else if (typeof entry === 'string') {
      const selected = options.centers.has(entry)
      rate = readRate(selected ? options.selectedRate : options.otherRate, segment.maxRate,
        selected ? 'Selected rate center index' : 'Other in-service exchange rate index')
    }
    if (rate == null) return
    edits[field.key] = String(rate)
    if (edits[field.key] !== field.value) changed.push({ exchange, from: field.value, to: rate })
  })
  return { edits, changed, listed: entries.size, missing, filler, areaChanged: segment.area !== options.area }
}

export function exportLocalDialingCsv(segment: LocalDialingSegment) {
  if (!isCode(segment.area)) throw new Error('Set an area code before exporting a rate-index spreadsheet.')
  const records = segment.fields.map((field, index) => {
    const exchange = firstExchange + index
    const rate = readRate(field.value ?? undefined, segment.maxRate, `Rate index for ${exchange}`)
    return `${segment.area},${exchange},${rate}`
  })
  return ['NPA,NXX,RateIndex', ...records].join('\r\n') + '\r\n'
}

function isCode(value: string | null | undefined): value is string {
  return value != null && /^[2-9]\d{2}$/.test(value)
}

function readRate(value: string | undefined, maximum: number, label: string) {
  if (!value || !/^\d+$/.test(value) || Number(value) > maximum)
    throw new Error(`${label} must be a whole number from 0 to ${maximum}.`)
  return Number(value)
}

async function* lines(chunks: AsyncIterable<string>) {
  let pending = ''
  for await (const chunk of chunks) {
    const parts = (pending + chunk).split('\n')
    pending = parts.pop() ?? ''
    for (const part of parts) yield part.replace(/\r$/, '')
  }
  if (pending) yield pending.replace(/\r$/, '')
}

async function* delimitedRows(chunks: AsyncIterable<string>): AsyncGenerator<Row> {
  let delimiter = ''
  let count = 0
  let lineNumber = 0
  let quoted = false
  let closedQuote = false
  let field = ''
  let cells: string[] = []
  for await (const line of lines(chunks)) {
    count++
    if (!quoted && !line.trim()) continue
    if (!delimiter) delimiter = line.includes('\t') ? '\t' : ','
    if (delimiter === '\t') {
      yield { line: count, cells: line.split('\t').map(cell => cell.trim()) }
      continue
    }
    if (!quoted) lineNumber = count
    for (let position = 0; position < line.length; position++) {
      const character = line[position]
      if (quoted) {
        if (character !== '"') field += character
        else if (line[position + 1] === '"') { field += '"'; position++ }
        else { quoted = false; closedQuote = true }
      } else if (character === ',') {
        cells.push(field.trim())
        field = ''
        closedQuote = false
      } else if (character === '"' && !closedQuote && !field.trim()) {
        field = ''
        quoted = true
      } else if (closedQuote ? character.trim() : character === '"') {
        throw new Error(`Line ${count} has invalid CSV quoting.`)
      } else {
        field += character
      }
    }
    if (quoted) {
      field += '\n'
    } else {
      cells.push(field.trim())
      yield { line: lineNumber, cells }
      cells = []
      field = ''
      closedQuote = false
    }
  }
  if (quoted) throw new Error(`Line ${lineNumber} has an unterminated quoted field.`)
}
