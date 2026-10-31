import { afterEach, describe, expect, it } from 'vitest'
import { mkdtempSync, rmSync, utimesSync, writeFileSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import { strToU8, unzipSync, zipSync } from 'fflate'
import { buildPresentation } from '../presentation/slides'
import { readPresentation } from '../presentation/readPresentation'
import { elementLabels, elementText, slideTarget } from '../../shared/viewer/presentation'

const NS = 'xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"'
const REL = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
const PNG = Uint8Array.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 0])

function rels(entries: Array<[string, string, string, boolean?]>): string {
  return `<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">${entries
    .map(([id, type, target, external]) => `<Relationship Id="${id}" Type="${REL}/${type}" Target="${target}"${external ? ' TargetMode="External"' : ''}/>`)
    .join('')}</Relationships>`
}

const xfrm = (x: number, y: number, w: number, h: number) => `<a:xfrm><a:off x="${x}" y="${y}"/><a:ext cx="${w}" cy="${h}"/></a:xfrm>`

const THEME = `<a:theme ${NS} name="T"><a:themeElements><a:clrScheme name="C">
<a:dk1><a:sysClr val="windowText" lastClr="000000"/></a:dk1><a:lt1><a:sysClr val="window" lastClr="FFFFFF"/></a:lt1>
<a:dk2><a:srgbClr val="44546A"/></a:dk2><a:lt2><a:srgbClr val="E7E6E6"/></a:lt2>
<a:accent1><a:srgbClr val="4472C4"/></a:accent1><a:accent2><a:srgbClr val="ED7D31"/></a:accent2>
</a:clrScheme><a:fontScheme name="F"><a:majorFont><a:latin typeface="Georgia"/></a:majorFont><a:minorFont><a:latin typeface="Calibri"/></a:minorFont></a:fontScheme></a:themeElements></a:theme>`

const MASTER = `<p:sldMaster ${NS}><p:cSld><p:bg><p:bgRef idx="1001"><a:schemeClr val="bg1"/></p:bgRef></p:bg><p:spTree>
<p:sp><p:nvSpPr><p:cNvPr id="2" name="Title Placeholder 1"/><p:cNvSpPr/><p:nvPr><p:ph type="title"/></p:nvPr></p:nvSpPr><p:spPr>${xfrm(838200, 365125, 10515600, 1325563)}</p:spPr><p:txBody><a:bodyPr anchor="ctr"/><a:p/></p:txBody></p:sp>
<p:sp><p:nvSpPr><p:cNvPr id="3" name="Text Placeholder 2"/><p:cNvSpPr/><p:nvPr><p:ph type="body" idx="1"/></p:nvPr></p:nvSpPr><p:spPr>${xfrm(838200, 1825625, 10515600, 4351338)}</p:spPr><p:txBody><a:bodyPr/><a:p/></p:txBody></p:sp>
<p:sp><p:nvSpPr><p:cNvPr id="9" name="Logo bar"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr>${xfrm(0, 6667500, 12192000, 190500)}<a:prstGeom prst="rect"/><a:solidFill><a:schemeClr val="accent2"/></a:solidFill></p:spPr></p:sp>
</p:spTree></p:cSld><p:clrMap bg1="lt1" tx1="dk1" bg2="lt2" tx2="dk2" accent1="accent1" accent2="accent2"/>
<p:txStyles><p:titleStyle><a:lvl1pPr algn="l"><a:defRPr sz="4400"><a:solidFill><a:schemeClr val="tx1"/></a:solidFill><a:latin typeface="+mj-lt"/></a:defRPr></a:lvl1pPr></p:titleStyle>
<p:bodyStyle><a:lvl1pPr marL="228600" indent="-228600"><a:buFont typeface="Arial"/><a:buChar char="•"/><a:defRPr sz="2800"><a:solidFill><a:schemeClr val="tx1"/></a:solidFill></a:defRPr></a:lvl1pPr>
<a:lvl2pPr marL="685800" indent="-228600"><a:buChar char="–"/><a:defRPr sz="2400"/></a:lvl2pPr></p:bodyStyle><p:otherStyle/></p:txStyles></p:sldMaster>`

