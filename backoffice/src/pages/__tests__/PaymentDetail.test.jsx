import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { LanguageProvider } from '../../i18n/LanguageContext';
import PaymentDetail from '../PaymentDetail';
import { api } from '../../api';

vi.mock('../../api', () => ({
  api: {
    getPayment: vi.fn(),
  },
}));

function renderPaymentDetail(paymentId = 'pay-001') {
  return render(
    <LanguageProvider>
      <MemoryRouter initialEntries={[`/payments/${paymentId}`]}>
        <Routes>
          <Route path="/payments/:id" element={<PaymentDetail />} />
        </Routes>
      </MemoryRouter>
    </LanguageProvider>
  );
}

function makePayment(overrides = {}) {
  return {
    id: 'pay-001',
    paymentReference: 'PAY-REF-001',
    paymentMethod: 'bank_transfer',
    status: 'received',
    amount: 15000.0,
    amountAllocated: 10000.0,
    amountUnallocated: 5000.0,
    receivedAt: '2025-01-15T12:00:00Z',
    customerId: 'cust-001',
    externalId: 'ext-001',
    valueDate: '2025-01-15',
    allocations: [],
    ...overrides,
  };
}

describe('PaymentDetail', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('displays payment header with reference and status', async () => {
    api.getPayment.mockResolvedValue(makePayment());

    renderPaymentDetail();

    await waitFor(() => {
      // Reference appears in breadcrumb and header
      expect(screen.getAllByText('PAY-REF-001')).toHaveLength(2);
    });
    expect(screen.getByText('Received')).toBeInTheDocument();
    expect(screen.getByText('Bank transfer')).toBeInTheDocument();
  });

  it('summary cards show amount, allocated, unallocated', async () => {
    api.getPayment.mockResolvedValue(makePayment());

    renderPaymentDetail();

    await waitFor(() => {
      // "Amount" appears in summary card and allocations table header
      expect(screen.getAllByText('Amount').length).toBeGreaterThanOrEqual(1);
    });
    // "Allocated" appears in summary card and allocations table header
    expect(screen.getAllByText('Allocated').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('Unallocated')).toBeInTheDocument();
  });

  it('allocations table renders with invoice links', async () => {
    api.getPayment.mockResolvedValue(makePayment({
      allocations: [
        { id: 'alloc-1', invoiceId: 'inv-001abcd-full-uuid', amount: 5000, allocatedAt: '2025-01-16T10:00:00Z', allocatedBy: 'system' },
        { id: 'alloc-2', invoiceId: 'inv-002abcd-full-uuid', amount: 5000, allocatedAt: '2025-01-17T10:00:00Z', allocatedBy: 'admin' },
      ],
    }));

    renderPaymentDetail();

    await waitFor(() => {
      expect(screen.getByText('2 allocations')).toBeInTheDocument();
    });

    const links = screen.getAllByText(/inv-001a\.\.\.|inv-002a\.\.\./);
    expect(links).toHaveLength(2);
    expect(links[0].closest('a')).toHaveAttribute('href', '/invoices/inv-001abcd-full-uuid');
    expect(links[1].closest('a')).toHaveAttribute('href', '/invoices/inv-002abcd-full-uuid');
  });

  it('shows empty allocations message when none', async () => {
    api.getPayment.mockResolvedValue(makePayment({ allocations: [] }));

    renderPaymentDetail();

    await waitFor(() => {
      expect(screen.getByText('No allocations yet.')).toBeInTheDocument();
    });
    expect(screen.getByText('0 allocations')).toBeInTheDocument();
  });

  it('shows not found state when payment is null', async () => {
    api.getPayment.mockResolvedValue(null);

    renderPaymentDetail();

    await waitFor(() => {
      expect(screen.getByText('Not found')).toBeInTheDocument();
    });
  });

  it('shows payment info fields', async () => {
    api.getPayment.mockResolvedValue(makePayment());

    renderPaymentDetail();

    await waitFor(() => {
      expect(screen.getByText('Payment ID')).toBeInTheDocument();
    });
    expect(screen.getByText('Customer')).toBeInTheDocument();
    expect(screen.getByText('External ID')).toBeInTheDocument();
    expect(screen.getByText('Received At')).toBeInTheDocument();
    expect(screen.getByText('Value Date')).toBeInTheDocument();
  });
});
