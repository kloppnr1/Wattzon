import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';
import WattzonLoader from '../components/WattzonLoader';

const PAGE_SIZE = 50;

const statusStyles = {
  draft: { dot: 'bg-slate-400', badge: 'bg-slate-50 text-slate-700' },
  sent: { dot: 'bg-blue-400', badge: 'bg-blue-50 text-blue-700' },
  paid: { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  partially_paid: { dot: 'bg-amber-400', badge: 'bg-amber-50 text-amber-700' },
  overdue: { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
  cancelled: { dot: 'bg-slate-300', badge: 'bg-slate-50 text-slate-500' },
  credited: { dot: 'bg-purple-400', badge: 'bg-purple-50 text-purple-700' },
};

const typeStyles = {
  aconto: 'bg-teal-50 text-teal-700',
  settlement: 'bg-indigo-50 text-indigo-700',
  combined_quarterly: 'bg-violet-50 text-violet-700',
  credit_note: 'bg-rose-50 text-rose-700',
  final_settlement: 'bg-amber-50 text-amber-700',
};

function StatusBadge({ status, t }) {
  const cfg = statusStyles[status] || statusStyles.draft;
  return (
    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${cfg.badge}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />
      {t(`invoices.status_${status}`)}
    </span>
  );
}

export default function InvoiceList() {
  const { t } = useTranslation();
  const [data, setData] = useState(null);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const [status, setStatus] = useState('');
  const [invoiceType, setInvoiceType] = useState('');
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [search, setSearch] = useState('');

  const fetchPage = useCallback((p) => {
    setError(null);
    api.getInvoices({
      status: status || undefined,
      invoiceType: invoiceType || undefined,
      fromDate: fromDate || undefined,
      toDate: toDate || undefined,
      search: search || undefined,
      page: p,
      pageSize: PAGE_SIZE,
    })
      .then(setData)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [status, invoiceType, fromDate, toDate, search]);

  useEffect(() => { setPage(1); }, [status, invoiceType, fromDate, toDate, search]);
  useEffect(() => { fetchPage(page); }, [page, fetchPage]);

  const invoices = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = Math.ceil(totalCount / PAGE_SIZE);

  if (loading) {
    return (
      <WattzonLoader message={t('invoices.loading')} />
    );
  }

  return (
    <div className="p-4 sm:p-8 max-w-6xl mx-auto">
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">{t('invoices.title')}</h1>
        <p className="text-base text-slate-500 mt-1">{t('invoices.subtitle')}</p>
      </div>

      {/* Filters */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-4 mb-6 animate-fade-in-up" style={{ animationDelay: '40ms' }}>
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3">
          <input
            type="text"
            placeholder={t('invoices.searchPlaceholder')}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
          />
          <select
            value={status}
            onChange={(e) => setStatus(e.target.value)}
            className="px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
          >
            <option value="">{t('invoices.allStatuses')}</option>
            <option value="draft">{t('invoices.status_draft')}</option>
            <option value="sent">{t('invoices.status_sent')}</option>
            <option value="paid">{t('invoices.status_paid')}</option>
            <option value="partially_paid">{t('invoices.status_partially_paid')}</option>
            <option value="overdue">{t('invoices.status_overdue')}</option>
            <option value="cancelled">{t('invoices.status_cancelled')}</option>
            <option value="credited">{t('invoices.status_credited')}</option>
          </select>
          <select
            value={invoiceType}
            onChange={(e) => setInvoiceType(e.target.value)}
            className="px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
          >
            <option value="">{t('invoices.allTypes')}</option>
            <option value="aconto">{t('invoices.type_aconto')}</option>
            <option value="settlement">{t('invoices.type_settlement')}</option>
            <option value="combined_quarterly">{t('invoices.type_combined_quarterly')}</option>
            <option value="credit_note">{t('invoices.type_credit_note')}</option>
            <option value="final_settlement">{t('invoices.type_final_settlement')}</option>
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
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <div className="bg-gradient-to-br from-white to-slate-50 rounded-xl p-5 shadow-sm border border-slate-100">
          <div className="text-sm font-medium text-slate-500 mb-1">{t('invoices.totalInvoices')}</div>
          <div className="text-3xl font-bold text-slate-900">{totalCount}</div>
        </div>
        <div className="bg-gradient-to-br from-white to-teal-50/30 rounded-xl p-5 shadow-sm border border-teal-100/50">
          <div className="text-sm font-medium text-teal-600 mb-1">{t('invoices.totalAmount')}</div>
          <div className="text-3xl font-bold text-teal-700">
            {invoices.reduce((s, i) => s + (i.totalInclVat || 0), 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK
          </div>
        </div>
        <div className="bg-gradient-to-br from-white to-amber-50/30 rounded-xl p-5 shadow-sm border border-amber-100/50">
          <div className="text-sm font-medium text-amber-600 mb-1">{t('invoices.totalOutstanding')}</div>
          <div className="text-3xl font-bold text-amber-700">
            {invoices.reduce((s, i) => s + (i.amountOutstanding || 0), 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK
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
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('invoices.colNumber')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('invoices.colCustomer')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('invoices.colType')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('invoices.colPeriod')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('invoices.colStatus')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('invoices.colTotal')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('invoices.colOutstanding')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('invoices.colDueDate')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {invoices.length === 0 ? (
                <tr>
                  <td colSpan="8" className="px-4 py-12 text-center text-slate-500">
                    {t('invoices.noInvoices')}
                  </td>
                </tr>
              ) : (
                invoices.map((inv) => (
                  <tr key={inv.id} className="hover:bg-slate-50 transition-colors">
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <Link to={`/invoices/${inv.id}`} className="text-teal-600 font-medium hover:text-teal-700 text-sm">
                        {inv.invoiceNumber || inv.id.substring(0, 8) + '...'}
                      </Link>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-700">
                      {inv.customerId ? (
                        <Link to={`/customers/${inv.customerId}`} className="text-teal-600 hover:text-teal-700">{inv.customerName}</Link>
                      ) : (
                        inv.customerName || '-'
                      )}
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <span className={`inline-flex px-2 py-0.5 rounded-full text-[11px] font-medium ${typeStyles[inv.invoiceType] || typeStyles.settlement}`}>
                        {t(`invoices.type_${inv.invoiceType}`)}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-700">{inv.periodStart} — {inv.periodEnd}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <StatusBadge status={inv.status} t={t} />
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums font-semibold text-slate-900">
                      {(inv.totalInclVat || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums text-slate-600">
                      {(inv.amountOutstanding || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-500">
                      {inv.dueDate || '—'}
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
              {t('common.showingRange', { from: (page - 1) * PAGE_SIZE + 1, to: Math.min(page * PAGE_SIZE, totalCount), total: totalCount })} {t('invoices.showingInvoices')}
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
    </div>
  );
}
