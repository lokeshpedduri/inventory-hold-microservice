import type { CreateHoldRequest, Hold, InventoryItem, ProblemDetails } from './types';
import { ApiError } from './types';

// In dev, Vite proxies /api → http://localhost:8080 (see vite.config.ts).
// In production, the request goes to the same origin the frontend is served from.
const BASE = '/api';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...init?.headers,
    },
  });

  if (!res.ok) {
    // Parse Problem Details (RFC 7807); fall back to a generic shape on parse failure.
    const problem = await res
      .json()
      .catch((): ProblemDetails => ({ title: 'Request failed', status: res.status })) as ProblemDetails;
    throw new ApiError(problem);
  }

  // 204 No Content
  if (res.status === 204) return undefined as unknown as T;

  return res.json() as Promise<T>;
}

// ── Typed API surface ─────────────────────────────────────────────────────────

export const api = {
  /** GET /api/inventory — returns all inventory items (cache-aside via Redis). */
  getInventory: (): Promise<InventoryItem[]> =>
    request<InventoryItem[]>('/inventory'),

  /** GET /api/holds — returns all holds ordered Active-first. */
  getHolds: (): Promise<Hold[]> =>
    request<Hold[]>('/holds'),

  /** GET /api/holds/:id — returns a single hold, applying lazy expiry inline. */
  getHold: (holdId: string): Promise<Hold> =>
    request<Hold>(`/holds/${holdId}`),

  /** POST /api/holds — places a new hold; returns 201 + hold or 409 on insufficient stock. */
  createHold: (body: CreateHoldRequest): Promise<Hold> =>
    request<Hold>('/holds', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  /** DELETE /api/holds/:id — releases an active hold; returns 200 + hold or 409 if terminal. */
  releaseHold: (holdId: string): Promise<Hold> =>
    request<Hold>(`/holds/${holdId}`, { method: 'DELETE' }),
};

// ── Query key factory (keeps keys consistent and refactorable) ────────────────
export const queryKeys = {
  inventory: ['inventory'] as const,
  holds: ['holds'] as const,
  hold: (holdId: string) => ['holds', holdId] as const,
};
