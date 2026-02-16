import { useState, useEffect, useCallback } from 'react';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';
import WattzonLoader from '../components/WattzonLoader';

function addDays(dateStr, days) {
  const d = new Date(dateStr + 'T00:00:00');
  d.setDate(d.getDate() + days);
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

export default function SpotPrices() {
  const { t, lang } = useTranslation();
  const [date, setDate] = useState(null);
  const [data, setData] = useState(null);
  const [status, setStatus] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const fetchData = useCallback(() => {
    setError(null);
    setLoading(true);
    Promise.all([
      api.getSpotPrices({ date: date ?? undefined }),
      api.getSpotPriceStatus(),
    ])
      .then(([prices, st]) => {
        setData(prices);
        setStatus(st);
        if (!date && prices.date) setDate(prices.date);
      })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [date]);

  useEffect(() => { fetchData(); }, [fetchData]);

  const items = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const displayDate = data?.date ?? date;
  const locale = lang === 'da' ? 'da-DK' : 'en-GB';

  if (loading && !data) {
    return <WattzonLoader message={t('spotPrices.loading')} />;
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

      {/* Status indicator */}
      <div className="mb-6 animate-fade-in-up" style={{ animationDelay: '40ms' }}>
        {error ? (
          <div className="flex items-center gap-4 px-4 py-2.5 bg-rose-50 rounded-lg border border-rose-200 text-sm">
            <span className="flex items-center gap-1.5">
              <span className="w-2 h-2 rounded-full bg-rose-500 animate-pulse" />
              <span className="font-medium text-rose-700">{t('common.error')}: {error}</span>
            </span>
          </div>
        ) : !status?.latestDate ? (
          <div className="flex items-center gap-2 px-4 py-2.5 bg-amber-50 rounded-lg border border-amber-200 text-sm text-amber-700">
            <span className="w-2 h-2 rounded-full bg-amber-500 animate-pulse" />
            {t('spotPrices.monitor.noData')}
          </div>
        ) : (
          <div className={`flex items-center gap-4 px-4 py-2.5 rounded-lg border text-sm ${
            status.status === 'ok'
              ? 'bg-emerald-50 border-emerald-200'
              : status.status === 'warning'
                ? 'bg-amber-50 border-amber-200'
                : 'bg-rose-50 border-rose-200'
          }`}>
            <span className="flex items-center gap-1.5">
              <span className={`w-2 h-2 rounded-full ${
                status.status === 'ok' ? 'bg-emerald-500' : status.status === 'warning' ? 'bg-amber-500 animate-pulse' : 'bg-rose-500 animate-pulse'
              }`} />
              <span className={`font-medium ${
                status.status === 'ok' ? 'text-emerald-700' : status.status === 'warning' ? 'text-amber-700' : 'text-rose-700'
              }`}>
                {t('spotPrices.monitor.dataThrough', { date: status.latestDate })}
              </span>
            </span>
            {!status.hasTomorrow && status.status !== 'ok' && (
              <>
                <span className="text-slate-400">|</span>
                <span className={status.status === 'warning' ? 'text-amber-700' : 'text-rose-700'}>
                  {t('spotPrices.monitor.tomorrowMissing')}
                </span>
              </>
            )}
            {status.lastFetchedAt && (
              <>
                <span className="text-slate-400">|</span>
                <span className="text-slate-500">
                  {t('spotPrices.monitor.lastFetch')}: {new Date(status.lastFetchedAt).toLocaleString(locale, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })}
                </span>
              </>
            )}
          </div>
        )}
      </div>

      {/* Date navigation */}
      <div className="flex items-center gap-3 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <button onClick={() => date && setDate(addDays(date, -1))}
          className="px-3 py-2 text-sm font-medium text-slate-600 bg-white border border-slate-300 rounded-lg hover:bg-slate-50 transition-colors">
          &larr; {t('common.previous')}
        </button>
        <input
          type="date"
          value={displayDate ?? ''}
          onChange={e => e.target.value && setDate(e.target.value)}
          className="px-3 py-2 text-sm border border-slate-300 rounded-lg text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-500 focus:border-transparent"
        />
        <button onClick={() => date && setDate(addDays(date, 1))}
          className="px-3 py-2 text-sm font-medium text-slate-600 bg-white border border-slate-300 rounded-lg hover:bg-slate-50 transition-colors">
          {t('common.next')} &rarr;
        </button>
        <button onClick={() => setDate(null)}
          className="px-3 py-2 text-sm font-medium text-teal-600 bg-teal-50 border border-teal-200 rounded-lg hover:bg-teal-100 transition-colors">
          {t('spotPrices.latest')}
        </button>
      </div>

      {/* Table */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="bg-slate-50 border-b border-slate-200">
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('spotPrices.colTimestamp')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('spotPrices.colHour')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">DK1 ({t('spotPrices.unit')})</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">DK2 ({t('spotPrices.unit')})</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {items.length === 0 ? (
                <tr>
                  <td colSpan="4" className="px-4 py-12 text-center text-slate-500">
                    {t('spotPrices.noPrices')}
                  </td>
                </tr>
              ) : (
                items.map((item) => {
                  const ts = new Date(item.timestamp);
                  const utcStr = ts.toISOString().slice(0, 16).replace('T', ' ');
                  const localStr = ts.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' });
                  return (
                    <tr key={item.timestamp} className="hover:bg-slate-50 transition-colors">
                      <td className="px-4 py-2.5 whitespace-nowrap text-sm font-mono text-slate-600">{utcStr}</td>
                      <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-700">{localStr}</td>
                      <td className="px-4 py-2.5 whitespace-nowrap text-sm text-right tabular-nums font-semibold text-slate-900">
                        {item.priceDk1 != null ? item.priceDk1.toFixed(4) : '—'}
                      </td>
                      <td className="px-4 py-2.5 whitespace-nowrap text-sm text-right tabular-nums font-semibold text-slate-900">
                        {item.priceDk2 != null ? item.priceDk2.toFixed(4) : '—'}
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>

        {/* Footer */}
        {items.length > 0 && (
          <div className="px-5 py-3.5 bg-slate-50 border-t border-slate-200">
            <div className="text-sm text-slate-600">
              {t('common.totalItems', { count: totalCount, label: t('spotPrices.showingPrices') })}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