const LAYOUT = `<p:sldLayout ${NS}><p:cSld><p:spTree>
<p:sp><p:nvSpPr><p:cNvPr id="2" name="Title 1"/><p:cNvSpPr/><p:nvPr><p:ph type="title"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle><a:lvl1pPr><a:defRPr sz="4000"/></a:lvl1pPr></a:lstStyle><a:p/></p:txBody></p:sp>
<p:sp><p:nvSpPr><p:cNvPr id="3" name="Content Placeholder 2"/><p:cNvSpPr/><p:nvPr><p:ph idx="1"/></p:nvPr></p:nvSpPr><p:spPr>${xfrm(838200, 1900000, 5000000, 4000000)}</p:spPr><p:txBody><a:bodyPr/><a:p/></p:txBody></p:sp>
</p:spTree></p:cSld></p:sldLayout>`

const SLIDE_ONE = `<p:sld ${NS}><p:cSld><p:spTree>
<p:sp><p:nvSpPr><p:cNvPr id="2" name="Title 1"/><p:cNvSpPr/><p:nvPr><p:ph type="title"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:p><a:r><a:rPr lang="en-US" b="1"/><a:t>Quarterly review</a:t></a:r></a:p></p:txBody></p:sp>
<p:sp><p:nvSpPr><p:cNvPr id="3" name="Content Placeholder 2"/><p:cNvSpPr/><p:nvPr><p:ph idx="1"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/>
<a:p><a:r><a:rPr lang="en-US"/><a:t>Revenue up </a:t></a:r><a:r><a:rPr lang="en-US"><a:solidFill><a:schemeClr val="accent1"><a:lumMod val="50000"/></a:schemeClr></a:solidFill><a:hlinkClick r:id="rId9"/></a:rPr><a:t>12%</a:t></a:r></a:p>
<a:p><a:pPr lvl="1"/><a:r><a:rPr lang="en-US"/><a:t>Driven by renewals</a:t></a:r></a:p></p:txBody></p:sp>
<p:pic><p:nvPicPr><p:cNvPr id="4" name="Picture 3"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed="rId2"/><a:srcRect l="10000"/></p:blipFill><p:spPr>${xfrm(7000000, 2000000, 3000000, 2000000)}</p:spPr></p:pic>
<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id="5" name="Table 4"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr><p:xfrm><a:off x="1000000" y="5000000"/><a:ext cx="4000000" cy="740000"/></p:xfrm>
<a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/table"><a:tbl><a:tblPr firstRow="1"><a:tableStyleId>{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}</a:tableStyleId></a:tblPr><a:tblGrid><a:gridCol w="2000000"/><a:gridCol w="2000000"/></a:tblGrid>
<a:tr h="370000"><a:tc gridSpan="2"><a:txBody><a:bodyPr/><a:p><a:r><a:t>Region</a:t></a:r></a:p></a:txBody><a:tcPr/></a:tc><a:tc hMerge="1"><a:txBody><a:bodyPr/><a:p/></a:txBody><a:tcPr/></a:tc></a:tr>
<a:tr h="370000"><a:tc><a:txBody><a:bodyPr/><a:p><a:r><a:t>EMEA</a:t></a:r></a:p></a:txBody><a:tcPr><a:solidFill><a:srgbClr val="FFEEDD"/></a:solidFill></a:tcPr></a:tc><a:tc><a:txBody><a:bodyPr/><a:p><a:r><a:t>42</a:t></a:r></a:p></a:txBody><a:tcPr/></a:tc></a:tr>
</a:tbl></a:graphicData></a:graphic></p:graphicFrame>
<p:sp><p:nvSpPr><p:cNvPr id="6" name="Oval 5"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr>${xfrm(100000, 100000, 952500, 952500)}<a:prstGeom prst="ellipse"><a:avLst/></a:prstGeom></p:spPr>
<p:style><a:lnRef idx="1"><a:schemeClr val="accent1"><a:shade val="50000"/></a:schemeClr></a:lnRef><a:fillRef idx="3"><a:schemeClr val="accent1"/></a:fillRef><a:effectRef idx="0"><a:schemeClr val="accent1"/></a:effectRef><a:fontRef idx="minor"><a:schemeClr val="lt1"/></a:fontRef></p:style>
<p:txBody><a:bodyPr/><a:p><a:r><a:t>Go</a:t></a:r></a:p></p:txBody></p:sp>
</p:spTree></p:cSld></p:sld>`

