import { useEffect, useRef, useState } from 'react'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import { SCALE } from '../pdf'
import type { FieldState, TableState } from '../types'

interface Props {
  doc: PDFDocumentProxy | null
  fields: Record<string, FieldState>; tables: Record<string, TableState>
  selected: string | null; drawMode: boolean
  onSelect: (id: string) => void
  onDraw: (page: number, rect: number[]) => void
}

/** Renders every page with pdf.js and overlays one tag per widget. Drag on a page in draw mode to add a box. */
export default function PdfCanvas({ doc, fields, tables, selected, drawMode, onSelect, onDraw }: Props) {
  const [pages, setPages] = useState<{ num: number; width: number; height: number; viewport: any }[]>([])
  const proxies = useRef<Record<number, any>>({})
  const canvases = useRef<Record<number, HTMLCanvasElement | null>>({})
  const drag = useRef<{ page: number; x0: number; y0: number; x1: number; y1: number } | null>(null)
  const [rubber, setRubber] = useState<{ page: number; l: number; t: number; w: number; h: number } | null>(null)

  useEffect(() => {
    let cancelled = false
    if (!doc) { setPages([]); return }
    ;(async () => {
      const ps = []
      for (let n = 1; n <= doc.numPages; n++) { const p = await doc.getPage(n); const vp = p.getViewport({ scale: SCALE }); ps.push({ num: n, width: vp.width, height: vp.height, viewport: vp, page: p }) }
      if (cancelled) return
      proxies.current = Object.fromEntries(ps.map(p => [p.num, p.page]))
      setPages(ps.map(({ num, width, height, viewport }) => ({ num, width, height, viewport })))
    })()
    return () => { cancelled = true }
  }, [doc])

  // Paint each page once its canvas exists in the DOM (runs after React commits the page elements).
  useEffect(() => {
    let cancelled = false
    ;(async () => {
      for (const p of pages) {
        const c = canvases.current[p.num]; const page = proxies.current[p.num]; if (!c || !page || cancelled) continue
        c.width = p.width; c.height = p.height
        await page.render({ canvasContext: c.getContext('2d')!, viewport: p.viewport }).promise
      }
    })()
    return () => { cancelled = true }
  }, [pages])

  const toCss = (vp: any, r: number[]) => { const [x1, y1, x2, y2] = vp.convertToViewportRectangle(r); return { left: Math.min(x1, x2), top: Math.min(y1, y2), width: Math.abs(x2 - x1), height: Math.abs(y2 - y1) } }

  const down = (e: React.PointerEvent<HTMLDivElement>, page: number) => {
    if (!drawMode || e.button !== 0) return
    const r = e.currentTarget.getBoundingClientRect()
    drag.current = { page, x0: e.clientX - r.left, y0: e.clientY - r.top, x1: e.clientX - r.left, y1: e.clientY - r.top }
    e.currentTarget.setPointerCapture(e.pointerId)
  }
  const move = (e: React.PointerEvent<HTMLDivElement>) => {
    const d = drag.current; if (!d) return
    const r = e.currentTarget.getBoundingClientRect(); d.x1 = e.clientX - r.left; d.y1 = e.clientY - r.top
    setRubber({ page: d.page, l: Math.min(d.x0, d.x1), t: Math.min(d.y0, d.y1), w: Math.abs(d.x1 - d.x0), h: Math.abs(d.y1 - d.y0) })
  }
  const up = () => {
    const d = drag.current; drag.current = null; setRubber(null); if (!d) return
    if (Math.abs(d.x1 - d.x0) < 6 || Math.abs(d.y1 - d.y0) < 6) return
    const vp = pages.find(p => p.num === d.page)!.viewport
    const [ax, ay] = vp.convertToPdfPoint(Math.min(d.x0, d.x1), Math.min(d.y0, d.y1))
    const [bx, by] = vp.convertToPdfPoint(Math.max(d.x0, d.x1), Math.max(d.y0, d.y1))
    onDraw(d.page, [Math.min(ax, bx), Math.min(ay, by), Math.max(ax, bx), Math.max(ay, by)])
  }

  return (
    <section className="stage">
      {!doc && <div className="empty">Loading template…</div>}
      {pages.map(p => (
        <div key={p.num} className={'page' + (drawMode ? ' draw' : '')} style={{ width: p.width, height: p.height }}
             onPointerDown={e => down(e, p.num)} onPointerMove={move} onPointerUp={up}>
          <canvas ref={el => { canvases.current[p.num] = el }} />
          {Object.values(fields).flatMap(f => f.rects.filter(r => r.page === p.num).map((r, i) => {
            const c = toCss(p.viewport, r.rect)
            const label = f.table ? `${tables[f.table]?.key ?? f.table}[]` : (f.key || f.id)
            return (
              <div key={f.id + i} className={'tag' + ((f.key || f.table) ? ' m' : '') + (f.drawn ? ' d' : '') + (f.table ? ' t' : '') + (selected === f.id ? ' sel' : '')}
                   style={c} title={`${f.id} · ${f.kind}`}
                   onPointerDown={e => { if (drawMode) return; e.stopPropagation(); onSelect(f.id) }}>
                <span className="lbl">{label}</span>
              </div>)
          }))}
          {rubber && rubber.page === p.num && <div className="rubber" style={{ left: rubber.l, top: rubber.t, width: rubber.w, height: rubber.h }} />}
        </div>
      ))}
    </section>
  )
}
