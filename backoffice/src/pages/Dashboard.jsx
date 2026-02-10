import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';

export default function Dashboard() {
  const { t } = useTranslation();
  const [stats, setStats] = useState(null);
  const [recent, setRecent] = useState([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    Promise.all([
      api.getDashboardStats().catch(() => null),
      api.getRecentSignups(5).catch(() => []),
    ]).then(([s, r]) => {
      setStats(s);
      setRecent(r);
      setLoading(false);
    });
  }, []);

  const cards = stats
    ? [
        {
          label: t('dashboard.pendingSignups'),
          value: stats.pendingSignups,
          link: '/signups',
          bg: 'bg-gradient-to-br from-white to-slate-50',
          border: 'border-slate-100',
          labelColor: 'text-slate-500',
          valueColor: 'text-slate-900',
        },
        {
          label: t('dashboard.activeCustomers'),
          value: stats.activeCustomers,
          link: '/customers',
          bg: 'bg-gradient-to-br from-white to-teal-50/30',
          border: 'border-teal-100/50',
          labelColor: 'text-teal-600',
          valueColor: 'text-teal-700',
        },
        {
          label: t('dashboard.rejected'),
          value: stats.rejectedSignups,
          link: '/signups',
          bg: 'bg-gradient-to-br from-white to-rose-50/30',
          border: 'border-rose-100/50',
          labelColor: 'text-rose-600',
          valueColor: 'text-rose-700',
        },
        {
          label: t('dashboard.products'),
          value: stats.productCount,
          link: '/products',
          bg: 'bg-gradient-to-br from-white to-emerald-50/30',
          border: 'border-emerald-100/50',
          labelColor: 'text-emerald-600',
          valueColor: 'text-emerald-700',
        },
      ]
    : [];

  const statusStyles = {
    registered: { dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-600' },
    processing: { dot: 'bg-teal-400', badge: 'bg-teal-50 text-teal-700' },
    active:     { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
    rejected:   { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
    cancelled:  { dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-500' },
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-[3px] border-teal-100 border-t-teal-500 rounded-full animate-spin" />
          <p className="text-sm text-slate-400 font-medium">{t('dashboard.loadingDashboard')}</p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-4 sm:p-8 max-w-6xl mx-auto">
      {/* Header */}
      <div className="mb-8 animate-fade-in-up">
        <h1 className="text-3xl font-bold text-slate-900 tracking-tight">{t('dashboard.title')}</h1>
        <p className="text-base text-slate-500 mt-1">{t('dashboard.subtitle')}</p>
      </div>

      {/* Stat cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mb-8 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        {cards.map((s) => (
          <Link
            key={s.label}
            to={s.link}
            className={`card-lift ${s.bg} rounded-xl p-5 shadow-sm border ${s.border}`}
          >
            <div className={`text-sm font-medium ${s.labelColor} mb-1`}>{s.label}</div>
            <div className={`text-3xl font-bold ${s.valueColor}`}>{s.value.toLocaleString('da-DK')}</div>
          </Link>
        ))}
      </div>

      {/* Recent signups */}
      <div className="bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden animate-fade-in-up">
        <div className="px-6 py-4 border-b border-slate-100 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="w-1 h-5 rounded-full bg-teal-500" />
            <h2 className="text-base font-semibold text-slate-900">{t('dashboard.recentSignups')}</h2>
          </div>
          <Link to="/signups" className="text-sm font-medium text-teal-500 hover:text-teal-700 transition-colors flex items-center gap-1">
            {t('common.viewAll')}
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="m8.25 4.5 7.5 7.5-7.5 7.5" />
            </svg>
          </Link>
        </div>
        {recent.length === 0 ? (
          <div className="p-14 text-center">
            <div className="w-14 h-14 rounded-2xl bg-slate-50 flex items-center justify-center mx-auto mb-3">
              <svg className="w-7 h-7 text-slate-300" fill="none" viewBox="0 0 24 24" strokeWidth={1} stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 0 0-3.375-3.375h-1.5A1.125 1.125 0 0 1 13.5 7.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 0 0-9-9Z" />
              </svg>
            </div>
            <p className="text-sm font-medium text-slate-400">{t('dashboard.noSignupsYet')}</p>
            <p className="text-xs text-slate-400 mt-1">{t('dashboard.createToStart')}</p>
          </div>
        ) : (
          <div className="overflow-x-auto">
          <table className="w-full min-w-[600px]">
            <thead>
              <tr className="border-b border-slate-50 bg-slate-50/50">
                <th className="text-left text-[11px] font-semibold text-slate-400 uppercase tracking-wider px-6 py-3">{t('dashboard.colSignup')}</th>
                <th className="text-left text-[11px] font-semibold text-slate-400 uppercase tracking-wider px-6 py-3">{t('dashboard.colCustomer')}</th>
                <th className="text-left text-[11px] font-semibold text-slate-400 uppercase tracking-wider px-6 py-3">{t('dashboard.colGsrn')}</th>
                <th className="text-left text-[11px] font-semibold text-slate-400 uppercase tracking-wider px-6 py-3">{t('dashboard.colStatus')}</th>
                <th className="text-left text-[11px] font-semibold text-slate-400 uppercase tracking-wider px-6 py-3">{t('dashboard.colCreated')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {recent.map((s, i) => (
                <tr key={s.id} className="hover:bg-teal-50/40 transition-colors duration-150 animate-slide-in" style={{ animationDelay: `${i * 60}ms` }}>
                  <td className="px-6 py-3.5">
                    <Link to={`/signups/${s.id}`} className="text-sm font-semibold text-teal-600 hover:text-teal-800 transition-colors">
                      {s.signupNumber}
                    </Link>
                  </td>
                  <td className="px-6 py-3.5 text-sm text-slate-700">{s.customerName}</td>
                  <td className="px-6 py-3.5">
                    <span className="text-xs font-mono text-slate-500 bg-slate-100 px-2 py-1 rounded-md">{s.gsrn}</span>
                  </td>
                  <td className="px-6 py-3.5">
                    <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium ${(statusStyles[s.status] || statusStyles.registered).badge}`}>
                      <span className={`w-1.5 h-1.5 rounded-full ${(statusStyles[s.status] || statusStyles.registered).dot}`} />
                      {t('status.' + s.status)}
                    </span>
                  </td>
                  <td className="px-6 py-3.5 text-sm text-slate-400">
                    {new Date(s.createdAt).toLocaleDateString('da-DK')}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          </div>
        )}
      </div>
    </div>
  );
}