const SLIDE_TWO = `<p:sld ${NS} show="0"><p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val="112233"/></a:solidFill></p:bgPr></p:bg><p:spTree>
<p:grpSp><p:nvGrpSpPr><p:cNvPr id="7" name="Group 6"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="1905000" y="0"/><a:ext cx="1905000" cy="1905000"/><a:chOff x="0" y="0"/><a:chExt cx="952500" cy="952500"/></a:xfrm></p:grpSpPr>
<p:sp><p:nvSpPr><p:cNvPr id="8" name="TextBox 7"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr><p:spPr>${xfrm(476250, 476250, 476250, 476250)}<a:prstGeom prst="rect"/><a:noFill/></p:spPr><p:txBody><a:bodyPr wrap="none"/><a:p><a:r><a:t>Hidden note</a:t></a:r></a:p></p:txBody></p:sp>
</p:grpSp>
<p:grpSp><p:nvGrpSpPr><p:cNvPr id="20" name="Rotated Group"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm rot="5400000"><a:off x="0" y="0"/><a:ext cx="1905000" cy="1905000"/><a:chOff x="0" y="0"/><a:chExt cx="1905000" cy="1905000"/></a:xfrm></p:grpSpPr>
<p:sp><p:nvSpPr><p:cNvPr id="21" name="Rotated Child"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr>${xfrm(0, 0, 190500, 190500)}<a:prstGeom prst="rect"/><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></p:spPr></p:sp>
</p:grpSp>
<p:grpSp><p:nvGrpSpPr><p:cNvPr id="22" name="Flipped Group"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm flipH="1"><a:off x="0" y="0"/><a:ext cx="1905000" cy="1905000"/><a:chOff x="0" y="0"/><a:chExt cx="1905000" cy="1905000"/></a:xfrm></p:grpSpPr>
<p:sp><p:nvSpPr><p:cNvPr id="23" name="Flipped Child"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr>${xfrm(0, 0, 190500, 190500)}<a:prstGeom prst="rect"/><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></p:spPr></p:sp>
</p:grpSp>
<p:sp><p:nvSpPr><p:cNvPr id="24" name="Numbered"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr><p:spPr>${xfrm(0, 0, 952500, 952500)}</p:spPr><p:txBody><a:bodyPr/><a:p><a:pPr><a:buAutoNum type="romanLcPeriod" startAt="100000000000000000000"/></a:pPr><a:r><a:t>Item</a:t></a:r></a:p></p:txBody></p:sp>
</p:spTree></p:cSld></p:sld>`

const NOTES = `<p:notes ${NS}><p:cSld><p:spTree><p:sp><p:nvSpPr><p:cNvPr id="2" name="Notes"/><p:cNvSpPr/><p:nvPr><p:ph type="body" idx="1"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:p><a:r><a:t>Mention churn.</a:t></a:r></a:p><a:p><a:r><a:t>Then pricing.</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:notes>`

