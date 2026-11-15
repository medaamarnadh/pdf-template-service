import * as pdfjs from 'pdfjs-dist'
import type { PDFDocumentProxy } from 'pdfjs-dist'
pdfjs.GlobalWorkerOptions.workerSrc = new URL('pdfjs-dist/build/pdf.worker.min.mjs', import.meta.url).toString()

export const SCALE = 1.15
export const loadPdf = (data: Uint8Array) => pdfjs.getDocument({ data }).promise
export type { PDFDocumentProxy }

/** All widget annotations per field name, with page and PDF rect, so overlays match every widget (radios, repeated headers). */
export async function widgetRects(doc: PDFDocumentProxy) {
  const out: Record<string, { page: number; rect: number[] }[]> = {}
  for (let n = 1; n <= doc.numPages; n++) {
    const page = await doc.getPage(n)
    for (const a of await page.getAnnotations()) {
      if (a.subtype !== 'Widget' || !a.fieldName) continue
      ;(out[a.fieldName] ??= []).push({ page: n, rect: a.rect as number[] })
    }
  }
  return out
}
