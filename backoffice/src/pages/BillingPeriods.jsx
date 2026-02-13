import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';
import WattzonLoader from '../components/WattzonLoader';

const PAGE_SIZE = 50;

export default function BillingPeriods() {
  const { t } = useTranslation();
  const [data, setData] = useState(null);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const fetchPage = useCallback((p) => {
    setError(null);
    api.getBillingPeriods({ page: p, pageSize: PAGE_SIZE })
      .then(setData)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => { fetchPage(page); }, [page, fetchPage]);

  const periods = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = Math.ceil(totalCount / PAGE_SIZE);

  // Calculate stats
  const totalPeriods = totalCount;
  const totalRuns = periods.reduce((sum, p) => sum + p.settlementRunCount, 0);
  const avgRuns = totalPeriods > 0 ? (totalRuns / totalPeriods).toFixed(1) : 0;

  if (loading) {
    return (
      <WattzonLoader message={t('billing.loadingPeriods')} />
    );
  }

  return (
    <div className="p-4 sm:p-8 max-w-6xl mx-auto">
      {/* Page header */}
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">{t('billing.title')}</h1>
        <p className="text-base text-slate-500 mt-1">{t('billing.subtitle')}</p>
      </div>

      {/* Stats cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <div className="bg-gradient-to-br from-white to-slate-50 rounded-xl p-5 shadow-sm border border-slate-100">
          <div className="text-sm font-medium text-slate-500 mb-1">{t('billing.totalPeriods')}</div>
          <div className="text-3xl font-bold text-slate-900">{totalPeriods}</div>
        </div>
        <div className="bg-gradient-to-br from-white to-teal-50/30 rounded-xl p-5 shadow-sm border border-teal-100/50">
          <div className="text-sm font-medium text-teal-600 mb-1">{t('billing.totalRuns')}</div>
          <div className="text-3xl font-bold text-teal-700">{totalRuns}</div>
        </div>
        <div className="bg-gradient-to-br from-white to-emerald-50/30 rounded-xl p-5 shadow-sm border border-emerald-100/50">
          <div className="text-sm font-medium text-emerald-600 mb-1">{t('billing.avgRunsPerPeriod')}</div>
          <div className="text-3xl font-bold text-emerald-700">{avgRuns}</div>
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
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('billing.colPeriodStart')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('billing.colPeriodEnd')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('billing.colFrequency')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('billing.colSettlementRuns')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('billing.colCreated')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {periods.length === 0 ? (
                <tr>
                  <td colSpan="5" className="px-4 py-12 text-center text-slate-500">
                    {t('billing.noPeriodsFound')}
                  </td>
                </tr>
              ) : (
                periods.map((period) => (
                  <tr key={period.id} className="hover:bg-slate-50 transition-colors cursor-pointer">
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <Link to={`/billing/periods/${period.id}`} className="text-sm text-teal-600 font-medium hover:text-teal-700">
                        {period.periodStart}
                      </Link>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-700">{period.periodEnd}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <span className="inline-flex px-2 py-0.5 text-[11px] font-medium rounded-full bg-slate-100 text-slate-700">
                        {period.frequency}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <span className="inline-flex px-2 py-0.5 text-[11px] font-medium rounded-full bg-teal-100 text-teal-700">
                        {period.settlementRunCount}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-500">
                      {new Date(period.createdAt).toLocaleString()}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* Pagination */}
        {totalPages > 1 && (
          <div className="px-5 py-3.5 bg-slate-50 border-t border-slate-200 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2">
            <div className="text-sm text-slate-600">
              {t('common.showingRange', { from: (page - 1) * PAGE_SIZE + 1, to: Math.min(page * PAGE_SIZE, totalCount), total: totalCount })} {t('billing.showingPeriods')}
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