function buildDeck(): Uint8Array {
  const files: Record<string, Uint8Array> = {
    '_rels/.rels': strToU8(rels([['rId1', 'officeDocument', 'ppt/presentation.xml']])),
    'ppt/presentation.xml': strToU8(`<p:presentation ${NS}><p:sldMasterIdLst><p:sldMasterId id="2147483648" r:id="rId1"/></p:sldMasterIdLst><p:sldIdLst><p:sldId id="256" r:id="rId2"/><p:sldId id="257" r:id="rId3"/></p:sldIdLst><p:sldSz cx="12192000" cy="6858000"/><p:defaultTextStyle><a:lvl1pPr><a:defRPr sz="1800"/></a:lvl1pPr></p:defaultTextStyle></p:presentation>`),
    'ppt/_rels/presentation.xml.rels': strToU8(rels([['rId1', 'slideMaster', 'slideMasters/slideMaster1.xml'], ['rId2', 'slide', 'slides/slide1.xml'], ['rId3', 'slide', 'slides/slide2.xml']])),
    'ppt/slideMasters/slideMaster1.xml': strToU8(MASTER),
    'ppt/slideMasters/_rels/slideMaster1.xml.rels': strToU8(rels([['rId1', 'slideLayout', '../slideLayouts/slideLayout1.xml'], ['rId2', 'theme', '../theme/theme1.xml']])),
    'ppt/slideLayouts/slideLayout1.xml': strToU8(LAYOUT),
    'ppt/slideLayouts/_rels/slideLayout1.xml.rels': strToU8(rels([['rId1', 'slideMaster', '../slideMasters/slideMaster1.xml']])),
    'ppt/theme/theme1.xml': strToU8(THEME),
    'ppt/slides/slide1.xml': strToU8(SLIDE_ONE),
    'ppt/slides/_rels/slide1.xml.rels': strToU8(rels([
      ['rId1', 'slideLayout', '../slideLayouts/slideLayout1.xml'],
      ['rId2', 'image', '../media/image1.png'],
      ['rId3', 'notesSlide', '../notesSlides/notesSlide1.xml'],
      ['rId9', 'hyperlink', 'https://example.com/report', true]
    ])),
    'ppt/slides/slide2.xml': strToU8(SLIDE_TWO),
    'ppt/slides/_rels/slide2.xml.rels': strToU8(rels([['rId1', 'slideLayout', '../slideLayouts/slideLayout1.xml']])),
    'ppt/notesSlides/notesSlide1.xml': strToU8(NOTES),
    'ppt/media/image1.png': PNG
  }
  return zipSync(files)
}

const tempDirs: string[] = []

afterEach(() => {
  for (const dir of tempDirs.splice(0)) rmSync(dir, { recursive: true, force: true })
})

