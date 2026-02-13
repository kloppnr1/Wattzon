import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';
import WattzonLoader from '../components/WattzonLoader';

export default function OutstandingOverview() {
  const { t } = useTranslation();
  const [customers, setCustomers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    api.getOutstandingCustomers()
      .then((data) => setCustomers(Array.isArray(data) ? data : data?.items ?? []))
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  const totalOutstanding = customers.reduce((s, c) => s + (c.totalOutstanding || 0), 0);
  const totalOverdue = customers.reduce((s, c) => s + (c.totalOverdue || 0), 0);

  if (loading) {
    return (
      <WattzonLoader message={t('outstanding.loading')} />
    );
  }

  return (
    <div className="p-4 sm:p-8 max-w-6xl mx-auto">
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">{t('outstanding.title')}</h1>
        <p className="text-base text-slate-500 mt-1">{t('outstanding.subtitle')}</p>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '40ms' }}>
        <div className="bg-gradient-to-br from-white to-slate-50 rounded-xl p-5 shadow-sm border border-slate-100">
          <div className="text-sm font-medium text-slate-500 mb-1">{t('outstanding.customersCount')}</div>
          <div className="text-3xl font-bold text-slate-900">{customers.length}</div>
        </div>
        <div className="bg-gradient-to-br from-white to-amber-50/30 rounded-xl p-5 shadow-sm border border-amber-100/50">
          <div className="text-sm font-medium text-amber-600 mb-1">{t('outstanding.totalOutstanding')}</div>
          <div className="text-3xl font-bold text-amber-700">{totalOutstanding.toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK</div>
        </div>
        <div className="bg-gradient-to-br from-white to-rose-50/30 rounded-xl p-5 shadow-sm border border-rose-100/50">
          <div className="text-sm font-medium text-rose-600 mb-1">{t('outstanding.totalOverdue')}</div>
          <div className="text-3xl font-bold text-rose-700">{totalOverdue.toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK</div>
        </div>
      </div>

      {/* Table */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden animate-fade-in-up" style={{ animationDelay: '80ms' }}>
        {error && (
          <div className="p-4 bg-rose-50 border-b border-rose-100 text-rose-700 text-sm">
            {t('common.error')}: {error}
          </div>
        )}

        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="bg-slate-50 border-b border-slate-200">
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('outstanding.colCustomer')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('outstanding.colInvoiced')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('outstanding.colPaid')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('outstanding.colOutstanding')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('outstanding.colOverdue')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('outstanding.colInvoiceCount')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {customers.length === 0 ? (
                <tr>
                  <td colSpan="6" className="px-4 py-12 text-center text-slate-500">
                    {t('outstanding.noOutstanding')}
                  </td>
                </tr>
              ) : (
                customers.map((c) => (
                  <tr key={c.customerId} className="hover:bg-slate-50 transition-colors">
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <Link to={`/customers/${c.customerId}`} className="text-teal-600 font-medium hover:text-teal-700 text-sm">
                        {c.customerName || c.customerId.substring(0, 8) + '...'}
                      </Link>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums text-slate-700">
                      {(c.totalInvoiced || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums text-emerald-700">
                      {(c.totalPaid || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums font-semibold text-amber-700">
                      {(c.totalOutstanding || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums text-rose-700">
                      {(c.totalOverdue || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums text-slate-600">
                      {c.invoiceCount || 0}
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
