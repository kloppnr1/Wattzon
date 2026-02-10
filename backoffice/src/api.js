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

  // Customers — paginated with search
  getCustomers: ({ page, pageSize, search } = {}) =>
    request(`/customers${qs({ page, pageSize, search })}`),
  getCustomer: (id) => request(`/customers/${id}`),

  // Billing
  getBillingPeriods: ({ page, pageSize } = {}) =>
    request(`/billing/periods${qs({ page, pageSize })}`),
  getBillingPeriod: (id) => request(`/billing/periods/${id}`),
  getSettlementRuns: ({ billingPeriodId, page, pageSize } = {}) =>
    request(`/billing/runs${qs({ billingPeriodId, page, pageSize })}`),
  getSettlementRun: (id) => request(`/billing/runs/${id}`),
  getSettlementLines: (runId, { page, pageSize } = {}) =>
    request(`/billing/runs/${runId}/lines${qs({ page, pageSize })}`),
  getMeteringPointLines: (gsrn, { fromDate, toDate } = {}) =>
    request(`/billing/metering-points/${gsrn}/lines${qs({ fromDate, toDate })}`),
  getCustomerBillingSummary: (customerId) =>
    request(`/billing/customers/${customerId}/summary`),

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
  getSpotPrices: ({ priceArea, from, to, page, pageSize } = {}) =>
    request(`/metering/spot-prices${qs({ priceArea, from, to, page, pageSize })}`),
  getSpotPriceLatest: () => request(`/metering/spot-prices/latest`),

  // Conversations & Deliveries
  getConversations: ({ page, pageSize } = {}) =>
    request(`/messages/conversations${qs({ page, pageSize })}`),
  getConversation: (correlationId) =>
    request(`/messages/conversations/${encodeURIComponent(correlationId)}`),
  getDataDeliveries: () => request(`/messages/deliveries`),
};
