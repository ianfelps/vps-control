import createClient, { type Middleware } from 'openapi-fetch'
import type { components, paths } from './schema'

export type Schemas = components['schemas']
export type HostSnapshot = Schemas['HostSnapshot']
export type MetricsPoint = Schemas['MetricsPoint']
export type SourceStatus = Schemas['SourceStatus']
export type CurrentUser = Schemas['CurrentUser']
export type AuditEntry = Schemas['AuditEntryDto']
export type ContainerView = Schemas['ContainerView']
export type ContainersSnapshot = Schemas['ContainersSnapshot']
export type ContainerDetails = Schemas['ContainerDetails']
export type ContainerHealth = Schemas['ContainerHealth']
export type LogLine = Schemas['LogLine']
export type ApplicationView = Schemas['ApplicationView']
export type ApplicationDetails = Schemas['ApplicationDetails']
export type ApplicationStatus = Schemas['ApplicationStatus']
export type Alert = Schemas['Alert']
export type ContainerAction = 'start' | 'stop' | 'restart'

export const PERMISSIONS = {
  containersManage: 'containers.manage',
  secretsReveal: 'secrets.reveal',
  applicationsEdit: 'applications.edit',
} as const

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

const UNSAFE = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

function readCookie(name: string): string | undefined {
  return document.cookie
    .split('; ')
    .find((c) => c.startsWith(`${name}=`))
    ?.slice(name.length + 1)
}

/** Garante o cookie XSRF-TOKEN (double-submit) antes de mutações. */
async function ensureXsrfToken(): Promise<string | undefined> {
  let token = readCookie('XSRF-TOKEN')
  if (!token) {
    await fetch('/api/auth/antiforgery', { credentials: 'same-origin' })
    token = readCookie('XSRF-TOKEN')
  }
  return token ? decodeURIComponent(token) : undefined
}

const antiforgery: Middleware = {
  async onRequest({ request }) {
    if (UNSAFE.has(request.method)) {
      const token = await ensureXsrfToken()
      if (token) request.headers.set('X-XSRF-TOKEN', token)
    }
    return request
  },
}

export const api = createClient<paths>({ baseUrl: '', credentials: 'same-origin' })
api.use(antiforgery)

/** Desembrulha a resposta do openapi-fetch, lançando ApiError em falha. */
export async function unwrap<T>(
  promise: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<T> {
  const { data, error, response } = await promise
  if (!response.ok) {
    const detail =
      (error as { detail?: string; error?: string } | undefined)?.detail ??
      (error as { error?: string } | undefined)?.error ??
      response.statusText
    throw new ApiError(response.status, detail || `HTTP ${response.status}`)
  }
  return data as T
}
