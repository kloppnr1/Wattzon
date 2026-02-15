import { useState, useEffect, useCallback } from 'react';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';
import WattzonLoader from '../components/WattzonLoader';

const PAGE_SIZE = 200;

function formatDate(d) {
  return d.toISOString().slice(0, 10);
}

function defaultFrom() {
  return formatDate(new Date());
}

function defaultTo() {
  const d = new Date();
  d.setDate(d.getDate() + 2);
  return formatDate(d);
}

function freshnessInfo(lastFetchedAt, latestDate, t) {
  if (!lastFetchedAt) return { label: t('spotPrices.monitor.noData'), color: 'slate', dot: 'bg-slate-400' };

  const now = new Date();
  const fetched = new Date(lastFetchedAt);
  const diffMs = now - fetched;
  const diffMins = Math.floor(diffMs / 60000);
  const diffHours = Math.floor(diffMs / 3600000);

  if (diffHours < 2) return { label: t('spotPrices.monitor.fresh', { mins: diffMins }), color: 'emerald', dot: 'bg-emerald-500' };
  if (diffHours < 6) return { label: t('spotPrices.monitor.recent', { hours: diffHours }), color: 'amber', dot: 'bg-amber-500' };
  return { label: t('spotPrices.monitor.stale', { hours: diffHours }), color: 'rose', dot: 'bg-rose-500' };
}

function AreaStatusCard({ area, t, lang }) {
  const freshness = freshnessInfo(area.lastFetchedAt, area.latestDate, t);
  const borderColor = {
    emerald: 'border-emerald-200',
    amber: 'border-amber-200',
    rose: 'border-rose-200',
    slate: 'border-slate-200',
  }[freshness.color];

  return (
    <div className={`bg-white rounded-xl p-4 shadow-sm border ${borderColor}`}>
      <div className="flex items-center justify-between mb-3">
        <span className="text-sm font-bold text-slate-800">{area.priceArea}</span>
        <span className="flex items-center gap-1.5">
          <span className={`w-2 h-2 rounded-full ${freshness.dot} animate-pulse`} />
          <span className={`text-xs font-medium text-${freshness.color}-700`}>{freshness.label}</span>
        </span>
      </div>
      <div className="grid grid-cols-2 gap-x-4 gap-y-2 text-xs">
        <div>
          <span className="text-slate-400">{t('spotPrices.monitor.coverage')}</span>
          <div className="font-medium text-slate-700 font-mono">
            {area.earliestDate ?? '—'} <span className="text-slate-300">→</span> {area.latestDate ?? '—'}
          </div>
        </div>
        <div>
          <span className="text-slate-400">{t('spotPrices.monitor.lastFetch')}</span>
          <div className="font-medium text-slate-700 font-mono">
            {area.lastFetchedAt
              ? new Date(area.lastFetchedAt).toLocaleString(lang === 'da' ? 'da-DK' : 'en-GB', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })
              : '—'}
          </div>
        </div>
        <div>
          <span className="text-slate-400">{t('spotPrices.monitor.totalRecords')}</span>
          <div className="font-semibold text-slate-800">{area.totalCount.toLocaleString(lang === 'da' ? 'da-DK' : 'en')}</div>
        </div>
        <div>
          <span className="text-slate-400">{t('spotPrices.monitor.last24h')} / {t('spotPrices.monitor.last7d')}</span>
          <div className="font-semibold text-slate-800">
            {area.last24hCount.toLocaleString(lang === 'da' ? 'da-DK' : 'en')}
            <span className="text-slate-300 mx-1">/</span>
            {area.last7dCount.toLocaleString(lang === 'da' ? 'da-DK' : 'en')}
          </div>
        </div>
      </div>
    </div>
  );
}

export default function SpotPrices() {
  const { t, lang } = useTranslation();
  const [priceArea, setPriceArea] = useState('DK1');
  const [from, setFrom] = useState(defaultFrom);
  const [to, setTo] = useState(defaultTo);
  const [page, setPage] = useState(1);
  const [data, setData] = useState(null);
  const [latest, setLatest] = useState(null);
  const [status, setStatus] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const fetchData = useCallback((p) => {
    setError(null);
    setLoading(true);
    Promise.all([
      api.getSpotPrices({ priceArea, from, to, page: p, pageSize: PAGE_SIZE }),
      api.getSpotPriceLatest(),
      api.getSpotPriceStatus(),
    ])
      .then(([prices, lat, st]) => { setData(prices); setLatest(lat); setStatus(st); })
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
  const areas = status?.areas ?? [];

  if (loading && !data) {
    return (
      <WattzonLoader message={t('spotPrices.loading')} />
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
        <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">{t('spotPrices.title')}</h1>
        <p className="text-base text-slate-500 mt-1">{t('spotPrices.subtitle')}</p>
      </div>

      {/* Monitoring panel */}
      {areas.length > 0 && (
        <div className="mb-6 animate-fade-in-up" style={{ animationDelay: '40ms' }}>
          <div className="text-xs font-semibold text-slate-500 uppercase tracking-wider mb-2">{t('spotPrices.monitor.title')}</div>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            {areas.map(area => (
              <AreaStatusCard key={area.priceArea} area={area} t={t} lang={lang} />
            ))}
          </div>
        </div>
      )}

      {/* Filters */}
      <div className="flex flex-col sm:flex-row flex-wrap items-start sm:items-end gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
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
          <table className="w-full">
            <thead>
              <tr className="bg-slate-50 border-b border-slate-200">
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('spotPrices.colTimestamp')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('spotPrices.colDate')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('spotPrices.colHour')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('spotPrices.colPrice')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('spotPrices.colPriceDkk')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('spotPrices.colResolution')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {items.length === 0 ? (
                <tr className="bg-slate-50 border-b border-slate-200">
                  <td colSpan="6" className="px-4 py-12 text-center text-slate-500">
                    {t('spotPrices.noPrices')}
                  </td>
                </tr>
              ) : (
                items.map((item) => {
                  const ts = new Date(item.timestamp);
                  const dateStr = ts.toLocaleDateString(lang === 'da' ? 'da-DK' : 'en-GB');
                  const timeStr = ts.toLocaleTimeString(lang === 'da' ? 'da-DK' : 'en-GB', { hour: '2-digit', minute: '2-digit' });
                  const dkkPerMwh = item.pricePerKwh * 10;
                  return (
                    <tr key={`${item.timestamp}-${item.priceArea}`} className="hover:bg-slate-50 transition-colors">
                      <td className="px-4 py-2.5 whitespace-nowrap text-sm font-mono text-slate-600">
                        {ts.toISOString().slice(0, 16).replace('T', ' ')}
                      </td>
                      <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-700">{dateStr}</td>
                      <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-700">{timeStr}</td>
                      <td className="px-4 py-2.5 whitespace-nowrap text-sm text-right font-semibold text-slate-900">
                        {item.pricePerKwh.toFixed(4)}
                      </td>
                      <td className="px-4 py-2.5 whitespace-nowrap text-sm text-right text-slate-500">
                        {dkkPerMwh.toFixed(2)}
                      </td>
                      <td className="px-4 py-2.5 whitespace-nowrap">
                        <span className={`inline-flex px-2 py-0.5 text-[11px] font-medium rounded-full ${
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
          <div className="px-5 py-3.5 bg-slate-50 border-t border-slate-200 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2">
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
