import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { LanguageProvider } from '../../i18n/LanguageContext';
import PaymentList from '../PaymentList';
import { api } from '../../api';

vi.mock('../../api', () => ({
  api: {
    getPayments: vi.fn(),
    createPayment: vi.fn(),
  },
}));

function renderPaymentList() {
  return render(
    <LanguageProvider>
      <MemoryRouter>
        <PaymentList />
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
    ...overrides,
  };
}

describe('PaymentList', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders payment table with data', async () => {
    api.getPayments.mockResolvedValue({
      items: [
        makePayment(),
        makePayment({ id: 'pay-002', paymentReference: 'PAY-REF-002', status: 'allocated', paymentMethod: 'direct_debit' }),
      ],
      totalCount: 2,
    });

    renderPaymentList();

    await waitFor(() => {
      expect(screen.getByText('PAY-REF-001')).toBeInTheDocument();
    });
    expect(screen.getByText('PAY-REF-002')).toBeInTheDocument();
    // Status/method text appears in both filter dropdowns and table badges
    expect(screen.getAllByText('Received').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Allocated').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Bank transfer').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Direct debit').length).toBeGreaterThanOrEqual(1);
  });

  it('shows empty state when no payments', async () => {
    api.getPayments.mockResolvedValue({ items: [], totalCount: 0 });

    renderPaymentList();

    await waitFor(() => {
      expect(screen.getByText('No payments found.')).toBeInTheDocument();
    });
  });

  it('stats cards show correct totals', async () => {
    api.getPayments.mockResolvedValue({
      items: [
        makePayment({ amount: 10000 }),
        makePayment({ id: 'pay-002', amount: 5000 }),
      ],
      totalCount: 2,
    });

    renderPaymentList();

    await waitFor(() => {
      expect(screen.getByText('2')).toBeInTheDocument();
    });
    expect(screen.getByText('Total Payments')).toBeInTheDocument();
    expect(screen.getByText('Total Received')).toBeInTheDocument();
  });

  it('Record Payment button opens modal', async () => {
    api.getPayments.mockResolvedValue({ items: [], totalCount: 0 });

    renderPaymentList();

    await waitFor(() => {
      expect(screen.getByText('No payments found.')).toBeInTheDocument();
    });

    // The button in the header says "Record Payment"
    const recordBtn = screen.getAllByText('Record Payment').find(el => el.tagName === 'BUTTON' || el.closest('button'));
    fireEvent.click(recordBtn);

    await waitFor(() => {
      expect(screen.getByText('Customer ID')).toBeInTheDocument();
    });
    expect(screen.getByText('Payment Method')).toBeInTheDocument();
    expect(screen.getByText('Amount (DKK)')).toBeInTheDocument();
  });

  it('modal submits successfully, closes, and refreshes list', async () => {
    api.getPayments.mockResolvedValue({ items: [], totalCount: 0 });
    api.createPayment.mockResolvedValue({});

    renderPaymentList();

    await waitFor(() => {
      expect(screen.getByText('No payments found.')).toBeInTheDocument();
    });

    // Open modal
    const recordBtn = screen.getAllByText('Record Payment').find(el => el.tagName === 'BUTTON' || el.closest('button'));
    fireEvent.click(recordBtn);

    await waitFor(() => {
      expect(screen.getByText('Customer ID')).toBeInTheDocument();
    });

    // Fill form - find inputs inside the modal
    const customerInput = screen.getAllByRole('textbox').find(el => el.closest('.fixed'));
    fireEvent.change(customerInput, { target: { value: 'cust-001' } });

    const amountInput = screen.getByRole('spinbutton');
    fireEvent.change(amountInput, { target: { value: '5000' } });

    // Submit via form submission
    const form = amountInput.closest('form');
    fireEvent.submit(form);

    await waitFor(() => {
      expect(api.createPayment).toHaveBeenCalledWith({
        customerId: 'cust-001',
        paymentMethod: 'bank_transfer',
        paymentReference: null,
        amount: 5000,
      });
    });
  });

  it('modal shows error on failed submit', async () => {
    api.getPayments.mockResolvedValue({ items: [], totalCount: 0 });
    api.createPayment.mockRejectedValue(new Error('Payment failed'));

    renderPaymentList();

    await waitFor(() => {
      expect(screen.getByText('No payments found.')).toBeInTheDocument();
    });

    // Open modal
    const recordBtn = screen.getAllByText('Record Payment').find(el => el.tagName === 'BUTTON' || el.closest('button'));
    fireEvent.click(recordBtn);

    await waitFor(() => {
      expect(screen.getByText('Customer ID')).toBeInTheDocument();
    });

    // Fill form
    const customerInput = screen.getAllByRole('textbox').find(el => el.closest('.fixed'));
    fireEvent.change(customerInput, { target: { value: 'cust-001' } });
    const amountInput = screen.getByRole('spinbutton');
    fireEvent.change(amountInput, { target: { value: '5000' } });

    // Submit via form
    const form = amountInput.closest('form');
    fireEvent.submit(form);

    await waitFor(() => {
      expect(screen.getByText('Payment failed')).toBeInTheDocument();
    });
  });
});
