import { describe, expect, it } from 'vitest'
import { exchangeCount, exportLocalDialingCsv, parseExchangeSource, planLocalDialingImport } from './localDialingExchangeData'

async function* chunks(...pieces: string[]) {
  yield* pieces
}

const segment = (area: string | null, maxRate = 15) => ({
  area, storedArea: area, maxRate,
  fields: Array.from({ length: exchangeCount }, (_, index) => ({ key: `exchange-${200 + index}`, value: '0' })),
})

const options = {
  area: '613', centers: new Set<string>(), selectedRate: '', otherRate: '', unlistedRate: '',
}

describe('local dialing spreadsheet import', () => {
  it('imports explicit rates into the matching segment and preserves unlisted entries', async () => {
    const source = await parseExchangeSource(chunks('\ufeffNPA-N', 'XX,RateIndex\r\n613-200,3\r\n', '613-999,2\r\n343-200,1'))
    const plan = planLocalDialingImport(source, options, segment('613'))
    expect(plan.edits).toEqual({ areaCode: '613', 'exchange-200': '3', 'exchange-999': '2' })
    expect(plan.changed.map(row => row.exchange)).toEqual([200, 999])
    expect(plan.missing).toBe(798)
    expect(plan.filler).toBeNull()
    expect(plan.areaChanged).toBe(false)
  })

  it('validates duplicates, exchange boundaries and the destination format capacity', async () => {
    await expect(parseExchangeSource(chunks('NPA,NXX,RateIndex\n613,200,2\n613,200,3'))).rejects.toThrow('Line 3 conflicts')
    await expect(parseExchangeSource(chunks('NPA,NXX,RateIndex\n613,199,2'))).rejects.toThrow('Line 2 needs')
    const source = await parseExchangeSource(chunks('NPA,NXX,RateIndex\n613,200,4'))
    expect(() => planLocalDialingImport(source, { ...options, unlistedRate: '3' }, segment(null, 3)))
      .toThrow('Rate index for 200 must be a whole number from 0 to 3.')
    expect(() => planLocalDialingImport(source, options, segment('343'))).toThrow('Choose a rate index for unlisted exchanges')
    expect(() => planLocalDialingImport(source, options, { ...segment('613'), storedArea: '343' }))
      .toThrow('Choose a rate index for unlisted exchanges')
  })

  it('round-trips a full exported segment without an inherited default', async () => {
    const current = segment('613')
    current.fields[0].value = '15'
    current.fields[799].value = '14'
    const csv = exportLocalDialingCsv(current)
    const source = await parseExchangeSource(chunks(csv))
    const plan = planLocalDialingImport(source, options, segment(null))
    expect(plan.listed).toBe(800)
    expect(plan.missing).toBe(0)
    expect(plan.edits['exchange-999']).toBe('14')
    expect(plan.edits['exchange-200']).toBe('15')
  })
})

describe('public exchange registry import', () => {
  it('uses only in-service assigned NANPA exchanges and applies explicit rate choices', async () => {
    const source = await parseExchangeSource(chunks(
      'State\tNPA-NXX\tUse\tRateCenter\tIn Service\tFile Updated\n',
      'ZZ\t205-200\tAS\tHOME\tYes\nZZ\t205-201\tAS\tOTHER\tYes\n',
      'ZZ\t205-202\tAS\tHOME\tNo\nZZ\t205-203\tUA\tHOME\tNo\n',
    ))
    const plan = planLocalDialingImport(source, {
      area: '205', centers: new Set(['HOME']), selectedRate: '1', otherRate: '2', unlistedRate: '3',
    }, segment(null, 3))
    expect(plan.listed).toBe(2)
    expect(plan.edits).toMatchObject({ areaCode: '205', 'exchange-200': '1', 'exchange-201': '2', 'exchange-202': '3', 'exchange-999': '3' })
  })

  it('reads CNA CSV with quoted text, line breaks and extra columns', async () => {
    const source = await parseExchangeSource(chunks(
      '"NPA","CO Code (NXX)","Status","Pooled","Exchange Area","Remarks"\r\n',
      '613,200,"In Service",,"Center, East","Line one\r\nLine two"\r\n',
      '613,201,Assigned,,"Center, East",\r\n613,202,"In Service",,"Town ""West""",\r\n',
    ))
    expect(source.label).toBe('CNA code status')
    expect([...source.areas.get('613')!]).toEqual([[200, 'Center, East'], [202, 'Town "West"']])
  })
})
