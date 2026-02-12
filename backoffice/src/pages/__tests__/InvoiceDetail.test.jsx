import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { LanguageProvider } from '../../i18n/LanguageContext';
import InvoiceDetail from '../InvoiceDetail';
import { api } from '../../api';

vi.mock('../../api', () => ({
  api: {
    getInvoice: vi.fn(),
    sendInvoice: vi.fn(),
    cancelInvoice: vi.fn(),
    creditInvoice: vi.fn(),
  },
}));

function renderInvoiceDetail(invoiceId = 'inv-001') {
  return render(
    <LanguageProvider>
      <MemoryRouter initialEntries={[`/invoices/${invoiceId}`]}>
        <Routes>
          <Route path="/invoices/:id" element={<InvoiceDetail />} />
        </Routes>
      </MemoryRouter>
    </LanguageProvider>
  );
}

function makeInvoice(overrides = {}) {
  return {
    id: 'inv-001',
    invoiceNumber: 'INV-2025-0001',
    invoiceType: 'settlement',
    status: 'draft',
    periodStart: '2025-01-01',
    periodEnd: '2025-01-31',
    totalExVat: 10000.0,
    vatAmount: 2500.0,
    totalInclVat: 12500.0,
    amountOutstanding: 12500.0,
    dueDate: '2025-02-15',
    issuedAt: null,
    paidAt: null,
    customerId: 'cust-001',
    notes: null,
    lines: [],
    ...overrides,
  };
}

