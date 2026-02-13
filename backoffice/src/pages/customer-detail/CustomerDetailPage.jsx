import { useState, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { api } from '../../api';
import { useTranslation } from '../../i18n/LanguageContext';
import Breadcrumb from '../../components/Breadcrumb';
import OverviewTab from './OverviewTab';
import ContractsMeteringTab from './ContractsMeteringTab';
import InvoicesTab from './InvoicesTab';
import PaymentsTab from './PaymentsTab';
import ChargesTab from './ChargesTab';
import ProcessesTab from './ProcessesTab';
import LedgerTab from './LedgerTab';

const TABS = [
  { key: 'overview', label: 'customerDetail.tabOverview' },
  { key: 'contracts', label: 'customerDetail.tabContracts' },
  { key: 'invoices', label: 'customerDetail.tabInvoices' },
  { key: 'payments', label: 'customerDetail.tabPayments' },
  { key: 'charges', label: 'customerDetail.tabCharges' },
  { key: 'processes', label: 'customerDetail.tabProcesses' },
  { key: 'ledger', label: 'customerDetail.tabLedger' },
];

export default function CustomerDetailPage() {
  const { t } = useTranslation();
  const { id } = useParams();
  const [customer, setCustomer] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [tab, setTab] = useState('overview');

  useEffect(() => {
    api.getCustomer(id)
      .then(setCustomer)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [id]);

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-[3px] border-teal-100 border-t-teal-500 rounded-full animate-spin" />
          <p className="text-sm text-slate-400 font-medium">{t('customerDetail.loadingCustomer')}</p>
        </div>
      </div>
    );
  }
  if (error) return <div className="p-8"><div className="bg-rose-50 border border-rose-200 rounded-xl px-4 py-3 text-sm text-rose-600">{error}</div></div>;
  if (!customer) return <div className="p-8"><p className="text-sm text-slate-500">{t('customerDetail.notFound')}</p></div>;

  return (
    <div className="p-4 sm:p-8 max-w-6xl mx-auto">
      <Breadcrumb
        fallback={[{ label: t('customerList.title'), to: '/customers' }]}
        current={customer.name}
      />

      <div className="flex flex-col sm:flex-row sm:items-center gap-4 mb-6 animate-fade-in-up">
        <div className="w-14 h-14 rounded-2xl bg-teal-500 flex items-center justify-center shadow-lg shadow-teal-500/25">
          <span className="text-base font-bold text-white">
            {customer.name.split(' ').map(n => n[0]).join('').slice(0, 2).toUpperCase()}
          </span>
        </div>
        <div>
          <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">{customer.name}</h1>
          <p className="text-sm text-slate-500 font-medium">
            {customer.contactType} &middot; <span className="font-mono text-xs bg-slate-100 px-2 py-0.5 rounded-md">{customer.cprCvr}</span>
          </p>
        </div>
        <div className="sm:ml-auto flex items-center gap-2">
          <span className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold ${
            customer.status === 'active' ? 'bg-emerald-50 text-emerald-700 border border-emerald-200' : 'bg-slate-100 text-slate-500'
          }`}>
            <span className={`w-1.5 h-1.5 rounded-full ${customer.status === 'active' ? 'bg-emerald-400' : 'bg-slate-400'}`} />
            {t('status.' + customer.status)}
          </span>
        </div>
      </div>

      {/* Tab bar */}
      <div className="flex items-center gap-1 mb-5 bg-white rounded-xl p-1.5 w-fit max-w-full overflow-x-auto shadow-sm border border-slate-100">
        {TABS.map(({ key, label }) => (
          <button
            key={key}
            onClick={() => setTab(key)}
            className={`px-4 py-2 text-sm font-semibold rounded-lg transition-all duration-200 whitespace-nowrap ${
              tab === key
                ? 'bg-teal-500 text-white shadow-md'
                : 'text-slate-600 hover:text-slate-900 hover:bg-slate-50'
            }`}
          >
            {t(label)}
          </button>
        ))}
      </div>

      {/* Tab content */}
      {tab === 'overview' && <OverviewTab customer={customer} customerId={id} />}
      {tab === 'contracts' && <ContractsMeteringTab customer={customer} />}
      {tab === 'invoices' && <InvoicesTab customerId={id} />}
      {tab === 'payments' && <PaymentsTab customerId={id} />}
      {tab === 'charges' && <ChargesTab customerId={id} />}
      {tab === 'processes' && <ProcessesTab customerId={id} />}
      {tab === 'ledger' && <LedgerTab customerId={id} />}
    </div>
  );
}
