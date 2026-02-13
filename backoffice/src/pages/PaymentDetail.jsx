import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';
import Breadcrumb from '../components/Breadcrumb';
import WattzonLoader from '../components/WattzonLoader';

const statusStyles = {
  received: { dot: 'bg-blue-400', badge: 'bg-blue-50 text-blue-700' },
  allocated: { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  partially_allocated: { dot: 'bg-amber-400', badge: 'bg-amber-50 text-amber-700' },
  refunded: { dot: 'bg-purple-400', badge: 'bg-purple-50 text-purple-700' },
};

export default function PaymentDetail() {
  const { t } = useTranslation();
  const { id } = useParams();
  const [payment, setPayment] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    api.getPayment(id)
      .then(setPayment)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [id]);

  if (loading) {
    return (
      <WattzonLoader message={t('paymentDetail.loading')} />
    );
  }

  if (!payment) {
    return (
      <div className="p-8 text-center text-slate-500">{t('common.notFound')}</div>
    );
  }

  const cfg = statusStyles[payment.status] || statusStyles.received;
  const allocations = payment.allocations ?? [];

  return (
    <div className="p-4 sm:p-8 max-w-5xl mx-auto">
      <Breadcrumb
        fallback={[{ label: t('payments.title'), to: '/payments' }]}
        current={payment.paymentReference || id.substring(0, 8)}
      />

      {error && (
        <div className="mb-4 p-3 bg-rose-50 border border-rose-200 rounded-lg text-sm text-rose-700">{error}</div>
      )}

      {/* Header */}
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">
          {payment.paymentReference || t('paymentDetail.title')}
        </h1>
        <div className="flex items-center gap-3 mt-2">
          <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${cfg.badge}`}>
            <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />
            {t(`payments.status_${payment.status}`)}
          </span>
          <span className="text-sm text-slate-500">{t(`payments.method_${payment.paymentMethod}`)}</span>
        </div>
      </div>

      {/* Summary cards */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <div className="bg-gradient-to-br from-white to-slate-50 rounded-xl p-4 shadow-sm border border-slate-100">
          <div className="text-xs font-medium text-slate-500 mb-1">{t('paymentDetail.amount')}</div>
          <div className="text-xl font-bold text-slate-900">{(payment.amount || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK</div>
        </div>
        <div className="bg-gradient-to-br from-white to-emerald-50/30 rounded-xl p-4 shadow-sm border border-emerald-100/50">
          <div className="text-xs font-medium text-emerald-600 mb-1">{t('paymentDetail.allocated')}</div>
          <div className="text-xl font-bold text-emerald-700">{(payment.amountAllocated || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK</div>
        </div>
        <div className="bg-gradient-to-br from-white to-amber-50/30 rounded-xl p-4 shadow-sm border border-amber-100/50">
          <div className="text-xs font-medium text-amber-600 mb-1">{t('paymentDetail.unallocated')}</div>
          <div className="text-xl font-bold text-amber-700">{(payment.amountUnallocated || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK</div>
        </div>
      </div>

      {/* Payment info */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-5 mb-6 animate-fade-in-up" style={{ animationDelay: '80ms' }}>
        <h2 className="text-sm font-semibold text-slate-900 mb-4">{t('paymentDetail.info')}</h2>
        <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-8 gap-y-3">
          <div>
            <dt className="text-xs font-medium text-slate-500">{t('paymentDetail.paymentId')}</dt>
            <dd className="text-sm text-slate-900 mt-0.5 font-mono">{payment.id}</dd>
          </div>
          {payment.customerId && (
            <div>
              <dt className="text-xs font-medium text-slate-500">{t('paymentDetail.customer')}</dt>
              <dd className="text-sm mt-0.5">
                <Link to={`/customers/${payment.customerId}?from=/payments/${id}`} className="text-teal-600 hover:text-teal-700">{payment.customerId.substring(0, 8)}...</Link>
              </dd>
            </div>
          )}
          {payment.externalId && (
            <div>
              <dt className="text-xs font-medium text-slate-500">{t('paymentDetail.externalId')}</dt>
              <dd className="text-sm text-slate-900 mt-0.5 font-mono">{payment.externalId}</dd>
            </div>
          )}
          <div>
            <dt className="text-xs font-medium text-slate-500">{t('paymentDetail.receivedAt')}</dt>
            <dd className="text-sm text-slate-900 mt-0.5">{payment.receivedAt ? new Date(payment.receivedAt).toLocaleString() : '—'}</dd>
          </div>
          {payment.valueDate && (
            <div>
              <dt className="text-xs font-medium text-slate-500">{t('paymentDetail.valueDate')}</dt>
              <dd className="text-sm text-slate-900 mt-0.5">{payment.valueDate}</dd>
            </div>
          )}
        </dl>
      </div>

      {/* Allocations */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        <div className="px-5 py-4 border-b border-slate-200">
          <h2 className="text-sm font-semibold text-slate-900">{t('paymentDetail.allocations')}</h2>
          <p className="text-xs text-slate-500 mt-0.5">{allocations.length} {t('paymentDetail.allocationsCount')}</p>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="bg-slate-50 border-b border-slate-200">
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('paymentDetail.colInvoice')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('paymentDetail.colAllocatedAmount')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('paymentDetail.colAllocatedAt')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('paymentDetail.colAllocatedBy')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {allocations.length === 0 ? (
                <tr>
                  <td colSpan="4" className="px-4 py-8 text-center text-slate-500">{t('paymentDetail.noAllocations')}</td>
                </tr>
              ) : (
                allocations.map((a, i) => (
                  <tr key={a.id || i} className="hover:bg-slate-50 transition-colors">
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <Link to={`/invoices/${a.invoiceId}`} className="text-teal-600 font-medium hover:text-teal-700 text-sm">
                        {a.invoiceId?.substring(0, 8)}...
                      </Link>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums font-semibold text-slate-900">
                      {(a.amount || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-500">
                      {a.allocatedAt ? new Date(a.allocatedAt).toLocaleString() : '—'}
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-500">
                      {a.allocatedBy || 'auto'}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