describe('InvoiceDetail', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('displays invoice header with number and status badge', async () => {
    api.getInvoice.mockResolvedValue(makeInvoice());

    renderInvoiceDetail();

    await waitFor(() => {
      // Invoice number appears in both breadcrumb and header
      expect(screen.getAllByText('INV-2025-0001')).toHaveLength(2);
    });
    expect(screen.getByText('Draft')).toBeInTheDocument();
    expect(screen.getByText('Settlement')).toBeInTheDocument();
  });

  it('summary cards show subtotal, VAT, total, outstanding', async () => {
    api.getInvoice.mockResolvedValue(makeInvoice());

    renderInvoiceDetail();

    await waitFor(() => {
      expect(screen.getByText('Subtotal')).toBeInTheDocument();
    });
    // VAT and Total appear in both summary cards and lines table headers
    expect(screen.getAllByText('VAT').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Total').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('Outstanding')).toBeInTheDocument();
  });

  it('invoice lines table renders correctly', async () => {
    api.getInvoice.mockResolvedValue(makeInvoice({
      lines: [
        { id: 'line-1', description: 'Energy supply', lineType: 'energy', quantity: 1500.123, amountExVat: 8000, vatAmount: 2000, amountInclVat: 10000 },
      ],
    }));

    renderInvoiceDetail();

    await waitFor(() => {
      expect(screen.getByText('Energy supply')).toBeInTheDocument();
    });
    expect(screen.getByText('1 lines')).toBeInTheDocument();
  });

  it('shows empty lines message when no lines', async () => {
    api.getInvoice.mockResolvedValue(makeInvoice({ lines: [] }));

    renderInvoiceDetail();

    await waitFor(() => {
      expect(screen.getByText('No invoice lines.')).toBeInTheDocument();
    });
  });

  it('shows Send button only for draft status', async () => {
    api.getInvoice.mockResolvedValue(makeInvoice({ status: 'draft' }));

    renderInvoiceDetail();

    await waitFor(() => {
      expect(screen.getByText('Send Invoice')).toBeInTheDocument();
    });
    expect(screen.getByText('Cancel')).toBeInTheDocument();
    expect(screen.queryByText('Create Credit Note')).not.toBeInTheDocument();
  });

  it('shows Credit button only for sent/overdue status', async () => {
    api.getInvoice.mockResolvedValue(makeInvoice({ status: 'sent' }));

    renderInvoiceDetail();

    await waitFor(() => {
      expect(screen.getByText('Create Credit Note')).toBeInTheDocument();
    });
    expect(screen.getByText('Cancel')).toBeInTheDocument();
    expect(screen.queryByText('Send Invoice')).not.toBeInTheDocument();
  });

  it('shows Credit button for overdue status', async () => {
    api.getInvoice.mockResolvedValue(makeInvoice({ status: 'overdue' }));

    renderInvoiceDetail();

    await waitFor(() => {
      expect(screen.getByText('Create Credit Note')).toBeInTheDocument();
    });
    expect(screen.queryByText('Cancel')).not.toBeInTheDocument();
    expect(screen.queryByText('Send Invoice')).not.toBeInTheDocument();
  });

  it('shows no action buttons for paid status', async () => {
    api.getInvoice.mockResolvedValue(makeInvoice({ status: 'paid' }));

    renderInvoiceDetail();

    await waitFor(() => {
      expect(screen.getAllByText('INV-2025-0001').length).toBeGreaterThanOrEqual(1);
    });
    expect(screen.queryByText('Send Invoice')).not.toBeInTheDocument();
    expect(screen.queryByText('Create Credit Note')).not.toBeInTheDocument();
    const cancelButtons = screen.queryAllByRole('button').filter(b => b.textContent === 'Cancel');
    expect(cancelButtons).toHaveLength(0);
  });

  it('shows no action buttons for cancelled status', async () => {
    api.getInvoice.mockResolvedValue(makeInvoice({ status: 'cancelled' }));

    renderInvoiceDetail();

    await waitFor(() => {
      expect(screen.getByText('Cancelled')).toBeInTheDocument();
    });
    expect(screen.queryByText('Send Invoice')).not.toBeInTheDocument();
    expect(screen.queryByText('Create Credit Note')).not.toBeInTheDocument();
  });

  it('shows no action buttons for credited status', async () => {
    api.getInvoice.mockResolvedValue(makeInvoice({ status: 'credited' }));

    renderInvoiceDetail();

    await waitFor(() => {
      expect(screen.getByText('Credited')).toBeInTheDocument();
    });
    expect(screen.queryByText('Send Invoice')).not.toBeInTheDocument();
    expect(screen.queryByText('Create Credit Note')).not.toBeInTheDocument();
  });

  it('Send action calls correct API and updates state', async () => {
    api.getInvoice.mockResolvedValue(makeInvoice({ status: 'draft' }));
    api.sendInvoice.mockResolvedValue({ status: 'sent' });

    renderInvoiceDetail();

    await waitFor(() => {
      expect(screen.getByText('Send Invoice')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('Send Invoice'));

    await waitFor(() => {
      expect(api.sendInvoice).toHaveBeenCalledWith('inv-001');
    });
    await waitFor(() => {
      expect(screen.getByText('Sent')).toBeInTheDocument();
    });
  });

  it('Cancel action calls correct API and updates state', async () => {
    api.getInvoice.mockResolvedValue(makeInvoice({ status: 'draft' }));
    api.cancelInvoice.mockResolvedValue({});

    renderInvoiceDetail();

    await waitFor(() => {
      expect(screen.getAllByText('INV-2025-0001').length).toBeGreaterThanOrEqual(1);
    });

    const cancelButton = screen.getAllByRole('button').find(b => b.textContent === 'Cancel');
    fireEvent.click(cancelButton);

    await waitFor(() => {
      expect(api.cancelInvoice).toHaveBeenCalledWith('inv-001');
    });
    await waitFor(() => {
      expect(screen.getByText('Cancelled')).toBeInTheDocument();
    });
  });

  it('Credit action calls correct API and updates state', async () => {
    api.getInvoice.mockResolvedValue(makeInvoice({ status: 'sent' }));
    api.creditInvoice.mockResolvedValue({});

    renderInvoiceDetail();

    await waitFor(() => {
      expect(screen.getByText('Create Credit Note')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('Create Credit Note'));

    await waitFor(() => {
      expect(api.creditInvoice).toHaveBeenCalledWith('inv-001', null);
    });
    await waitFor(() => {
      expect(screen.getByText('Credited')).toBeInTheDocument();
    });
  });

  it('displays error when action fails', async () => {
    api.getInvoice.mockResolvedValue(makeInvoice({ status: 'draft' }));
    api.sendInvoice.mockRejectedValue(new Error('Send failed'));

    renderInvoiceDetail();

    await waitFor(() => {
      expect(screen.getByText('Send Invoice')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('Send Invoice'));

    await waitFor(() => {
      expect(screen.getByText('Send failed')).toBeInTheDocument();
    });
  });

  it('shows not found when invoice is null', async () => {
    api.getInvoice.mockResolvedValue(null);

    renderInvoiceDetail();

    await waitFor(() => {
      expect(screen.getByText('Not found')).toBeInTheDocument();
    });
  });
});
