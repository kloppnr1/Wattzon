import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';
import WattzonLoader from '../components/WattzonLoader';

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

function StatusBadge({ status, t }) {
  const cfg = statusStyles[status] || statusStyles.received;
  return (
    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${cfg.badge}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />
      {t(`payments.status_${status}`)}
    </span>
  );
}

export default function PaymentList() {
  const { t } = useTranslation();
  const [data, setData] = useState(null);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const [status, setStatus] = useState('');
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');

  // Record payment modal
  const [showModal, setShowModal] = useState(false);
  const [modalData, setModalData] = useState({ customerId: '', paymentMethod: 'bank_transfer', paymentReference: '', amount: '' });
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState(null);

  const fetchPage = useCallback((p) => {
    setError(null);
    api.getPayments({
      status: status || undefined,
      fromDate: fromDate || undefined,
      toDate: toDate || undefined,
      page: p,
      pageSize: PAGE_SIZE,
    })
      .then(setData)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [status, fromDate, toDate]);

  useEffect(() => { setPage(1); }, [status, fromDate, toDate]);
  useEffect(() => { fetchPage(page); }, [page, fetchPage]);

  const payments = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = Math.ceil(totalCount / PAGE_SIZE);

  const handleRecordPayment = async (e) => {
    e.preventDefault();
    setSubmitting(true);
    setSubmitError(null);
    try {
      await api.createPayment({
        customerId: modalData.customerId,
        paymentMethod: modalData.paymentMethod,
        paymentReference: modalData.paymentReference || null,
        amount: parseFloat(modalData.amount),
      });
      setShowModal(false);
      setModalData({ customerId: '', paymentMethod: 'bank_transfer', paymentReference: '', amount: '' });
      fetchPage(page);
    } catch (err) {
      setSubmitError(err.message);
    } finally {
      setSubmitting(false);
    }
  };

  if (loading) {
    return (
      <WattzonLoader message={t('payments.loading')} />
    );
  }

  return (
    <div className="p-4 sm:p-8 max-w-6xl mx-auto">
      <div className="mb-6 animate-fade-in-up flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <div>
          <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">{t('payments.title')}</h1>
          <p className="text-base text-slate-500 mt-1">{t('payments.subtitle')}</p>
        </div>
        <button
          onClick={() => setShowModal(true)}
          className="inline-flex items-center gap-2 px-4 py-2.5 rounded-xl bg-teal-600 text-white text-sm font-medium hover:bg-teal-700 transition-colors shadow-sm"
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
          </svg>
          {t('payments.recordPayment')}
        </button>
      </div>

      {/* Filters */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-4 mb-6 animate-fade-in-up" style={{ animationDelay: '40ms' }}>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
          <select
            value={status}
            onChange={(e) => setStatus(e.target.value)}
            className="px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
          >
            <option value="">{t('payments.allStatuses')}</option>
            <option value="received">{t('payments.status_received')}</option>
            <option value="allocated">{t('payments.status_allocated')}</option>
            <option value="partially_allocated">{t('payments.status_partially_allocated')}</option>
            <option value="refunded">{t('payments.status_refunded')}</option>
          </select>
          <input
            type="date"
            value={fromDate}
            onChange={(e) => setFromDate(e.target.value)}
            className="px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
          />
          <input
            type="date"
            value={toDate}
            onChange={(e) => setToDate(e.target.value)}
            className="px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
          />
        </div>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <div className="bg-gradient-to-br from-white to-slate-50 rounded-xl p-5 shadow-sm border border-slate-100">
          <div className="text-sm font-medium text-slate-500 mb-1">{t('payments.totalPayments')}</div>
          <div className="text-3xl font-bold text-slate-900">{totalCount}</div>
        </div>
        <div className="bg-gradient-to-br from-white to-emerald-50/30 rounded-xl p-5 shadow-sm border border-emerald-100/50">
          <div className="text-sm font-medium text-emerald-600 mb-1">{t('payments.totalReceived')}</div>
          <div className="text-3xl font-bold text-emerald-700">
            {payments.reduce((s, p) => s + (p.amount || 0), 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK
          </div>
        </div>
      </div>

      {/* Table */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        {error && (
          <div className="p-4 bg-rose-50 border-b border-rose-100 text-rose-700 text-sm">
            {t('common.error')}: {error}
          </div>
        )}

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
              {payments.length === 0 ? (
                <tr>
                  <td colSpan="7" className="px-4 py-12 text-center text-slate-500">
                    {t('payments.noPayments')}
                  </td>
                </tr>
              ) : (
                payments.map((p) => (
                  <tr key={p.id} className="hover:bg-slate-50 transition-colors">
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <Link to={`/payments/${p.id}`} className="text-teal-600 font-medium hover:text-teal-700 text-sm">
                        {p.paymentReference || p.id.substring(0, 8) + '...'}
                      </Link>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <span className={`inline-flex px-2 py-0.5 rounded-full text-[11px] font-medium ${methodStyles[p.paymentMethod] || methodStyles.manual}`}>
                        {t(`payments.method_${p.paymentMethod}`)}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <StatusBadge status={p.status} t={t} />
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums font-semibold text-slate-900">
                      {(p.amount || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums text-emerald-700">
                      {(p.amountAllocated || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums text-slate-600">
                      {(p.amountUnallocated || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-500">
                      {p.receivedAt ? new Date(p.receivedAt).toLocaleString() : '—'}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {totalPages > 1 && (
          <div className="px-5 py-3.5 bg-slate-50 border-t border-slate-200 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2">
            <div className="text-sm text-slate-600">
              {t('common.showingRange', { from: (page - 1) * PAGE_SIZE + 1, to: Math.min(page * PAGE_SIZE, totalCount), total: totalCount })} {t('payments.showingPayments')}
            </div>
            <div className="flex gap-2">
              <button
                onClick={() => setPage(p => Math.max(1, p - 1))}
                disabled={page === 1}
                className="px-3 py-1.5 text-sm font-medium rounded-lg bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                {t('common.previous')}
              </button>
              <button
                onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                disabled={page === totalPages}
                className="px-3 py-1.5 text-sm font-medium rounded-lg bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                {t('common.next')}
              </button>
            </div>
          </div>
        )}
      </div>

      {/* Record Payment Modal */}
      {showModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="bg-white rounded-2xl shadow-xl w-full max-w-md mx-4 p-6 animate-fade-in-up">
            <h2 className="text-lg font-semibold text-slate-900 mb-4">{t('payments.recordPayment')}</h2>

            {submitError && (
              <div className="mb-4 p-3 bg-rose-50 border border-rose-200 rounded-lg text-sm text-rose-700">{submitError}</div>
            )}

            <form onSubmit={handleRecordPayment} className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-1">{t('payments.modalCustomerId')}</label>
                <input
                  type="text"
                  required
                  value={modalData.customerId}
                  onChange={(e) => setModalData(d => ({ ...d, customerId: e.target.value }))}
                  className="w-full px-3 py-2 rounded-lg border border-slate-200 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-1">{t('payments.modalMethod')}</label>
                <select
                  value={modalData.paymentMethod}
                  onChange={(e) => setModalData(d => ({ ...d, paymentMethod: e.target.value }))}
                  className="w-full px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
                >
                  <option value="bank_transfer">{t('payments.method_bank_transfer')}</option>
                  <option value="direct_debit">{t('payments.method_direct_debit')}</option>
                  <option value="manual">{t('payments.method_manual')}</option>
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-1">{t('payments.modalReference')}</label>
                <input
                  type="text"
                  value={modalData.paymentReference}
                  onChange={(e) => setModalData(d => ({ ...d, paymentReference: e.target.value }))}
                  className="w-full px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-1">{t('payments.modalAmount')}</label>
                <input
                  type="number"
                  step="0.01"
                  required
                  value={modalData.amount}
                  onChange={(e) => setModalData(d => ({ ...d, amount: e.target.value }))}
                  className="w-full px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
                />
              </div>
              <div className="flex gap-3 justify-end pt-2">
                <button
                  type="button"
                  onClick={() => { setShowModal(false); setSubmitError(null); }}
                  className="px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100 rounded-lg transition-colors"
                >
                  {t('common.cancel')}
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="px-4 py-2 text-sm font-medium text-white bg-teal-600 hover:bg-teal-700 rounded-lg transition-colors disabled:opacity-50"
                >
                  {submitting ? t('payments.recording') : t('payments.recordPayment')}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
