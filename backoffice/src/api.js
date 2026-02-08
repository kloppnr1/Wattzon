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
};
