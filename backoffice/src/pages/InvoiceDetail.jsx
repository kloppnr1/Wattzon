import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';

const statusStyles = {
  draft: { dot: 'bg-slate-400', badge: 'bg-slate-50 text-slate-700' },
  sent: { dot: 'bg-blue-400', badge: 'bg-blue-50 text-blue-700' },
  paid: { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  partially_paid: { dot: 'bg-amber-400', badge: 'bg-amber-50 text-amber-700' },
  overdue: { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
  cancelled: { dot: 'bg-slate-300', badge: 'bg-slate-50 text-slate-500' },
  credited: { dot: 'bg-purple-400', badge: 'bg-purple-50 text-purple-700' },
};

export default function InvoiceDetail() {
  const { t } = useTranslation();
  const { id } = useParams();
  const [invoice, setInvoice] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [actionLoading, setActionLoading] = useState(false);

  useEffect(() => {
    api.getInvoice(id)
      .then(detail => {
        if (!detail?.invoice) { setInvoice(null); return; }
        setInvoice({
          ...detail.invoice,
          lines: detail.lines,
          customerName: detail.customerName,
          payerName: detail.payerName,
        });
      })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [id]);

  const handleSend = async () => {
    setActionLoading(true);
    try {
      const detail = await api.sendInvoice(id);
      setInvoice(prev => ({
        ...prev,
        ...detail.invoice,
        lines: detail.lines,
        customerName: detail.customerName,
        payerName: detail.payerName,
      }));
    } catch (e) {
      setError(e.message);
    } finally {
      setActionLoading(false);
    }
  };

  const handleCancel = async () => {
    setActionLoading(true);
    try {
      await api.cancelInvoice(id);
      setInvoice(prev => ({ ...prev, status: 'cancelled' }));
    } catch (e) {
      setError(e.message);
    } finally {
      setActionLoading(false);
    }
  };

  const handleCredit = async () => {
    setActionLoading(true);
    try {
      await api.creditInvoice(id, null);
      setInvoice(prev => ({ ...prev, status: 'credited' }));
    } catch (e) {
      setError(e.message);
    } finally {
      setActionLoading(false);
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-[3px] border-teal-100 border-t-teal-500 rounded-full animate-spin" />
          <p className="text-sm text-slate-400 font-medium">{t('invoiceDetail.loading')}</p>
        </div>
      </div>
    );
  }

  if (!invoice) {
    return (
      <div className="p-8 text-center text-slate-500">{t('common.notFound')}</div>
    );
  }

  const cfg = statusStyles[invoice.status] || statusStyles.draft;
  const lines = invoice.lines ?? [];

  return (
    <div className="p-4 sm:p-8 max-w-5xl mx-auto">
      {/* Breadcrumb */}
      <div className="mb-4 animate-fade-in-up">
        <div className="flex items-center gap-2 text-sm text-slate-500">
          <Link to="/invoices" className="hover:text-teal-600 transition-colors">{t('invoices.title')}</Link>
          <span>/</span>
          <span className="text-slate-900 font-medium">{invoice.invoiceNumber || id.substring(0, 8)}</span>
        </div>
      </div>

      {error && (
        <div className="mb-4 p-3 bg-rose-50 border border-rose-200 rounded-lg text-sm text-rose-700">{error}</div>
      )}

      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 mb-6 animate-fade-in-up">
        <div>
          <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">
            {invoice.invoiceNumber || t('invoiceDetail.title')}
          </h1>
          <div className="flex items-center gap-3 mt-2">
            <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${cfg.badge}`}>
              <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />
              {t(`invoices.status_${invoice.status}`)}
            </span>
            <span className="text-sm text-slate-500">{t(`invoices.type_${invoice.invoiceType}`)}</span>
          </div>
        </div>
        <div className="flex gap-2">
          {invoice.status === 'draft' && (
            <button
              onClick={handleSend}
              disabled={actionLoading}
              className="px-4 py-2 text-sm font-medium text-white bg-teal-600 hover:bg-teal-700 rounded-lg transition-colors disabled:opacity-50"
            >
              {t('invoiceDetail.send')}
            </button>
          )}
          {(invoice.status === 'sent' || invoice.status === 'overdue') && (
            <button
              onClick={handleCredit}
              disabled={actionLoading}
              className="px-4 py-2 text-sm font-medium text-purple-700 bg-purple-50 hover:bg-purple-100 rounded-lg transition-colors disabled:opacity-50"
            >
              {t('invoiceDetail.credit')}
            </button>
          )}
          {(invoice.status === 'draft' || invoice.status === 'sent') && (
            <button
              onClick={handleCancel}
              disabled={actionLoading}
              className="px-4 py-2 text-sm font-medium text-rose-700 bg-rose-50 hover:bg-rose-100 rounded-lg transition-colors disabled:opacity-50"
            >
              {t('common.cancel')}
            </button>
          )}
        </div>
      </div>

      {/* Summary cards */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <div className="bg-gradient-to-br from-white to-slate-50 rounded-xl p-4 shadow-sm border border-slate-100">
          <div className="text-xs font-medium text-slate-500 mb-1">{t('invoiceDetail.subtotal')}</div>
          <div className="text-xl font-bold text-slate-900">{(invoice.totalExVat || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })}</div>
        </div>
        <div className="bg-gradient-to-br from-white to-slate-50 rounded-xl p-4 shadow-sm border border-slate-100">
          <div className="text-xs font-medium text-slate-500 mb-1">{t('invoiceDetail.vat')}</div>
          <div className="text-xl font-bold text-slate-900">{(invoice.vatAmount || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })}</div>
        </div>
        <div className="bg-gradient-to-br from-white to-teal-50/30 rounded-xl p-4 shadow-sm border border-teal-100/50">
          <div className="text-xs font-medium text-teal-600 mb-1">{t('invoiceDetail.total')}</div>
          <div className="text-xl font-bold text-teal-700">{(invoice.totalInclVat || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK</div>
        </div>
        <div className="bg-gradient-to-br from-white to-amber-50/30 rounded-xl p-4 shadow-sm border border-amber-100/50">
          <div className="text-xs font-medium text-amber-600 mb-1">{t('invoiceDetail.outstanding')}</div>
          <div className="text-xl font-bold text-amber-700">{(invoice.amountOutstanding || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })} DKK</div>
        </div>
      </div>

      {/* Invoice info */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-5 mb-6 animate-fade-in-up" style={{ animationDelay: '80ms' }}>
        <h2 className="text-sm font-semibold text-slate-900 mb-4">{t('invoiceDetail.info')}</h2>
        <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-8 gap-y-3">
          <div>
            <dt className="text-xs font-medium text-slate-500">{t('invoiceDetail.period')}</dt>
            <dd className="text-sm text-slate-900 mt-0.5">{invoice.periodStart} — {invoice.periodEnd}</dd>
          </div>
          <div>
            <dt className="text-xs font-medium text-slate-500">{t('invoiceDetail.dueDate')}</dt>
            <dd className="text-sm text-slate-900 mt-0.5">{invoice.dueDate || '—'}</dd>
          </div>
          <div>
            <dt className="text-xs font-medium text-slate-500">{t('invoiceDetail.issuedAt')}</dt>
            <dd className="text-sm text-slate-900 mt-0.5">{invoice.issuedAt ? new Date(invoice.issuedAt).toLocaleString() : '—'}</dd>
          </div>
          <div>
            <dt className="text-xs font-medium text-slate-500">{t('invoiceDetail.paidAt')}</dt>
            <dd className="text-sm text-slate-900 mt-0.5">{invoice.paidAt ? new Date(invoice.paidAt).toLocaleString() : '—'}</dd>
          </div>
          {invoice.customerId && (
            <div>
              <dt className="text-xs font-medium text-slate-500">{t('invoiceDetail.customer')}</dt>
              <dd className="text-sm mt-0.5">
                <Link to={`/customers/${invoice.customerId}`} className="text-teal-600 hover:text-teal-700">{invoice.customerId.substring(0, 8)}...</Link>
              </dd>
            </div>
          )}
          {invoice.notes && (
            <div className="sm:col-span-2">
              <dt className="text-xs font-medium text-slate-500">{t('invoiceDetail.notes')}</dt>
              <dd className="text-sm text-slate-900 mt-0.5">{invoice.notes}</dd>
            </div>
          )}
        </dl>
      </div>

      {/* Invoice lines */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        <div className="px-5 py-4 border-b border-slate-200">
          <h2 className="text-sm font-semibold text-slate-900">{t('invoiceDetail.lines')}</h2>
          <p className="text-xs text-slate-500 mt-0.5">{lines.length} {t('invoiceDetail.linesCount')}</p>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="bg-slate-50 border-b border-slate-200">
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('invoiceDetail.colDescription')}</th>
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('invoiceDetail.colLineType')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('invoiceDetail.colQuantity')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('invoiceDetail.colAmount')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('invoiceDetail.colVat')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('invoiceDetail.colTotalLine')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {lines.length === 0 ? (
                <tr>
                  <td colSpan="6" className="px-4 py-8 text-center text-slate-500">{t('invoiceDetail.noLines')}</td>
                </tr>
              ) : (
                lines.map((line, i) => (
                  <tr key={line.id || i} className="hover:bg-slate-50 transition-colors">
                    <td className="px-4 py-2.5 text-sm text-slate-700">{line.description || '—'}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-600">{line.lineType?.replace(/_/g, ' ')}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums text-slate-600">{line.quantity != null ? line.quantity.toFixed(3) : '—'}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums text-slate-700">{(line.amountExVat || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums text-slate-500">{(line.vatAmount || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums font-semibold text-slate-900">{(line.amountInclVat || 0).toLocaleString('da-DK', { minimumFractionDigits: 2 })}</td>
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
