import type { DiscoveredField, FieldState, MappingEntry, TableState } from './types'

export const snake = (s: string) => s.replace(/([a-z0-9])([A-Z])/g, '$1_$2').replace(/[^A-Za-z0-9]+/g, '_').replace(/^_+|_+$/g, '').toLowerCase()

export function fieldFromDiscovered(d: DiscoveredField, rects: { page: number; rect: number[] }[]): FieldState {
  const sig = d.type === 'text' && /^sig_[a-z0-9]+$/i.test(d.id)
  return {
    id: d.id, kind: sig ? 'image' : d.type, rects: rects.length ? rects : [{ page: d.page, rect: d.rect }],
    options: d.options, tooltip: d.tooltip, key: '', required: false, format: '', drawn: false,
    maxLength: d.maxLength ?? 1000, imgTypes: 'png,jpeg', imgMaxKb: 500, multiline: d.multiline,
    table: null, columns: 'name,qty,amount', rowHeight: 18, header: true,
  }
}

export function newDrawnField(id: string, page: number, rect: number[]): FieldState {
  return { id, kind: 'text', rects: [{ page, rect }], options: [], tooltip: '', key: '', required: false, format: '', drawn: true,
    maxLength: 1000, imgTypes: 'png,jpeg', imgMaxKb: 500, multiline: false, table: null, columns: 'name,qty,amount', rowHeight: 18, header: true }
}

/** Group Prefix{n}_Column fields into tables. Mutates fields (sets .table) and returns the tables. */
export function detectTables(fields: Record<string, FieldState>): Record<string, TableState> {
  const groups: Record<string, { rows: Set<number>; cols: Set<string>; ids: string[]; sep: string }> = {}
  for (const f of Object.values(fields)) {
    if (f.drawn) continue
    const m = /^([A-Za-z]+?)_?(\d+)_([A-Za-z0-9]+)$/.exec(f.id); if (!m) continue
    const [, prefix, n, col] = m
    const g = (groups[prefix] ??= { rows: new Set(), cols: new Set(), ids: [], sep: f.id.includes(`${prefix}_${n}`) ? '_' : '' })
    g.rows.add(+n); g.cols.add(col); g.ids.push(f.id)
  }
  const tables: Record<string, TableState> = {}
  for (const [prefix, g] of Object.entries(groups)) {
    if (g.rows.size < 2) continue
    const name = prefix.toLowerCase()
    const columns: Record<string, string> = {}; for (const c of g.cols) columns[snake(c)] = `${prefix}${g.sep}{n}_${c}`
    tables[name] = { name, key: name, required: false, rows: [...g.rows].sort((a, b) => a - b), columns, fieldIds: g.ids }
    for (const id of g.ids) { fields[id].table = name; fields[id].key = '' }
  }
  return tables
}

/** Editor state -> mapping contract (what PUT /mapping stores and the engine consumes). */
export function toMapping(fields: Record<string, FieldState>, tables: Record<string, TableState>): Record<string, MappingEntry> {
  const out: Record<string, MappingEntry> = {}
  for (const t of Object.values(tables)) {
    if (!t.key) continue
    out[t.key] = { type: 'table', rows: t.rows.length, columns: t.columns, ...(t.required ? { required: true } : {}) }
  }
  for (const f of Object.values(fields)) {
    if (!f.key || f.table) continue
    const e: MappingEntry = { field: f.id, type: f.kind }
    if (f.drawn) { delete e.field; e.box = { page: f.rects[0].page, rect: f.rects[0].rect.map(v => +v.toFixed(1)) } }
    if (f.kind === 'text') e.maxLength = f.maxLength
    if (f.kind === 'image') { e.imageTypes = f.imgTypes.split(','); e.maxKb = f.imgMaxKb }
    if (f.kind === 'table') { e.columns = f.columns.split(',').map(c => c.trim()).filter(Boolean); e.rowHeight = f.rowHeight; e.header = f.header }
    if (f.format) e.format = f.format
    if (f.required) e.required = true
    if (f.options.length && f.kind !== 'checkbox') e.values = f.options
    const prev = out[f.key]
    if (prev && !prev.box && e.field) prev.field = ([] as string[]).concat(prev.field ?? [], e.field)   // one key -> several fields
    else out[f.key] = e
  }
  return out
}

/** Mapping contract -> editor state (when opening a saved template). */
export function applyMapping(mapping: Record<string, MappingEntry>, fields: Record<string, FieldState>): Record<string, TableState> {
  const tables: Record<string, TableState> = {}
  let seq = 1
  for (const [key, e] of Object.entries(mapping)) {
    if (e.type === 'table' && e.columns && !Array.isArray(e.columns) && !e.box) {
      const ids: string[] = []
      for (let n = 1; n <= (e.rows ?? 0); n++) for (const p of Object.values(e.columns)) { const id = p.replace('{n}', String(n)); if (fields[id]) { fields[id].table = key; ids.push(id) } }
      tables[key] = { name: key, key, required: !!e.required, rows: Array.from({ length: e.rows ?? 0 }, (_, i) => i + 1), columns: e.columns, fieldIds: ids }
      continue
    }
    if (e.box) {
      const id = `Box_${seq++}`
      const f = newDrawnField(id, e.box.page, e.box.rect)
      Object.assign(f, { key, kind: e.type, required: !!e.required, format: e.format ?? '', maxLength: e.maxLength ?? 1000,
        columns: Array.isArray(e.columns) ? e.columns.join(',') : f.columns, rowHeight: e.rowHeight ?? 18, header: e.header ?? true })
      fields[id] = f; continue
    }
    for (const id of ([] as string[]).concat(e.field ?? [])) {
      const f = fields[id]; if (!f) continue
      Object.assign(f, { key, required: !!e.required, format: e.format ?? '', maxLength: e.maxLength ?? f.maxLength,
        imgTypes: e.imageTypes?.join(',') ?? f.imgTypes, imgMaxKb: e.maxKb ?? f.imgMaxKb })
      if (e.type === 'image') f.kind = 'image'
    }
  }
  return tables
}

/** Sample payload from the mapping, for the test panel. */
export function sampleData(fields: Record<string, FieldState>, tables: Record<string, TableState>): Record<string, unknown> {
  const d: Record<string, unknown> = {}
  for (const t of Object.values(tables)) if (t.key)
    d[t.key] = [1, 2].map(i => Object.fromEntries(Object.keys(t.columns).map(c => [c, /amount|requested|match|total|qty|price/.test(c) ? 1250 * i : `Row ${i} ${c}`])))
  for (const f of Object.values(fields)) {
    if (!f.key || f.table || d[f.key] !== undefined) continue
    if (f.kind === 'table') d[f.key] = [1, 2, 3].map(i => Object.fromEntries(f.columns.split(',').map(c => c.trim()).filter(Boolean).map(c => [c, /amount|qty|price|total/.test(c) ? 100 * i : `Row ${i} ${c}`])))
    else if (f.kind === 'checkbox') d[f.key] = true
    else if (f.kind === 'radio' || f.kind === 'choice') d[f.key] = f.options[0] ?? 'Yes'
    else if (f.kind === 'image') d[f.key] = ''
    else if (/date|_start|_end/.test(f.key) || f.format.startsWith('date')) d[f.key] = '2026-09-18'
    else if (/(_q[1-4]|amount|requested|match|total|score|employees|jobs|year|months|pct)$/.test(f.key)) d[f.key] = 1250
    else d[f.key] = `Sample ${f.key.replace(/_/g, ' ')}`.slice(0, f.maxLength)
  }
  return d
}