describe('buildPresentation', () => {
  const deck = buildPresentation(buildDeck())
  const [first, second] = deck.slides
  const byName = (name: string) => first!.elements.find((element) => element.name === name)!

  it('converts the slide size from EMU to pixels and reads slide flags and notes', () => {
    expect(deck).toMatchObject({ width: 1280, height: 720, slideCount: 2 })
    expect(first).toMatchObject({ number: 1, hidden: false, notes: 'Mention churn.\nThen pricing.', background: { color: '#ffffff' } })
    expect(second).toMatchObject({ number: 2, hidden: true, notes: '', background: { color: '#112233' } })
  })

  it('inherits placeholder frames and text styles from the layout and master', () => {
    const title = byName('Title 1')
    expect(title.kind).toBe('text')
    expect(title.frame).toMatchObject({ x: 88, w: 1104 })
    expect(title.frame.y).toBeCloseTo(38.33, 1)
    expect(title.text?.anchor).toBe('middle')
    expect(title.text?.paragraphs[0]?.runs[0]).toMatchObject({ text: 'Quarterly review', bold: true, font: 'Georgia', color: '#000000' })
    expect(title.text?.paragraphs[0]?.runs[0]?.size).toBeCloseTo((40 * 96) / 72)

    const body = byName('Content Placeholder 2')
    expect(body.frame).toMatchObject({ x: 88, y: 1900000 / 9525, w: 5000000 / 9525 })
    const [top, nested] = body.text!.paragraphs
    expect(top).toMatchObject({ level: 0, bullet: { text: '•' }, marginLeft: 24, indent: -24 })
    expect(top!.runs[0]).toMatchObject({ font: 'Calibri' })
    expect(top!.runs[0]!.size).toBeCloseTo((28 * 96) / 72)
    expect(nested).toMatchObject({ level: 1, bullet: { text: '–' } })
    expect(nested!.runs[0]!.size).toBeCloseTo((24 * 96) / 72)
  })

  it('resolves theme colours with modifiers and keeps only web hyperlinks', () => {
    const link = byName('Content Placeholder 2').text!.paragraphs[0]!.runs[1]!
    expect(link).toMatchObject({ text: '12%', link: 'https://example.com/report', color: '#203864' })
    const oval = byName('Oval 5')
    expect(oval).toMatchObject({ kind: 'shape', geometry: { kind: 'ellipse' }, fill: { color: '#4472c4' }, line: { color: '#223962' } })
    expect(oval.text?.paragraphs[0]?.runs[0]?.color).toBe('#ffffff')
  })

  it('reads pictures as shared data URLs with their crop', () => {
    const picture = byName('Picture 3')
    expect(picture).toMatchObject({ kind: 'picture', crop: { left: 0.1, top: 0, right: 0, bottom: 0 } })
    expect(deck.images[picture.image!]).toMatch(/^data:image\/png;base64,/)
  })

  it('reads tables with merged cells, explicit fills, and the default table style', () => {
    const table = byName('Table 4').table!
    expect(table.columns).toEqual([2000000 / 9525, 2000000 / 9525])
    expect(table.rows[0]!.cells[0]).toMatchObject({ colSpan: 2, fill: '#4472c4' })
    expect(table.rows[0]!.cells[0]!.text.paragraphs[0]!.runs[0]).toMatchObject({ text: 'Region', bold: true, color: '#ffffff' })
    expect(table.rows[0]!.cells[1]).toBeNull()
    expect(table.rows[1]!.cells[0]!.fill).toBe('#ffeedd')
    expect(elementText(byName('Table 4'))).toBe('Region\nEMEA\n42')
  })

  it('draws master decorations behind slide content without making them commentable', () => {
    const bar = first!.elements[0]!
    expect(bar).toMatchObject({ name: 'Logo bar', inherited: true, kind: 'shape', fill: { color: '#ed7d31' } })
    expect(first!.elements.some((element) => element.inherited && element.placeholder)).toBe(false)
    expect(elementLabels(first!).has(bar.id)).toBe(false)
  })

  it('flattens groups into slide coordinates', () => {
    const box = second!.elements.find((element) => element.name === 'TextBox 7')!
    expect(box).toMatchObject({ kind: 'text', frame: { x: 300, y: 100, w: 100, h: 100 } })
    expect(box.text?.wrap).toBe(false)
  })

  it('rotates and mirrors grouped children around the group centre', () => {
    const rotated = second!.elements.find((element) => element.name === 'Rotated Child')!
    expect(rotated.frame).toMatchObject({ x: 180, y: 0, w: 20, h: 20, rotation: 90 })
    const flipped = second!.elements.find((element) => element.name === 'Flipped Child')!
    expect(flipped.frame).toMatchObject({ x: 180, y: 0, w: 20, h: 20, flipH: true })
  })

  it('bounds automatic numbering to the format range', () => {
    const numbered = second!.elements.find((element) => element.name === 'Numbered')!
    expect(numbered.text?.paragraphs[0]?.bullet?.text).toBe('mmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmdcclxvii.')
  })

  it('labels elements uniquely for comment targets', () => {
    const labels = elementLabels(first!)
    expect(labels.get(byName('Title 1').id)).toBe('Title')
    expect(labels.get(byName('Picture 3').id)).toBe('Picture')
    expect(labels.get(byName('Table 4').id)).toBe('Table')
    expect(labels.get(byName('Content Placeholder 2').id)).toBe('Text')
    expect(slideTarget(3, 'Chart')).toBe('Slide 3 · Chart')
    expect(slideTarget(3)).toBe('Slide 3')
  })

  it('keeps a slide that cannot be parsed as a failed frame', () => {
    const files = { ...unzipSync(buildDeck()), 'ppt/slides/slide2.xml': strToU8('<nonsense/>') }
    const result = buildPresentation(zipSync(files))
    expect(result.slides[1]).toMatchObject({ number: 2, failed: true, elements: [] })
    expect(result.slides[0]!.failed).toBeUndefined()
  })
})

describe('readPresentation', () => {
  it('rereads the file when it changes on disk', async () => {
    const dir = mkdtempSync(join(tmpdir(), 'presentation-test-'))
    tempDirs.push(dir)
    const file = join(dir, 'deck.pptx')
    writeFileSync(file, buildDeck())
    const first = await readPresentation(file)
    expect(await readPresentation(file)).toBe(first)
    writeFileSync(file, buildDeck())
    utimesSync(file, new Date(), new Date(Date.now() + 5000))
    expect(await readPresentation(file)).not.toBe(first)
  })

  it('rejects files that are not presentations', async () => {
    const dir = mkdtempSync(join(tmpdir(), 'presentation-test-'))
    tempDirs.push(dir)
    const file = join(dir, 'deck.pptx')
    writeFileSync(file, zipSync({ 'readme.txt': strToU8('hello') }))
    await expect(readPresentation(file)).rejects.toThrow()
  })
})
