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

export const api = {
  // Products
  getProducts: () => request('/products'),

  // Signups (sales channel)
  createSignup: (data) => request('/signup', { method: 'POST', body: JSON.stringify(data) }),
  getSignupStatus: (id) => request(`/signup/${id}/status`),
  cancelSignup: (id) => request(`/signup/${id}/cancel`, { method: 'POST' }),

  // Signups (back-office)
  getSignups: (status) => request(`/signups${status ? `?status=${status}` : ''}`),
  getSignup: (id) => request(`/signups/${id}`),
  getSignupEvents: (id) => request(`/signups/${id}/events`),

  // Address lookup
  lookupAddress: (darId) => request(`/address/${darId}`),

  // Customers
  getCustomers: () => request('/customers'),
  getCustomer: (id) => request(`/customers/${id}`),
};
