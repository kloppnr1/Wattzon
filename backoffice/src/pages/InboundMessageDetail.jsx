import { useState, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';
import Breadcrumb from '../components/Breadcrumb';
import WattzonLoader from '../components/WattzonLoader';

const statusStyles = {
  processed: { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  dead_lettered: { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
};

function StatusBadge({ status, label }) {
  const cfg = statusStyles[status] || { dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-600' };
  return (
    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${cfg.badge}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />
      {label || status.replace('_', ' ')}
    </span>
  );
}

export default function InboundMessageDetail() {
  const { id } = useParams();
  const { t } = useTranslation();
  const [message, setMessage] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [payloadOpen, setPayloadOpen] = useState(false);

  useEffect(() => {
    setLoading(true);
    setError(null);
    api.getInboundMessage(id)
      .then(setMessage)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [id]);

  if (loading) {
    return (
      <WattzonLoader message={t('inboundDetail.loadingMessage')} />
    );
  }

  if (error || !message) {
    return (
      <div className="p-8 max-w-6xl mx-auto">
        <div className="text-center text-rose-600">Error: {error || 'Message not found'}</div>
      </div>
    );
  }

  return (
    <div className="p-4 sm:p-8 max-w-6xl mx-auto">
      <Breadcrumb
        fallback={[{ label: t('inboundDetail.breadcrumbMessages'), to: '/datahub/messages' }]}
        current={t('inboundDetail.breadcrumbInbound')}
      />

      {/* Page header */}
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">{t('inboundDetail.title')}</h1>
        <p className="text-base text-slate-500 mt-1">{message.messageType}</p>
      </div>

      {/* Message metadata card */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <h2 className="text-lg font-semibold text-slate-900 mb-4">{t('inboundDetail.messageMetadata')}</h2>
        <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 sm:gap-x-8 gap-y-4">
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('inboundDetail.messageId')}</dt>
            <dd className="text-base font-mono text-slate-900 mt-1 break-all">{message.id}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('inboundDetail.datahubMessageId')}</dt>
            <dd className="text-base font-mono text-slate-900 mt-1 break-all">{message.datahubMessageId}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('inboundDetail.messageType')}</dt>
            <dd className="text-base font-semibold text-slate-900 mt-1">{message.messageType}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('inboundDetail.status')}</dt>
            <dd className="mt-1">
              <StatusBadge status={message.status} label={t('status.' + message.status)} />
            </dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('inboundDetail.correlationId')}</dt>
            <dd className="text-base font-mono text-slate-700 mt-1 break-all">{message.correlationId || '-'}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('inboundDetail.queueName')}</dt>
            <dd className="text-base text-slate-900 mt-1">{message.queueName}</dd>
          </div>
        </dl>
      </div>

      {/* Timestamps */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6 mb-6 animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        <h2 className="text-lg font-semibold text-slate-900 mb-4">{t('inboundDetail.timestamps')}</h2>
        <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 sm:gap-x-8 gap-y-4">
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('inboundDetail.receivedAt')}</dt>
            <dd className="text-base text-slate-900 mt-1">{new Date(message.receivedAt).toLocaleString()}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('inboundDetail.processedAt')}</dt>
            <dd className="text-base text-slate-900 mt-1">
              {message.processedAt ? new Date(message.processedAt).toLocaleString() : '-'}
            </dd>
          </div>
        </dl>
      </div>

      {/* Message Content */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6 mb-6 animate-fade-in-up" style={{ animationDelay: '180ms' }}>
        <h2 className="text-lg font-semibold text-slate-900 mb-4">{t('inboundDetail.messageContent')}</h2>
        {message.context ? (
          <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 sm:gap-x-8 gap-y-4">
            {message.context.processType && (
              <div>
                <dt className="text-sm font-medium text-slate-500">{t('messageContext.processType')}</dt>
                <dd className="text-base text-slate-900 mt-1">
                  <span className="inline-flex items-center px-2 py-0.5 rounded-full text-[11px] font-medium bg-indigo-50 text-indigo-700">
                    {t('processType.' + message.context.processType) || message.context.processType}
                  </span>
                </dd>
              </div>
            )}
            {message.context.processStatus && (
              <div>
                <dt className="text-sm font-medium text-slate-500">{t('inboundDetail.status')}</dt>
                <dd className="mt-1">
                  <StatusBadge status={message.context.processStatus} label={t('status.' + message.context.processStatus)} />
                </dd>
              </div>
            )}
            {message.context.gsrn && (
              <div>
                <dt className="text-sm font-medium text-slate-500">{t('messageContext.gsrn')}</dt>
                <dd className="text-base font-mono text-slate-900 mt-1">{message.context.gsrn}</dd>
              </div>
            )}
            {message.context.effectiveDate && (
              <div>
                <dt className="text-sm font-medium text-slate-500">{t('messageContext.effectiveDate')}</dt>
                <dd className="text-base text-slate-900 mt-1">{message.context.effectiveDate}</dd>
              </div>
            )}
            {message.context.customerName && (
              <div>
                <dt className="text-sm font-medium text-slate-500">{t('messageContext.customer')}</dt>
                <dd className="text-base text-slate-900 mt-1">{message.context.customerName}{message.context.cprCvr ? ` (${message.context.cprCvr})` : ''}</dd>
              </div>
            )}
            {message.context.gridAreaCode && (
              <div>
                <dt className="text-sm font-medium text-slate-500">{t('messageContext.gridArea')}</dt>
                <dd className="text-base text-slate-900 mt-1">{message.context.gridAreaCode}</dd>
              </div>
            )}
            {message.context.priceArea && (
              <div>
                <dt className="text-sm font-medium text-slate-500">{t('messageContext.priceArea')}</dt>
                <dd className="text-base text-slate-900 mt-1">{message.context.priceArea}</dd>
              </div>
            )}
            {message.context.meteringDataPoints != null && (
              <>
                <div>
                  <dt className="text-sm font-medium text-slate-500">{t('messageContext.dataPoints')}</dt>
                  <dd className="text-base text-slate-900 mt-1">{message.context.meteringDataPoints.toLocaleString()}</dd>
                </div>
                <div>
                  <dt className="text-sm font-medium text-slate-500">{t('messageContext.period')}</dt>
                  <dd className="text-base text-slate-900 mt-1">{message.context.meteringPeriodStart} — {message.context.meteringPeriodEnd}</dd>
                </div>
              </>
            )}
          </dl>
        ) : (
          <p className="text-sm text-slate-400">{t('messageContext.noContext')}</p>
        )}
      </div>

      {/* Error details */}
      {message.errorDetails && (
        <div className="bg-white rounded-xl shadow-sm border border-rose-200 p-6 mb-6 animate-fade-in-up" style={{ animationDelay: '240ms' }}>
          <h2 className="text-lg font-semibold text-rose-900 mb-4">{t('inboundDetail.errorDetails')}</h2>
          <div className="p-4 bg-rose-50 border border-rose-200 rounded-lg overflow-x-auto">
            <pre className="text-sm text-rose-700 whitespace-pre-wrap font-mono">{message.errorDetails}</pre>
          </div>
        </div>
      )}

      {/* Raw Payload */}
      {message.rawPayload && (
        <div className="bg-white rounded-xl shadow-sm border border-slate-200 animate-fade-in-up" style={{ animationDelay: '300ms' }}>
          <button
            onClick={() => setPayloadOpen(o => !o)}
            className="w-full px-6 py-4 flex items-center justify-between text-left"
          >
            <h2 className="text-lg font-semibold text-slate-900">{t('messages.rawPayload')}</h2>
            <span className="text-slate-400 text-sm">{payloadOpen ? '−' : '+'}</span>
          </button>
          {payloadOpen && (
            <div className="px-6 pb-6">
              <div className="p-4 bg-slate-50 border border-slate-200 rounded-lg overflow-x-auto max-h-[600px] overflow-y-auto">
                <pre className="text-sm text-slate-700 whitespace-pre-wrap font-mono">
                  {(() => { try { return JSON.stringify(JSON.parse(message.rawPayload), null, 2); } catch { return message.rawPayload; } })()}
                </pre>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
