import { useState, useEffect, useCallback } from 'react';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';

const PAGE_SIZE = 200;

function formatDate(d) {
  return d.toISOString().slice(0, 10);
}

function defaultFrom() {
  const d = new Date();
  d.setDate(d.getDate() - 7);
  return formatDate(d);
}

function defaultTo() {
  const d = new Date();
  d.setDate(d.getDate() + 2);
  return formatDate(d);
}

export default function SpotPrices() {
  const { t, lang } = useTranslation();
  const [priceArea, setPriceArea] = useState('DK1');
  const [from, setFrom] = useState(defaultFrom);
  const [to, setTo] = useState(defaultTo);
  const [page, setPage] = useState(1);
  const [data, setData] = useState(null);
  const [latest, setLatest] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const fetchData = useCallback((p) => {
    setError(null);
    setLoading(true);
    Promise.all([
      api.getSpotPrices({ priceArea, from, to, page: p, pageSize: PAGE_SIZE }),
      api.getSpotPriceLatest(),
    ])
      .then(([prices, lat]) => { setData(prices); setLatest(lat); })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [priceArea, from, to]);

  // Reset to page 1 when filters change
  useEffect(() => { setPage(1); fetchData(1); }, [fetchData]);

  // Fetch when page changes (but not on filter change — handled above)
  const handlePageChange = (newPage) => {
    setPage(newPage);
    fetchData(newPage);
  };

  const items = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = data?.totalPages ?? 1;
  const avgPrice = data?.avgPrice ?? 0;
  const minPrice = data?.minPrice ?? 0;
  const maxPrice = data?.maxPrice ?? 0;

  const latestDate = priceArea === 'DK1' ? latest?.dk1 : latest?.dk2;

  if (loading && !data) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-[3px] border-teal-100 border-t-teal-500 rounded-full animate-spin" />
          <p className="text-sm text-slate-400 font-medium">{t('spotPrices.loading')}</p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-4 sm:p-8 max-w-6xl mx-auto relative">
      {/* Loading progress bar */}
      {loading && (
        <div className="absolute top-0 left-0 right-0 h-1 bg-teal-100 overflow-hidden rounded-full z-10">
          <div className="h-full bg-teal-500 rounded-full animate-progress-bar" />
        </div>
      )}

      {/* Page header */}
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-3xl font-bold text-slate-900 tracking-tight">{t('spotPrices.title')}</h1>
        <p className="text-base text-slate-500 mt-1">{t('spotPrices.subtitle')}</p>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap items-end gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <div>
          <label className="block text-xs font-semibold text-slate-500 uppercase tracking-wider mb-1">{t('spotPrices.priceArea')}</label>
          <div className="flex rounded-lg overflow-hidden border border-slate-300">
            {['DK1', 'DK2'].map(area => (
              <button
                key={area}
                onClick={() => setPriceArea(area)}
                className={`px-4 py-2 text-sm font-medium transition-colors ${
                  priceArea === area
                    ? 'bg-teal-600 text-white'
                    : 'bg-white text-slate-700 hover:bg-slate-50'
                }`}
              >
                {area}
              </button>
            ))}
          </div>
        </div>
        <div>
          <label className="block text-xs font-semibold text-slate-500 uppercase tracking-wider mb-1">{t('spotPrices.from')}</label>
          <input
            type="date"
            value={from}
            onChange={e => setFrom(e.target.value)}
            className="px-3 py-2 text-sm border border-slate-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-teal-500 focus:border-transparent"
          />
        </div>
        <div>
          <label className="block text-xs font-semibold text-slate-500 uppercase tracking-wider mb-1">{t('spotPrices.to')}</label>
          <input
            type="date"
            value={to}
            onChange={e => setTo(e.target.value)}
            className="px-3 py-2 text-sm border border-slate-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-teal-500 focus:border-transparent"
          />
        </div>
        {latestDate && (
          <div className="text-xs text-slate-400 pb-2">
            {t('spotPrices.latestData')}: <span className="font-medium text-slate-600">{latestDate}</span>
          </div>
        )}
      </div>

      {/* Stats cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '90ms' }}>
        <div className="bg-gradient-to-br from-white to-slate-50 rounded-xl p-5 shadow-sm border border-slate-100">
          <div className="text-sm font-medium text-slate-500 mb-1">{t('spotPrices.totalPrices')}</div>
          <div className="text-3xl font-bold text-slate-900">{totalCount.toLocaleString(lang === 'da' ? 'da-DK' : 'en')}</div>
        </div>
        <div className="bg-gradient-to-br from-white to-teal-50/30 rounded-xl p-5 shadow-sm border border-teal-100/50">
          <div className="text-sm font-medium text-teal-600 mb-1">{t('spotPrices.avgPrice')}</div>
          <div className="text-3xl font-bold text-teal-700">{avgPrice.toFixed(2)}</div>
          <div className="text-xs text-teal-500 mt-0.5">{t('spotPrices.unit')}</div>
        </div>
        <div className="bg-gradient-to-br from-white to-emerald-50/30 rounded-xl p-5 shadow-sm border border-emerald-100/50">
          <div className="text-sm font-medium text-emerald-600 mb-1">{t('spotPrices.minPrice')}</div>
          <div className="text-3xl font-bold text-emerald-700">{minPrice.toFixed(2)}</div>
          <div className="text-xs text-emerald-500 mt-0.5">{t('spotPrices.unit')}</div>
        </div>
        <div className="bg-gradient-to-br from-white to-rose-50/30 rounded-xl p-5 shadow-sm border border-rose-100/50">
          <div className="text-sm font-medium text-rose-600 mb-1">{t('spotPrices.maxPrice')}</div>
          <div className="text-3xl font-bold text-rose-700">{maxPrice.toFixed(2)}</div>
          <div className="text-xs text-rose-500 mt-0.5">{t('spotPrices.unit')}</div>
        </div>
      </div>

      {/* Table */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden animate-fade-in-up" style={{ animationDelay: '150ms' }}>
        {error && (
          <div className="p-4 bg-rose-50 border-b border-rose-100 text-rose-700 text-sm">
            {t('common.error')}: {error}
          </div>
        )}

        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-slate-200">
            <thead className="bg-slate-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('spotPrices.colTimestamp')}</th>
                <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('spotPrices.colDate')}</th>
                <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('spotPrices.colHour')}</th>
                <th className="px-6 py-3 text-right text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('spotPrices.colPrice')}</th>
                <th className="px-6 py-3 text-right text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('spotPrices.colPriceDkk')}</th>
                <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('spotPrices.colResolution')}</th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-slate-100">
              {items.length === 0 ? (
                <tr>
                  <td colSpan="6" className="px-6 py-12 text-center text-slate-500">
                    {t('spotPrices.noPrices')}
                  </td>
                </tr>
              ) : (
                items.map((item, i) => {
                  const ts = new Date(item.timestamp);
                  const dateStr = ts.toLocaleDateString(lang === 'da' ? 'da-DK' : 'en-GB');
                  const timeStr = ts.toLocaleTimeString(lang === 'da' ? 'da-DK' : 'en-GB', { hour: '2-digit', minute: '2-digit' });
                  const dkkPerMwh = item.pricePerKwh * 10;
                  return (
                    <tr key={`${item.timestamp}-${item.priceArea}`} className={`transition-colors ${i % 2 === 0 ? 'bg-white hover:bg-teal-50/30' : 'bg-slate-50/50 hover:bg-teal-50/50'}`}>
                      <td className="px-6 py-2.5 whitespace-nowrap text-sm font-mono text-slate-600">
                        {ts.toISOString().slice(0, 16).replace('T', ' ')}
                      </td>
                      <td className="px-6 py-2.5 whitespace-nowrap text-sm text-slate-700">{dateStr}</td>
                      <td className="px-6 py-2.5 whitespace-nowrap text-sm text-slate-700">{timeStr}</td>
                      <td className="px-6 py-2.5 whitespace-nowrap text-sm text-right font-semibold text-slate-900">
                        {item.pricePerKwh.toFixed(4)}
                      </td>
                      <td className="px-6 py-2.5 whitespace-nowrap text-sm text-right text-slate-500">
                        {dkkPerMwh.toFixed(2)}
                      </td>
                      <td className="px-6 py-2.5 whitespace-nowrap">
                        <span className={`inline-flex px-2 py-0.5 text-xs font-medium rounded-full ${
                          item.resolution === 'PT15M'
                            ? 'bg-purple-100 text-purple-700'
                            : 'bg-slate-100 text-slate-600'
                        }`}>
                          {item.resolution === 'PT15M' ? '15 min' : '1 hour'}
                        </span>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>

        {/* Footer with pagination */}
        {items.length > 0 && (
          <div className="px-6 py-4 bg-slate-50 border-t border-slate-200 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2">
            <div className="text-sm text-slate-600">
              {t('common.totalItems', { count: totalCount, label: t('spotPrices.showingPrices') })}
            </div>
            {totalPages > 1 && (
              <div className="flex items-center gap-1">
                <button
                  onClick={() => handlePageChange(Math.max(1, page - 1))}
                  disabled={page <= 1}
                  className="px-3 py-1.5 text-xs font-medium text-slate-600 bg-white border border-slate-200 rounded-lg hover:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
                >
                  {t('common.previous')}
                </button>
                <span className="px-3 py-1.5 text-xs font-semibold text-slate-700">
                  {page} / {totalPages}
                </span>
                <button
                  onClick={() => handlePageChange(Math.min(totalPages, page + 1))}
                  disabled={page >= totalPages}
                  className="px-3 py-1.5 text-xs font-medium text-slate-600 bg-white border border-slate-200 rounded-lg hover:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
                >
                  {t('common.next')}
                </button>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
