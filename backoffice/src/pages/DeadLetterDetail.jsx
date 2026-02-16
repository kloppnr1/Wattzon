import { useState, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';
import Breadcrumb from '../components/Breadcrumb';
import WattzonLoader from '../components/WattzonLoader';

export default function DeadLetterDetail() {
  const { id } = useParams();
  const { t } = useTranslation();
  const [deadLetter, setDeadLetter] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    setLoading(true);
    setError(null);
    api.getDeadLetter(id)
      .then(setDeadLetter)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [id]);

  if (loading) {
    return (
      <WattzonLoader message={t('deadLetterDetail.loadingDeadLetter')} />
    );
  }

  if (error || !deadLetter) {
    return (
      <div className="p-8 max-w-6xl mx-auto">
        <div className="text-center text-rose-600">Error: {error || 'Dead letter not found'}</div>
      </div>
    );
  }

  return (
    <div className="p-4 sm:p-8 max-w-6xl mx-auto">
      <Breadcrumb
        fallback={[{ label: t('deadLetterDetail.breadcrumbMessages'), to: '/datahub/messages' }]}
        current={t('deadLetterDetail.breadcrumbDeadLetter')}
      />

      {/* Page header */}
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">{t('deadLetterDetail.title')}</h1>
        <p className="text-base text-slate-500 mt-1">{deadLetter.queueName}</p>
      </div>

      {/* Dead letter metadata card */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <h2 className="text-lg font-semibold text-slate-900 mb-4">{t('deadLetterDetail.metadata')}</h2>
        <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 sm:gap-x-8 gap-y-4">
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('deadLetterDetail.deadLetterId')}</dt>
            <dd className="text-base font-mono text-slate-900 mt-1 break-all">{deadLetter.id}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('deadLetterDetail.originalMessageId')}</dt>
            <dd className="text-base font-mono text-slate-900 mt-1 break-all">{deadLetter.originalMessageId || '-'}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('deadLetterDetail.queueName')}</dt>
            <dd className="text-base text-slate-900 mt-1">{deadLetter.queueName}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('deadLetterDetail.failedAt')}</dt>
            <dd className="text-base text-slate-900 mt-1">{new Date(deadLetter.failedAt).toLocaleString()}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('deadLetterDetail.resolved')}</dt>
            <dd className="mt-1">
              <span className={`inline-flex px-2 py-1 text-xs font-medium rounded-full ${
                deadLetter.resolved ? 'bg-emerald-100 text-emerald-700' : 'bg-rose-100 text-rose-700'
              }`}>
                {deadLetter.resolved ? t('common.yes') : t('common.no')}
              </span>
            </dd>
          </div>
          {deadLetter.resolved && (
            <>
              <div>
                <dt className="text-sm font-medium text-slate-500">{t('deadLetterDetail.resolvedAt')}</dt>
                <dd className="text-base text-slate-900 mt-1">
                  {deadLetter.resolvedAt ? new Date(deadLetter.resolvedAt).toLocaleString() : '-'}
                </dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-slate-500">{t('deadLetterDetail.resolvedBy')}</dt>
                <dd className="text-base text-slate-900 mt-1">{deadLetter.resolvedBy || '-'}</dd>
              </div>
            </>
          )}
        </dl>
      </div>

      {/* Error reason */}
      <div className="bg-white rounded-xl shadow-sm border border-rose-200 p-6 mb-6 animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        <h2 className="text-lg font-semibold text-rose-900 mb-4">{t('deadLetterDetail.errorReason')}</h2>
        <div className="p-4 bg-rose-50 border border-rose-200 rounded-lg overflow-x-auto">
          <pre className="text-sm text-rose-700 whitespace-pre-wrap font-mono">{deadLetter.errorReason}</pre>
        </div>
      </div>

      {/* Raw payload */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6 animate-fade-in-up" style={{ animationDelay: '180ms' }}>
        <h2 className="text-lg font-semibold text-slate-900 mb-4">{t('deadLetterDetail.rawPayload')}</h2>
        <div className="p-4 bg-slate-50 border border-slate-200 rounded-lg overflow-x-auto">
          <pre className="text-xs text-slate-700 font-mono">{deadLetter.rawPayload}</pre>
        </div>
      </div>
    </div>
  );
}
