import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  api,
  ApiError,
  unwrap,
  type ContainerAction,
  type ContainersSnapshot,
  type CurrentUser,
} from './client'

export const queryKeys = {
  me: ['auth', 'me'] as const,
  snapshot: ['system', 'snapshot'] as const,
  history: ['system', 'history'] as const,
  status: ['system', 'status'] as const,
  audit: ['audit'] as const,
  containers: ['containers'] as const,
  container: (id: string) => ['containers', id] as const,
  applications: ['applications'] as const,
  application: (slug: string) => ['applications', slug] as const,
  alerts: ['alerts'] as const,
}

export function useMe() {
  return useQuery({
    queryKey: queryKeys.me,
    queryFn: async (): Promise<CurrentUser | null> => {
      try {
        return await unwrap(api.GET('/api/auth/me'))
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) return null
        throw e
      }
    },
    staleTime: 60_000,
  })
}

export function useHasPermission(permission: string) {
  const { data } = useMe()
  return data?.permissions.includes(permission) ?? false
}

export function useLogin() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: { username: string; password: string }) =>
      unwrap(api.POST('/api/auth/login', { body })),
    onSuccess: (user) => qc.setQueryData(queryKeys.me, user),
  })
}

export function useLogout() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/auth/logout')),
    onSettled: () => {
      qc.clear()
      qc.setQueryData(queryKeys.me, null)
    },
  })
}

/** Polling serve de fallback; o SignalR atualiza o cache em tempo real. */
export function useSnapshot() {
  return useQuery({
    queryKey: queryKeys.snapshot,
    queryFn: () => unwrap(api.GET('/api/system/snapshot')),
    refetchInterval: 15_000,
    retry: 1,
  })
}

export function useHistory() {
  return useQuery({
    queryKey: queryKeys.history,
    queryFn: () => unwrap(api.GET('/api/system/history')),
    staleTime: Infinity,
  })
}

export function useSourceStatus() {
  return useQuery({
    queryKey: queryKeys.status,
    queryFn: () => unwrap(api.GET('/api/system/status')),
    refetchInterval: 15_000,
  })
}

export function useAudit(limit = 100) {
  return useQuery({
    queryKey: [...queryKeys.audit, limit],
    queryFn: () => unwrap(api.GET('/api/audit', { params: { query: { limit } } })),
  })
}

// ---------- Containers ----------

export function useContainers() {
  return useQuery({
    queryKey: queryKeys.containers,
    queryFn: () => unwrap(api.GET('/api/containers')),
    refetchInterval: 30_000,
  })
}

export function useContainer(id: string) {
  return useQuery({
    queryKey: queryKeys.container(id),
    queryFn: () => unwrap(api.GET('/api/containers/{id}', { params: { path: { id } } })),
    retry: (count, e) => !(e instanceof ApiError && e.status === 404) && count < 2,
  })
}

export function useRevealEnv(id: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/containers/{id}/env/reveal', { params: { path: { id } } })),
    onSuccess: (details) => qc.setQueryData(queryKeys.container(id), details),
    onSettled: () => qc.invalidateQueries({ queryKey: queryKeys.audit }),
  })
}

export function useContainerAction() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: ContainerAction }) =>
      unwrap(api.POST('/api/containers/{id}/{action}', { params: { path: { id, action } } })),
    onSettled: (_data, _err, { id }) => {
      void qc.invalidateQueries({ queryKey: queryKeys.containers })
      void qc.invalidateQueries({ queryKey: queryKeys.container(id) })
      void qc.invalidateQueries({ queryKey: queryKeys.applications })
      void qc.invalidateQueries({ queryKey: queryKeys.audit })
    },
  })
}

// ---------- Aplicações ----------

export function useApplications() {
  return useQuery({
    queryKey: queryKeys.applications,
    queryFn: () => unwrap(api.GET('/api/applications')),
    refetchInterval: 10_000,
  })
}

export function useApplication(slug: string) {
  return useQuery({
    queryKey: queryKeys.application(slug),
    queryFn: () => unwrap(api.GET('/api/applications/{slug}', { params: { path: { slug } } })),
    refetchInterval: 10_000,
  })
}

export function useUpdateApplication(slug: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: { displayName: string | null; description: string | null; url: string | null }) =>
      unwrap(api.PUT('/api/applications/{slug}', { params: { path: { slug } }, body })),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: queryKeys.applications })
      void qc.invalidateQueries({ queryKey: queryKeys.audit })
    },
  })
}

// ---------- Alertas ----------

export function useAlerts() {
  return useQuery({
    queryKey: queryKeys.alerts,
    queryFn: () => unwrap(api.GET('/api/alerts')),
    refetchInterval: 10_000,
  })
}

export function selectContainerCounts(snapshot: ContainersSnapshot | undefined) {
  const items = snapshot?.items ?? []
  return {
    total: items.length,
    running: items.filter((c) => c.state === 'running').length,
    stopped: items.filter((c) => c.state !== 'running').length,
    unhealthy: items.filter((c) => c.health === 'unhealthy').length,
  }
}
