import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api';
import { useTranslation } from '../../i18n/LanguageContext';

const PAGE_SIZE = 50;

const statusStyles = {
  received: { dot: 'bg-blue-400', badge: 'bg-blue-50 text-blue-700' },
  allocated: { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  partially_allocated: { dot: 'bg-amber-400', badge: 'bg-amber-50 text-amber-700' },
  refunded: { dot: 'bg-purple-400', badge: 'bg-purple-50 text-purple-700' },
};

const methodStyles = {
  bank_transfer: 'bg-blue-50 text-blue-700',
  direct_debit: 'bg-teal-50 text-teal-700',
  manual: 'bg-slate-50 text-slate-700',
  refund: 'bg-rose-50 text-rose-700',
};

export default function PaymentsTab({ customerId, customerName }) {
  const { t } = useTranslation();
  const [data, setData] = useState(null);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const fetch = useCallback((p) => {
    setLoading(true);
    api.getPayments({ customerId, page: p, pageSize: PAGE_SIZE })
      .then(setData)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [customerId]);

  useEffect(() => { fetch(page); }, [page, fetch]);

  const items = data?.items ?? [];
  const total = data?.totalCount ?? 0;
  const pages = Math.ceil(total / PAGE_SIZE);

  return (
    <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden animate-fade-in-up">
      {error && <div className="p-4 bg-rose-50 border-b border-rose-100 text-rose-700 text-sm">{error}</div>}
      <div className="overflow-x-auto">
        <table className="w-full">
          <thead>
            <tr className="bg-slate-50 border-b border-slate-200">
              <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('payments.colReference')}</th>
              <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('payments.colMethod')}</th>
              <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('payments.colStatus')}</th>
              <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('payments.colAmount')}</th>
              <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('payments.colAllocated')}</th>
              <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('payments.colUnallocated')}</th>
              <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('payments.colReceivedAt')}</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {loading && items.length === 0 ? (
              <tr><td colSpan="7" className="px-4 py-12 text-center text-slate-500">{t('common.loading')}</td></tr>
            ) : items.length === 0 ? (
              <tr><td colSpan="7" className="px-4 py-12 text-center text-slate-500">{t('customerDetail.noPayments')}</td></tr>
            ) : (
              items.map((pay) => {
                const sCfg = statusStyles[pay.status] || statusStyles.received;
                return (
                  <tr key={pay.id} className="hover:bg-slate-50 transition-colors">
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <Link to={`/payments/${pay.id}?from=${encodeURIComponent('/customers/' + customerId + '?tab=payments')}&fromLabel=${encodeURIComponent(customerName)}`} className="text-sm text-teal-600 font-medium hover:text-teal-700">
                        {pay.paymentReference || pay.id.slice(0, 8)}
                      </Link>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <span className={`inline-flex px-2 py-0.5 rounded-full text-[11px] font-medium ${methodStyles[pay.method] || 'bg-slate-100 text-slate-600'}`}>
                        {t(`payments.method_${pay.method}`)}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${sCfg.badge}`}>
                        <span className={`w-1.5 h-1.5 rounded-full ${sCfg.dot}`} />
                        {t(`payments.status_${pay.status}`)}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums font-semibold text-slate-900">{pay.amount?.toFixed(2)} DKK</td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums text-emerald-600">{pay.allocatedAmount?.toFixed(2)}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums text-slate-500">{pay.unallocatedAmount?.toFixed(2)}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-500">{pay.receivedAt ? new Date(pay.receivedAt).toLocaleString() : <span className="text-slate-300">&mdash;</span>}</td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      {pages > 1 && (
        <div className="px-5 py-3.5 bg-slate-50 border-t border-slate-200 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2">
          <div className="text-sm text-slate-600">
            {t('common.showingRange', { from: (page - 1) * PAGE_SIZE + 1, to: Math.min(page * PAGE_SIZE, total), total })} {t('payments.showingPayments')}
          </div>
          <div className="flex gap-2">
            <button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1}
              className="px-3 py-1.5 text-sm font-medium rounded-lg bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors">
              {t('common.previous')}
            </button>
            <button onClick={() => setPage(p => Math.min(pages, p + 1))} disabled={page === pages}
              className="px-3 py-1.5 text-sm font-medium rounded-lg bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors">
              {t('common.next')}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
