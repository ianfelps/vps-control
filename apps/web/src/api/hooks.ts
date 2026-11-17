import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, unwrap, type CurrentUser } from './client'

export const queryKeys = {
  me: ['auth', 'me'] as const,
  snapshot: ['system', 'snapshot'] as const,
  history: ['system', 'history'] as const,
  status: ['system', 'status'] as const,
  audit: ['audit'] as const,
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
