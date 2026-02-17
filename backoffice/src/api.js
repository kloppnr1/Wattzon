const BASE = '/api';

async function request(path, options = {}) {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json', ...options.headers },
    ...options,
  });
  if (!res.ok) {
    const body = await res.json().catch(() => null);
    const error = new Error(body?.error || `Request failed: ${res.status}`);
    error.status = res.status;
    throw error;
  }
  return res.json();
}

function qs(params) {
  const entries = Object.entries(params).filter(([, v]) => v != null);
  return entries.length ? '?' + new URLSearchParams(entries).toString() : '';
}

export const api = {
  // Products
  getProducts: () => request('/products'),

  // Signups (sales channel)
  createSignup: (data) => request('/signup', { method: 'POST', body: JSON.stringify(data) }),
  getSignupStatus: (id) => request(`/signup/${id}/status`),
  cancelSignup: (id) => request(`/signup/${id}/cancel`, { method: 'POST' }),

  // Signups (back-office) — paginated
  getSignups: ({ status, page, pageSize } = {}) =>
    request(`/signups${qs({ status, page, pageSize })}`),
  getSignup: (id) => request(`/signups/${id}`),
  getSignupEvents: (id) => request(`/signups/${id}/events`),

  // Dashboard
  getDashboardStats: () => request('/dashboard/stats'),
  getRecentSignups: (limit = 5) => request(`/dashboard/recent-signups?limit=${limit}`),

  // Address lookup
  lookupAddress: (darId) => request(`/address/${darId}`),
  validateGsrn: (gsrn) => request(`/address/gsrn/${gsrn}`),

  // Customers — paginated with search
  getCustomers: ({ page, pageSize, search } = {}) =>
    request(`/customers${qs({ page, pageSize, search })}`),
  getCustomer: (id) => request(`/customers/${id}`),

  // Billing
  getBillingPeriods: ({ page, pageSize } = {}) =>
    request(`/billing/periods${qs({ page, pageSize })}`),
  getBillingPeriod: (id) => request(`/billing/periods/${id}`),
  getSettlementRuns: ({ billingPeriodId, status, meteringPointId, gridAreaCode, fromDate, toDate, page, pageSize } = {}) =>
    request(`/billing/runs${qs({ billingPeriodId, status, meteringPointId, gridAreaCode, fromDate, toDate, page, pageSize })}`),
  getSettlementRun: (id) => request(`/billing/runs/${id}`),
  getSettlementLines: (runId, { page, pageSize } = {}) =>
    request(`/billing/runs/${runId}/lines${qs({ page, pageSize })}`),
  getMeteringPointLines: (gsrn, { fromDate, toDate } = {}) =>
    request(`/billing/metering-points/${gsrn}/lines${qs({ fromDate, toDate })}`),
  getCustomerBillingSummary: (customerId) =>
    request(`/billing/customers/${customerId}/summary`),

  // Corrections
  getCorrections: ({ meteringPointId, triggerType, fromDate, toDate, page, pageSize } = {}) =>
    request(`/billing/corrections${qs({ meteringPointId, triggerType, fromDate, toDate, page, pageSize })}`),
  getCorrection: (batchId) => request(`/billing/corrections/${batchId}`),
  triggerCorrection: (data) => request('/billing/corrections', { method: 'POST', body: JSON.stringify(data) }),
  getRunCorrections: (runId) => request(`/billing/runs/${runId}/corrections`),

  // Messages
  getInboundMessages: ({ messageType, status, correlationId, queueName, fromDate, toDate, page, pageSize } = {}) =>
    request(`/messages/inbound${qs({ messageType, status, correlationId, queueName, fromDate, toDate, page, pageSize })}`),
  getInboundMessage: (id) => request(`/messages/inbound/${id}`),
  getOutboundRequests: ({ processType, status, correlationId, fromDate, toDate, page, pageSize } = {}) =>
    request(`/messages/outbound${qs({ processType, status, correlationId, fromDate, toDate, page, pageSize })}`),
  getOutboundRequest: (id) => request(`/messages/outbound/${id}`),
  getDeadLetters: ({ resolved, page, pageSize } = {}) =>
    request(`/messages/dead-letters${qs({ resolved, page, pageSize })}`),
  getDeadLetter: (id) => request(`/messages/dead-letters/${id}`),
  getMessageStats: () => request(`/messages/stats`),

  // Spot Prices
  getSpotPrices: ({ date } = {}) =>
    request(`/metering/spot-prices${qs({ date })}`),
  getSpotPriceStatus: () => request(`/metering/spot-prices/status`),

  // Aconto Payments
  getAcontoPayments: (gsrn, { from, to } = {}) =>
    request(`/billing/aconto/${gsrn}${qs({ from, to })}`),

  // Metering Point Tariffs
  getMeteringPointTariffs: (gsrn) => request(`/metering-points/${gsrn}/tariffs`),

  // Settlement Preview (dry-run, no persistence)
  getSettlementPreview: (gsrn, periodStart, periodEnd) =>
    request(`/metering-points/${gsrn}/settlement-preview`, {
      method: 'POST',
      body: JSON.stringify({ periodStart, periodEnd }),
    }),

  // Customer Processes
  getCustomerProcesses: (customerId) => request(`/customers/${customerId}/processes`),
  getCustomerMeteringSummary: (customerId) => request(`/customers/${customerId}/metering-summary`),

  // Processes
  getProcesses: ({ status, processType, search, page, pageSize } = {}) =>
    request(`/processes${qs({ status, processType, search, page, pageSize })}`),
  getProcessDetail: (id) => request(`/processes/${id}`),
  getProcessEvents: (id) =>
    request(`/processes/${id}/events`),

  // Conversations & Deliveries
  getConversations: ({ page, pageSize } = {}) =>
    request(`/messages/conversations${qs({ page, pageSize })}`),
  getConversation: (correlationId) =>
    request(`/messages/conversations/${encodeURIComponent(correlationId)}`),
  getDataDeliveries: () => request(`/messages/deliveries`),

  // Invoices
  getInvoices: ({ customerId, status, invoiceType, fromDate, toDate, search, page, pageSize } = {}) =>
    request(`/billing/invoices${qs({ customerId, status, invoiceType, fromDate, toDate, search, page, pageSize })}`),
  getInvoice: (id) => request(`/billing/invoices/${id}`),
  getOverdueInvoices: () => request('/billing/invoices/overdue'),
  sendInvoice: (id) => request(`/billing/invoices/${id}/send`, { method: 'POST' }),
  cancelInvoice: (id) => request(`/billing/invoices/${id}/cancel`, { method: 'POST' }),
  creditInvoice: (id, notes) => request(`/billing/invoices/${id}/credit`, { method: 'POST', body: JSON.stringify({ notes }) }),

  // Payments
  getPayments: ({ customerId, status, fromDate, toDate, page, pageSize } = {}) =>
    request(`/billing/payments${qs({ customerId, status, fromDate, toDate, page, pageSize })}`),
  getPayment: (id) => request(`/billing/payments/${id}`),
  createPayment: (data) => request('/billing/payments', { method: 'POST', body: JSON.stringify(data) }),
  allocatePayment: (id, data) => request(`/billing/payments/${id}/allocate`, { method: 'POST', body: JSON.stringify(data) }),
  importPayments: (data) => request('/billing/payments/import', { method: 'POST', body: JSON.stringify(data) }),

  // Customer Balance
  getCustomerBalance: (customerId) => request(`/billing/customers/${customerId}/balance`),
  getCustomerLedger: (customerId) => request(`/billing/customers/${customerId}/ledger`),
  getOutstandingCustomers: () => request('/billing/outstanding'),
};
