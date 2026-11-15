import type { ApiKey, Application, MappingEntry, TemplateDetail, TemplateSummary, ValidationProblem } from './types'

const base = '/api/v1'

// ---- session (token kept in sessionStorage; cleared on logout or 401) ----
let token: string | null = sessionStorage.getItem('token')
const listeners = new Set<() => void>()
export const session = {
  get token() { return token },
  set(t: string | null) { token = t; t ? sessionStorage.setItem('token', t) : sessionStorage.removeItem('token'); listeners.forEach(l => l()) },
  onChange(l: () => void) { listeners.add(l); return () => { listeners.delete(l) } },
}
const authHeaders = (h: Record<string, string> = {}) => token ? { ...h, Authorization: `Bearer ${token}` } : h
async function json<T>(r: Response): Promise<T> {
  if (r.status === 401 && token) { session.set(null) }
  // 4xx bodies carry { error } and sometimes { detail } (the underlying exception) - keep the detail on the
  // Error so callers can show why, not just that, it failed.
  if (!r.ok) {
    const b = await r.json().catch(() => ({}))
    const message = b.error ?? b.detail ?? b.title ?? `${r.status} ${r.statusText}`
    throw Object.assign(new Error(message), b.error && b.detail ? { detail: b.detail as string } : {})
  }
  return r.status === 204 ? (undefined as T) : r.json()
}
// fetch with the bearer token attached
const f = (url: string, init: RequestInit = {}) => fetch(url, { ...init, headers: authHeaders(init.headers as Record<string, string>) })

export interface Me { id: string; email: string; role: string; must_change_password: boolean }
export const authApi = {
  login: (email: string, password: string) => fetch(`${base}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password }) })
    .then(r => json<{ token: string; expires_at: string; user: Me }>(r)).then(r => { session.set(r.token); return r.user }),
  me: () => f(`${base}/auth/me`).then(r => json<Me>(r)),
  changePassword: (currentPassword: string, newPassword: string) => f(`${base}/auth/change-password`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ currentPassword, newPassword }) }).then(r => json<void>(r)),
  logout: () => session.set(null),
}

export const api = {
  listApplications: () => f(`${base}/applications`).then(r => json<Application[]>(r)),
  createApplication: (name: string) =>
    f(`${base}/applications`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name }) }).then(r => json<Application>(r)),
  renameApplication: (id: string, name: string) =>
    f(`${base}/applications/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name }) }).then(r => json<Application>(r)),
  deleteApplication: (id: string) =>
    f(`${base}/applications/${id}`, { method: 'DELETE' }).then(r => json<void>(r)),

  listKeys: () => f(`${base}/keys`).then(r => json<ApiKey[]>(r)),
  createKey: (name: string, applicationIds: string[]) =>
    f(`${base}/keys`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name, application_ids: applicationIds }) })
      .then(r => json<ApiKey & { secret: string }>(r)),
  setKeyApplications: (id: string, applicationIds: string[]) =>
    f(`${base}/keys/${id}/applications`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ application_ids: applicationIds }) })
      .then(r => json<ApiKey>(r)),
  revokeKey: (id: string) => f(`${base}/keys/${id}`, { method: 'DELETE' }).then(r => json<void>(r)),

  listTemplates: (applicationId: string) =>
    f(`${base}/templates?applicationId=${encodeURIComponent(applicationId)}`).then(r => json<TemplateSummary[]>(r)),
  getTemplate: (id: string) => f(`${base}/templates/${id}`).then(r => json<TemplateDetail>(r)),
  templateFile: (id: string) => f(`${base}/templates/${id}/file`).then(r => { if (!r.ok) throw new Error(r.statusText); return r.arrayBuffer() }),

  uploadTemplate: (applicationId: string, file: File, name?: string) => {
    const fd = new FormData()
    fd.append('file', file)
    fd.append('applicationId', applicationId)
    if (name) fd.append('name', name)
    return f(`${base}/templates`, { method: 'POST', body: fd }).then(r => json<TemplateDetail>(r))
  },

  deleteTemplate: (id: string) => f(`${base}/templates/${id}`, { method: 'DELETE' }).then(r => { if (!r.ok) throw new Error(r.statusText) }),
  saveMapping: (id: string, fields: Record<string, MappingEntry>) =>
    f(`${base}/templates/${id}/mapping`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ fields }) }).then(r => json<{ mappedKeys: number }>(r)),

  validate: (applicationId: string, templateId: string, data: unknown, strict: boolean) =>
    f(`${base}/validate`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ application_id: applicationId, template_id: templateId, options: { strict }, data }),
    }).then(r => json<{ valid: boolean; problems: ValidationProblem[] }>(r)),

  /** Returns PDF bytes, or throws with the validation problems attached. */
  render: async (applicationId: string, templateId: string, data: unknown, strict: boolean, readOnly: boolean) => {
    const r = await f(`${base}/render`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ application_id: applicationId, template_id: templateId, options: { strict, read_only: readOnly }, data }),
    })
    if (r.status === 400) { const body = await r.json(); throw Object.assign(new Error('Payload rejected'), { problems: body.problems as ValidationProblem[] }) }
    if (r.status === 401) { session.set(null); throw new Error('Session expired, please sign in again') }
    if (!r.ok) throw new Error(`${r.status} ${r.statusText}`)
    return new Uint8Array(await r.arrayBuffer())
  },
}
