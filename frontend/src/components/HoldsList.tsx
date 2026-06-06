import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, queryKeys } from '../api/client';
import type { Hold } from '../api/types';
import { StatusBadge } from './shared/StatusBadge';
import { Countdown } from './shared/Countdown';
import { Spinner } from './shared/Spinner';
import { ErrorBanner } from './shared/ErrorBanner';

// ── Confirm dialog ────────────────────────────────────────────────────────────

interface ConfirmDialogProps {
  holdId: string;
  onConfirm: () => void;
  onCancel: () => void;
  isPending: boolean;
}

function ConfirmDialog({ holdId, onConfirm, onCancel, isPending }: ConfirmDialogProps) {
  return (
    <div className="dialog-backdrop" role="dialog" aria-modal="true" aria-labelledby="confirm-title">
      <div className="dialog">
        <h2 className="dialog-title" id="confirm-title">Release this hold?</h2>
        <div className="dialog-body">
          Reserved stock will be returned to inventory immediately. This cannot be undone.
          <code className="dialog-hold-id">{holdId}</code>
        </div>
        <div className="dialog-actions">
          <button className="btn btn-ghost" onClick={onCancel} disabled={isPending}>
            Cancel
          </button>
          <button
            className="btn btn-danger"
            onClick={onConfirm}
            disabled={isPending}
            aria-busy={isPending}
          >
            {isPending ? <><Spinner />Releasing…</> : 'Release Hold'}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Individual hold card ──────────────────────────────────────────────────────

interface HoldCardProps {
  hold: Hold;
  onRelease: (holdId: string) => void;
  isReleasing: boolean;
}

function HoldCard({ hold, onRelease, isReleasing }: HoldCardProps) {
  const isActive = hold.status === 'Active';

  return (
    <article className={`hold-card ${hold.status}`}>
      <div className="hold-card-header">
        <span className="hold-id" title={hold.holdId}>
          {hold.holdId}
        </span>
        <StatusBadge status={hold.status} />
      </div>

      <div className="hold-card-body">
        <div className="hold-items">
          {hold.items.map(item => (
            <div key={item.productId} className="hold-item-row">
              <span className="hold-item-qty">×{item.quantity}</span>
              <span>{item.productId}</span>
            </div>
          ))}
          <div className="timestamp" style={{ marginTop: '0.5rem' }}>
            Created {new Date(hold.createdAtUtc).toLocaleString()}
          </div>
        </div>

        <div className="hold-card-right">
          {isActive && <Countdown expiresAtUtc={hold.expiresAtUtc} />}

          {!isActive && (
            <div className="timestamp" style={{ textAlign: 'right' }}>
              Expires {new Date(hold.expiresAtUtc).toLocaleTimeString()}
            </div>
          )}

          {isActive && (
            <button
              className="btn btn-danger"
              onClick={() => onRelease(hold.holdId)}
              disabled={isReleasing}
              aria-label={`Release hold ${hold.holdId}`}
            >
              {isReleasing ? <><Spinner />Releasing…</> : 'Release'}
            </button>
          )}
        </div>
      </div>
    </article>
  );
}

// ── Holds list ────────────────────────────────────────────────────────────────

function SkeletonCard() {
  return (
    <div className="hold-card" style={{ padding: '1rem 1.25rem', display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
      <div className="skeleton skeleton-line" style={{ width: '60%' }} />
      <div className="skeleton skeleton-line" style={{ width: '40%' }} />
      <div className="skeleton skeleton-line" style={{ width: '30%' }} />
    </div>
  );
}

export function HoldsList() {
  const queryClient = useQueryClient();
  const [confirmId, setConfirmId] = useState<string | null>(null);
  const [releaseError, setReleaseError] = useState<unknown>(null);

  const { data: holds, isLoading, error } = useQuery({
    queryKey: queryKeys.holds,
    queryFn: api.getHolds,
    // Auto-refetch every 30 s so status changes from the expiry worker appear.
    refetchInterval: 30_000,
  });

  const release = useMutation({
    mutationFn: (holdId: string) => api.releaseHold(holdId),
    onSuccess: async () => {
      setConfirmId(null);
      setReleaseError(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.inventory }),
        queryClient.invalidateQueries({ queryKey: queryKeys.holds }),
      ]);
    },
    onError: (err) => {
      setConfirmId(null);
      setReleaseError(err);
    },
  });

  const activeCount = holds?.filter(h => h.status === 'Active').length ?? 0;

  return (
    <section>
      <div className="section-header">
        <span className="section-title">Holds</span>
        {holds && (
          <span className="section-count">
            {activeCount} active / {holds.length} total
          </span>
        )}
      </div>

      {error && <div className="mt-1"><ErrorBanner error={error} /></div>}
      {releaseError && <div className="mt-1"><ErrorBanner error={releaseError} /></div>}

      {isLoading && (
        <div className="holds-list">
          {Array.from({ length: 3 }).map((_, i) => <SkeletonCard key={i} />)}
        </div>
      )}

      {!isLoading && holds?.length === 0 && (
        <div className="empty-state">
          <div className="empty-state-icon">◻</div>
          <p className="empty-state-msg">No holds yet.</p>
          <p className="empty-state-hint">Create a hold from the New Hold tab.</p>
        </div>
      )}

      {holds && holds.length > 0 && (
        <div className="holds-list">
          {holds.map(hold => (
            <HoldCard
              key={hold.holdId}
              hold={hold}
              onRelease={id => { setReleaseError(null); setConfirmId(id); }}
              isReleasing={release.isPending && release.variables === hold.holdId}
            />
          ))}
        </div>
      )}

      {confirmId && (
        <ConfirmDialog
          holdId={confirmId}
          onConfirm={() => release.mutate(confirmId)}
          onCancel={() => setConfirmId(null)}
          isPending={release.isPending}
        />
      )}
    </section>
  );
}
