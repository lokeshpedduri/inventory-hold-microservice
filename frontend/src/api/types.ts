// ── DTOs — mirror the backend Contracts layer exactly ────────────────────────

/** Lifecycle state of a hold. Matches the server's HoldStatus enum. */
export type HoldStatus = 'Active' | 'Released' | 'Expired';

/** A single reserved item line within a hold. Matches HoldItemLineResponse. */
export interface HoldItemLine {
  productId: string;
  quantity: number;
}

/** Full hold state. Matches HoldResponse. */
export interface Hold {
  holdId: string;
  items: HoldItemLine[];
  status: HoldStatus;
  createdAtUtc: string; // ISO-8601 UTC
  expiresAtUtc: string; // ISO-8601 UTC
}

/** A single inventory item. Matches InventoryItemResponse. */
export interface InventoryItem {
  productId: string;
  name: string;
  /** Live sellable count — already net of active holds. */
  availableQuantity: number;
}

/** Request body for POST /api/holds. Matches CreateHoldRequest. */
export interface CreateHoldRequest {
  items: HoldItemLine[];
}

// ── Error types ───────────────────────────────────────────────────────────────

/** RFC 7807 Problem Details response shape from the API. */
export interface ProblemDetails {
  type?: string;
  title: string;
  detail?: string;
  status: number;
  instance?: string;
}

/** Typed error thrown by the API client for non-2xx responses. */
export class ApiError extends Error {
  public readonly status: number;
  public readonly detail: string | undefined;

  constructor(public readonly problem: ProblemDetails) {
    super(problem.detail ?? problem.title);
    this.name = 'ApiError';
    this.status = problem.status;
    this.detail = problem.detail;
  }
}
