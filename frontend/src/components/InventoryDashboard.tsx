import { useQuery } from '@tanstack/react-query';
import { api, queryKeys } from '../api/client';
import type { InventoryItem } from '../api/types';
import { ErrorBanner } from './shared/ErrorBanner';

function qtyClass(qty: number): string {
  if (qty === 0) return 'empty';
  if (qty <= 5) return 'low';
  return '';
}

function InvCard({ item }: { item: InventoryItem }) {
  return (
    <div className="inv-card">
      <span className="inv-product-id">{item.productId}</span>
      <div className="inv-qty-wrap">
        <span className={`inv-qty ${qtyClass(item.availableQuantity)}`}>
          {item.availableQuantity}
        </span>
        <span className="inv-qty-label">units available</span>
      </div>
      <span className="inv-name">{item.name}</span>
    </div>
  );
}

function SkeletonGrid() {
  return (
    <div className="inventory-grid" aria-busy="true" aria-label="Loading inventory">
      {Array.from({ length: 7 }).map((_, i) => (
        <div key={i} className="skeleton-card">
          <div className="skeleton skeleton-line" style={{ width: '45%' }} />
          <div className="skeleton skeleton-num" />
          <div className="skeleton skeleton-line" style={{ width: '70%' }} />
        </div>
      ))}
    </div>
  );
}

export function InventoryDashboard() {
  const { data, isLoading, error, dataUpdatedAt } = useQuery({
    queryKey: queryKeys.inventory,
    queryFn: api.getInventory,
    refetchInterval: 30_000,
  });

  const updatedAt = dataUpdatedAt
    ? new Date(dataUpdatedAt).toLocaleTimeString()
    : null;

  return (
    <section>
      <div className="section-header">
        <span className="section-title">Inventory</span>
        {data && <span className="section-count">{data.length} products</span>}
        {updatedAt && (
          <span className="timestamp" style={{ marginLeft: 'auto' }}>
            Updated {updatedAt}
          </span>
        )}
      </div>

      {error && <div className="mt-1"><ErrorBanner error={error} /></div>}

      {isLoading ? (
        <SkeletonGrid />
      ) : (
        <div className="inventory-grid">
          {data?.map(item => <InvCard key={item.productId} item={item} />)}
        </div>
      )}
    </section>
  );
}
