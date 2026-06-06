import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, queryKeys } from '../api/client';
import type { InventoryItem } from '../api/types';
import { Spinner } from './shared/Spinner';
import { ErrorBanner } from './shared/ErrorBanner';

interface Props {
  /** Called after a hold is successfully created so the parent can switch tabs. */
  onHoldCreated: () => void;
}

/** Map of productId → requested quantity (0 = not selected). */
type Lines = Record<string, number>;

function ProductRow({
  item,
  quantity,
  onChangeQty,
}: {
  item: InventoryItem;
  quantity: number;
  onChangeQty: (delta: number) => void;
}) {
  const max = item.availableQuantity;

  return (
    <div className="product-row">
      <div className="product-row-info">
        <div className="product-row-name">{item.name}</div>
        <div className="product-row-meta">{item.productId}</div>
      </div>

      <span className="product-row-avail">{max} avail.</span>

      <div className="stepper" role="group" aria-label={`Quantity for ${item.name}`}>
        <button
          className="stepper-btn"
          onClick={() => onChangeQty(-1)}
          disabled={quantity === 0}
          aria-label="Decrease"
        >
          −
        </button>
        <span className={`stepper-val${quantity === 0 ? ' zero' : ''}`}>
          {quantity}
        </span>
        <button
          className="stepper-btn"
          onClick={() => onChangeQty(1)}
          disabled={quantity >= max}
          aria-label="Increase"
        >
          +
        </button>
      </div>
    </div>
  );
}

export function CreateHoldForm({ onHoldCreated }: Props) {
  const queryClient = useQueryClient();
  const [lines, setLines] = useState<Lines>({});
  const [successId, setSuccessId] = useState<string | null>(null);

  const { data: inventory, isLoading: invLoading, error: invError } = useQuery({
    queryKey: queryKeys.inventory,
    queryFn: api.getInventory,
  });

  const mutation = useMutation({
    mutationFn: api.createHold,
    onSuccess: async (hold) => {
      setSuccessId(hold.holdId);
      setLines({});
      // Invalidate both queries so inventory quantities and holds list refresh.
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.inventory }),
        queryClient.invalidateQueries({ queryKey: queryKeys.holds }),
      ]);
      // Navigate to the holds tab after a brief success flash.
      setTimeout(onHoldCreated, 900);
    },
  });

  function handleQty(productId: string, delta: number) {
    setLines(prev => {
      const cur = prev[productId] ?? 0;
      const next = Math.max(0, cur + delta);
      return { ...prev, [productId]: next };
    });
    // Clear any prior success/error state when the user adjusts quantities.
    setSuccessId(null);
    mutation.reset();
  }

  const selectedItems = Object.entries(lines)
    .filter(([, qty]) => qty > 0)
    .map(([productId, quantity]) => ({ productId, quantity }));

  function handleSubmit() {
    if (selectedItems.length === 0) return;
    mutation.mutate({ items: selectedItems });
  }

  const nameFor = (productId: string): string =>
    inventory?.find(i => i.productId === productId)?.name ?? productId;

  return (
    <section>
      <div className="section-header">
        <span className="section-title">New Hold</span>
        <span className="section-count">
          {selectedItems.length} item{selectedItems.length !== 1 ? 's' : ''} selected
        </span>
      </div>

      {invError && <div className="mt-1"><ErrorBanner error={invError} /></div>}

      <div className="hold-form">
        {/* ── Left: product picker ── */}
        <div className="hold-form-panel">
          <div className="hold-form-panel-header">Select Items</div>

          {invLoading && (
            <div style={{ padding: '1.5rem', display: 'flex', justifyContent: 'center' }}>
              <Spinner label="Loading products…" />
            </div>
          )}

          {inventory?.map(item => (
            <ProductRow
              key={item.productId}
              item={item}
              quantity={lines[item.productId] ?? 0}
              onChangeQty={delta => handleQty(item.productId, delta)}
            />
          ))}
        </div>

        {/* ── Right: summary + submit ── */}
        <div className="hold-form-panel">
          <div className="hold-form-panel-header">Hold Summary</div>

          <div className="hold-summary">
            {selectedItems.length === 0 ? (
              <div className="summary-empty">
                Use the steppers to add items to your hold.
              </div>
            ) : (
              selectedItems.map(({ productId, quantity }) => (
                <div key={productId} className="summary-item">
                  <span className="summary-item-name">{nameFor(productId)}</span>
                  <span className="summary-item-qty">×{quantity}</span>
                </div>
              ))
            )}
          </div>

          <div className="summary-footer">
            {mutation.isError && <ErrorBanner error={mutation.error} />}

            {successId && (
              <div className="success-banner">
                <span>✓</span>
                <span>
                  Hold placed —{' '}
                  <span style={{ fontFamily: 'var(--font-mono)', fontSize: '0.78em' }}>
                    {successId.slice(0, 16)}…
                  </span>
                </span>
              </div>
            )}

            <button
              className="btn btn-primary"
              onClick={handleSubmit}
              disabled={selectedItems.length === 0 || mutation.isPending}
              aria-busy={mutation.isPending}
            >
              {mutation.isPending ? (
                <>
                  <Spinner label="Placing hold…" />
                  Placing hold…
                </>
              ) : (
                'Place Hold'
              )}
            </button>
          </div>
        </div>
      </div>
    </section>
  );
}
