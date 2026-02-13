import { useState, useEffect, useCallback } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';
import WattzonLoader from '../components/WattzonLoader';

const PAGE_SIZE = 50;

const triggerStyles = {
  manual: 'bg-blue-50 text-blue-700',
  auto: 'bg-purple-50 text-purple-700',
};

const statusStyles = {
  completed: { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  failed: { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
};

function StatusBadge({ status, label }) {
  const cfg = statusStyles[status] || statusStyles.completed;
  return (
    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${cfg.badge}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />
      {label}
    </span>
  );
}

export default function Corrections() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [data, setData] = useState(null);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  // Filters
  const [meteringPointId, setMeteringPointId] = useState('');
  const [triggerType, setTriggerType] = useState('');
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');

  // Modal
  const [showModal, setShowModal] = useState(false);
  const [modalData, setModalData] = useState({ meteringPointId: '', periodStart: '', periodEnd: '', note: '' });
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState(null);

  const fetchPage = useCallback((p) => {
    setError(null);
    api.getCorrections({
      meteringPointId: meteringPointId || undefined,
      triggerType: triggerType || undefined,
      fromDate: fromDate || undefined,
      toDate: toDate || undefined,
      page: p,
      pageSize: PAGE_SIZE,
    })
      .then(setData)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [meteringPointId, triggerType, fromDate, toDate]);

  useEffect(() => { setPage(1); }, [meteringPointId, triggerType, fromDate, toDate]);
  useEffect(() => { fetchPage(page); }, [page, fetchPage]);

  const corrections = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = Math.ceil(totalCount / PAGE_SIZE);

  const totalDelta = corrections.reduce((sum, c) => sum + c.total, 0);

  const handleTrigger = async (e) => {
    e.preventDefault();
    setSubmitting(true);
    setSubmitError(null);
    try {
      const result = await api.triggerCorrection({
        meteringPointId: modalData.meteringPointId,
        periodStart: modalData.periodStart,
        periodEnd: modalData.periodEnd,
        note: modalData.note || null,
      });
      setShowModal(false);
      setModalData({ meteringPointId: '', periodStart: '', periodEnd: '', note: '' });
      navigate(`/billing/corrections/${result.correctionBatchId}`);
    } catch (err) {
      setSubmitError(err.message);
    } finally {
      setSubmitting(false);
    }
  };

  if (loading) {
    return (
      <WattzonLoader message={t('corrections.loading')} />
    );
  }

  return (
    <div className="p-4 sm:p-8 max-w-6xl mx-auto">
      {/* Page header */}
      <div className="mb-6 animate-fade-in-up flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <div>
          <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">{t('corrections.title')}</h1>
          <p className="text-base text-slate-500 mt-1">{t('corrections.subtitle')}</p>
        </div>
        <button
          onClick={() => setShowModal(true)}
          className="inline-flex items-center gap-2 px-4 py-2.5 rounded-xl bg-teal-600 text-white text-sm font-medium hover:bg-teal-700 transition-colors shadow-sm"
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
          </svg>
          {t('corrections.triggerCorrection')}
        </button>
      </div>

      {/* Filters */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-4 mb-6 animate-fade-in-up" style={{ animationDelay: '40ms' }}>
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
          <input
            type="text"
            placeholder={t('corrections.filterMeteringPoint')}
            value={meteringPointId}
            onChange={(e) => setMeteringPointId(e.target.value)}
            className="px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
          />
          <select
            value={triggerType}
            onChange={(e) => setTriggerType(e.target.value)}
            className="px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
          >
            <option value="">{t('corrections.filterAllTriggers')}</option>
            <option value="manual">{t('corrections.triggerManual')}</option>
            <option value="auto">{t('corrections.triggerAuto')}</option>
          </select>
          <input
            type="date"
            value={fromDate}
            onChange={(e) => setFromDate(e.target.value)}
            className="px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
            placeholder={t('corrections.filterFrom')}
          />
          <input
            type="date"
            value={toDate}
            onChange={(e) => setToDate(e.target.value)}
            className="px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
            placeholder={t('corrections.filterTo')}
          />
        </div>
      </div>

      {/* Stats cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <div className="bg-gradient-to-br from-white to-slate-50 rounded-xl p-5 shadow-sm border border-slate-100">
          <div className="text-sm font-medium text-slate-500 mb-1">{t('corrections.totalCorrections')}</div>
          <div className="text-3xl font-bold text-slate-900">{totalCount}</div>
        </div>
        <div className="bg-gradient-to-br from-white to-teal-50/30 rounded-xl p-5 shadow-sm border border-teal-100/50">
          <div className="text-sm font-medium text-teal-600 mb-1">{t('corrections.totalDeltaDkk')}</div>
          <div className="text-3xl font-bold text-teal-700">{totalDelta.toFixed(2)} DKK</div>
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
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('corrections.colId')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('corrections.colMeteringPoint')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('corrections.colPeriod')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('corrections.colTrigger')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('corrections.colDeltaKwh')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('corrections.colTotal')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('corrections.colCreated')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {corrections.length === 0 ? (
                <tr>
                  <td colSpan="7" className="px-4 py-12 text-center text-slate-500">
                    {t('corrections.noCorrections')}
                  </td>
                </tr>
              ) : (
                corrections.map((c) => (
                  <tr key={c.correctionBatchId} className="hover:bg-slate-50 transition-colors">
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <Link to={`/billing/corrections/${c.correctionBatchId}`} className="text-teal-600 font-medium hover:text-teal-700 font-mono text-sm">
                        {c.correctionBatchId.substring(0, 8)}...
                      </Link>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-sm font-mono text-slate-700">{c.meteringPointId}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-700">{c.periodStart} — {c.periodEnd}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <span className={`inline-flex px-2 py-0.5 rounded-full text-[11px] font-medium ${triggerStyles[c.triggerType] || triggerStyles.manual}`}>
                        {t('corrections.trigger_' + c.triggerType)}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums text-slate-600">{c.totalDeltaKwh.toFixed(3)}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums font-semibold text-slate-900">{c.total.toFixed(2)} DKK</td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-500">{new Date(c.createdAt).toLocaleString()}</td>
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
              {t('common.showingRange', { from: (page - 1) * PAGE_SIZE + 1, to: Math.min(page * PAGE_SIZE, totalCount), total: totalCount })} {t('corrections.showingCorrections')}
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

      {/* Trigger Correction Modal */}
      {showModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="bg-white rounded-2xl shadow-xl w-full max-w-md mx-4 p-6 animate-fade-in-up">
            <h2 className="text-lg font-semibold text-slate-900 mb-4">{t('corrections.triggerCorrection')}</h2>

            {submitError && (
              <div className="mb-4 p-3 bg-rose-50 border border-rose-200 rounded-lg text-sm text-rose-700">{submitError}</div>
            )}

            <form onSubmit={handleTrigger} className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-1">{t('corrections.modalGsrn')}</label>
                <input
                  type="text"
                  required
                  value={modalData.meteringPointId}
                  onChange={(e) => setModalData(d => ({ ...d, meteringPointId: e.target.value }))}
                  placeholder="571313100000000000"
                  className="w-full px-3 py-2 rounded-lg border border-slate-200 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
                />
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-sm font-medium text-slate-700 mb-1">{t('corrections.modalPeriodStart')}</label>
                  <input
                    type="date"
                    required
                    value={modalData.periodStart}
                    onChange={(e) => setModalData(d => ({ ...d, periodStart: e.target.value }))}
                    className="w-full px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-slate-700 mb-1">{t('corrections.modalPeriodEnd')}</label>
                  <input
                    type="date"
                    required
                    value={modalData.periodEnd}
                    onChange={(e) => setModalData(d => ({ ...d, periodEnd: e.target.value }))}
                    className="w-full px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
                  />
                </div>
              </div>
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-1">{t('corrections.modalNote')}</label>
                <textarea
                  value={modalData.note}
                  onChange={(e) => setModalData(d => ({ ...d, note: e.target.value }))}
                  rows={3}
                  placeholder={t('corrections.modalNotePlaceholder')}
                  className="w-full px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400 resize-none"
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
                  {submitting ? t('corrections.triggering') : t('corrections.triggerCorrection')}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
