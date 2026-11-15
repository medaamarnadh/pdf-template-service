export type FieldKind = 'text' | 'checkbox' | 'radio' | 'choice' | 'image' | 'table'

/** A field as the API discovered it in the PDF. */
export interface DiscoveredField {
  id: string; type: FieldKind; page: number; rect: number[]; tooltip: string
  options: string[]; maxLength: number | null; multiline: boolean; widgetCount: number
}

/** Editor state for one PDF field (detected) or one drawn box. */
export interface FieldState {
  id: string; kind: FieldKind; rects: { page: number; rect: number[] }[]
  options: string[]; tooltip: string; key: string; required: boolean; format: string
  drawn: boolean; maxLength: number; imgTypes: string; imgMaxKb: number; multiline: boolean
  table: string | null            // name of the detected table this field belongs to
  columns: string; rowHeight: number; header: boolean   // drawn table box
}

/** A detected repeating-row table (Budget1_Category, Budget2_Category ...). */
export interface TableState {
  name: string; key: string; required: boolean; rows: number[]
  columns: Record<string, string>; fieldIds: string[]
}

export interface TemplateSummary { id: string; applicationId: string; name: string; fileName: string; pageCount: number; mappedKeys: number; updatedAt: string }
export interface TemplateDetail extends TemplateSummary { fields: DiscoveredField[]; mapping: { fields?: Record<string, MappingEntry> } }

/** The mapping contract stored with the template and consumed by the engine. */
export interface MappingEntry {
  field?: string | string[]; type: FieldKind; required?: boolean; maxLength?: number; format?: string
  values?: string[]; imageTypes?: string[]; maxKb?: number
  rows?: number; columns?: Record<string, string> | string[]
  box?: { page: number; rect: number[] }; rowHeight?: number; header?: boolean
}
export interface ValidationProblem { key: string; code: string; message: string; severity: 'error' | 'warning' }

// Applications and keys are new endpoints and speak snake_case (CLAUDE.md); the older
// template endpoints still speak camelCase, so TemplateSummary above is unchanged.
export interface Application {
  application_id: string
  name: string
  template_count: number
  created_at: string
  updated_at: string
}

export interface ApiKey {
  api_key_id: string
  name: string
  prefix: string
  applications: { application_id: string; name: string }[]
  created_at: string
  last_used_at: string | null
  revoked_at: string | null
}
