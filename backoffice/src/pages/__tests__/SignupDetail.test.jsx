import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { LanguageProvider } from '../../i18n/LanguageContext';
import SignupDetail from '../SignupDetail';
import { api } from '../../api';

vi.mock('../../api', () => ({
  api: {
    getSignup: vi.fn(),
    getSignupEvents: vi.fn(),
    cancelSignup: vi.fn(),
  },
}));

function renderSignupDetail(signupId = 'test-id') {
  return render(
    <LanguageProvider>
      <MemoryRouter initialEntries={[`/signups/${signupId}`]}>
        <Routes>
          <Route path="/signups/:id" element={<SignupDetail />} />
        </Routes>
      </MemoryRouter>
    </LanguageProvider>
  );
}

function makeSignup(overrides = {}) {
  return {
    id: 'test-id',
    signupNumber: 'SU-2025-0001',
    status: 'processing',
    type: 'move_in',
    effectiveDate: '2025-07-01',
    gsrn: '571313100000000001',
    darId: 'dar-123',
    productId: 'prod-1',
    productName: 'Green Standard',
    customerId: 'cust-1',
    customerName: 'Test Customer',
    cprCvr: '1234567890',
    contactType: 'private',
    createdAt: '2025-06-01T10:00:00Z',
    correctionChain: [],
    ...overrides,
  };
}

describe('SignupDetail - pending step indicator', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows pending indicator when latest event is non-terminal', async () => {
    api.getSignup.mockResolvedValue(makeSignup());
    api.getSignupEvents.mockResolvedValue([
      { eventType: 'created', occurredAt: '2025-06-01T10:00:00Z' },
      { eventType: 'sent', occurredAt: '2025-06-01T10:05:00Z' },
    ]);

    renderSignupDetail();

    await waitFor(() => {
      expect(screen.getByText('Awaiting acknowledgement from DataHub...')).toBeInTheDocument();
    });
  });

  it('shows correct pending text after "created" event', async () => {
    api.getSignup.mockResolvedValue(makeSignup({ status: 'registered' }));
    api.getSignupEvents.mockResolvedValue([
      { eventType: 'created', occurredAt: '2025-06-01T10:00:00Z' },
    ]);

    renderSignupDetail();

    await waitFor(() => {
      expect(screen.getByText('Awaiting dispatch to DataHub...')).toBeInTheDocument();
    });
  });

  it('does NOT show pending indicator when latest event is "completed"', async () => {
    api.getSignup.mockResolvedValue(makeSignup({ status: 'active' }));
    api.getSignupEvents.mockResolvedValue([
      { eventType: 'created', occurredAt: '2025-06-01T10:00:00Z' },
      { eventType: 'awaiting_effectuation', occurredAt: '2025-06-01T11:00:00Z' },
      { eventType: 'completed', occurredAt: '2025-06-01T12:00:00Z' },
    ]);

    renderSignupDetail();

    await waitFor(() => {
      expect(screen.getByText('Effectuated')).toBeInTheDocument();
    });
    expect(screen.queryByText(/Awaiting.*\.\.\./)).not.toBeInTheDocument();
  });

  it('does NOT show pending indicator when latest event is "cancelled"', async () => {
    api.getSignup.mockResolvedValue(makeSignup({ status: 'cancelled' }));
    api.getSignupEvents.mockResolvedValue([
      { eventType: 'created', occurredAt: '2025-06-01T10:00:00Z' },
      { eventType: 'sent', occurredAt: '2025-06-01T10:05:00Z' },
      { eventType: 'cancelled', occurredAt: '2025-06-01T11:00:00Z' },
    ]);

    renderSignupDetail();

    await waitFor(() => {
      expect(screen.getByText('Cancelled')).toBeInTheDocument();
    });
    expect(screen.queryByText(/Awaiting/)).not.toBeInTheDocument();
  });

  it('does NOT show pending indicator when latest event is "cancellation_reason"', async () => {
    api.getSignup.mockResolvedValue(makeSignup({ status: 'cancelled' }));
    api.getSignupEvents.mockResolvedValue([
      { eventType: 'created', occurredAt: '2025-06-01T10:00:00Z' },
      { eventType: 'sent', occurredAt: '2025-06-01T10:05:00Z' },
      { eventType: 'cancelled', occurredAt: '2025-06-01T11:00:00Z' },
      { eventType: 'cancellation_reason', occurredAt: '2025-06-01T11:01:00Z', payload: '{"reason":"Cancelled by user"}' },
    ]);

    renderSignupDetail();

    await waitFor(() => {
      expect(screen.getByText('Cancellation reason')).toBeInTheDocument();
    });
    expect(screen.queryByText(/Awaiting/)).not.toBeInTheDocument();
  });

  it('does NOT show pending indicator when latest event is "rejection_reason"', async () => {
    api.getSignup.mockResolvedValue(makeSignup({ status: 'rejected', rejectionReason: 'Invalid GSRN' }));
    api.getSignupEvents.mockResolvedValue([
      { eventType: 'created', occurredAt: '2025-06-01T10:00:00Z' },
      { eventType: 'sent', occurredAt: '2025-06-01T10:05:00Z' },
      { eventType: 'rejection_reason', occurredAt: '2025-06-01T11:00:00Z', payload: '{"reason":"Invalid GSRN"}' },
    ]);

    renderSignupDetail();

    await waitFor(() => {
      expect(screen.getByText('Rejected')).toBeInTheDocument();
    });
    expect(screen.queryByText(/Awaiting/)).not.toBeInTheDocument();
  });

  // --- Remaining non-terminal pending texts ---

  it('shows "Awaiting effectuation..." after "acknowledged" event', async () => {
    api.getSignup.mockResolvedValue(makeSignup());
    api.getSignupEvents.mockResolvedValue([
      { eventType: 'created', occurredAt: '2025-06-01T10:00:00Z' },
      { eventType: 'sent', occurredAt: '2025-06-01T10:05:00Z' },
      { eventType: 'acknowledged', occurredAt: '2025-06-01T10:10:00Z' },
    ]);

    renderSignupDetail();

    await waitFor(() => {
      expect(screen.getByText('Awaiting effectuation...')).toBeInTheDocument();
    });
  });

  it('shows "Awaiting effective date..." after "awaiting_effectuation" event', async () => {
    api.getSignup.mockResolvedValue(makeSignup({ status: 'awaiting_effectuation' }));
    api.getSignupEvents.mockResolvedValue([
      { eventType: 'created', occurredAt: '2025-06-01T10:00:00Z' },
      { eventType: 'sent', occurredAt: '2025-06-01T10:05:00Z' },
      { eventType: 'acknowledged', occurredAt: '2025-06-01T10:10:00Z' },
      { eventType: 'awaiting_effectuation', occurredAt: '2025-06-01T10:15:00Z' },
    ]);

    renderSignupDetail();

    await waitFor(() => {
      expect(screen.getByText('Awaiting effective date...')).toBeInTheDocument();
    });
  });

  it('shows "Awaiting cancellation acknowledgement..." after "cancellation_sent" event', async () => {
    api.getSignup.mockResolvedValue(makeSignup({ status: 'cancellation_pending' }));
    api.getSignupEvents.mockResolvedValue([
      { eventType: 'created', occurredAt: '2025-06-01T10:00:00Z' },
      { eventType: 'sent', occurredAt: '2025-06-01T10:05:00Z' },
      { eventType: 'acknowledged', occurredAt: '2025-06-01T10:10:00Z' },
      { eventType: 'awaiting_effectuation', occurredAt: '2025-06-01T10:15:00Z' },
      { eventType: 'cancellation_sent', occurredAt: '2025-06-01T10:20:00Z' },
    ]);

    renderSignupDetail();

    await waitFor(() => {
      expect(screen.getByText('Awaiting cancellation acknowledgement...')).toBeInTheDocument();
    });
  });

  it('shows pending indicator (no text) after "offboarding_started" event', async () => {
    api.getSignup.mockResolvedValue(makeSignup({ status: 'active' }));
    api.getSignupEvents.mockResolvedValue([
      { eventType: 'created', occurredAt: '2025-06-01T10:00:00Z' },
      { eventType: 'completed', occurredAt: '2025-06-01T12:00:00Z' },
      { eventType: 'offboarding_started', occurredAt: '2025-06-01T13:00:00Z' },
    ]);

    renderSignupDetail();

    await waitFor(() => {
      expect(screen.getByText('Offboarding started')).toBeInTheDocument();
    });
    // offboarding_started is non-terminal so the pulsing indicator renders,
    // but there's no pending translation so the text is empty
    const pulsingDot = document.querySelector('.animate-pulse');
    expect(pulsingDot).toBeInTheDocument();
  });

  // --- Remaining terminal events ---

  it('does NOT show pending indicator when latest event is "final_settled"', async () => {
    api.getSignup.mockResolvedValue(makeSignup({ status: 'active' }));
    api.getSignupEvents.mockResolvedValue([
      { eventType: 'created', occurredAt: '2025-06-01T10:00:00Z' },
      { eventType: 'completed', occurredAt: '2025-06-01T12:00:00Z' },
      { eventType: 'final_settled', occurredAt: '2025-06-01T14:00:00Z' },
    ]);

    renderSignupDetail();

    await waitFor(() => {
      expect(screen.getByText('Final settled')).toBeInTheDocument();
    });
    expect(screen.queryByText(/Awaiting.*\.\.\./)).not.toBeInTheDocument();
  });

  it('does NOT show pending indicator when latest event is "rejected"', async () => {
    api.getSignup.mockResolvedValue(makeSignup({ status: 'rejected', rejectionReason: 'Bad data' }));
    api.getSignupEvents.mockResolvedValue([
      { eventType: 'created', occurredAt: '2025-06-01T10:00:00Z' },
      { eventType: 'sent', occurredAt: '2025-06-01T10:05:00Z' },
      { eventType: 'rejected', occurredAt: '2025-06-01T11:00:00Z' },
    ]);

    renderSignupDetail();

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: 'SU-2025-0001' })).toBeInTheDocument();
    });
    expect(screen.queryByText(/Awaiting.*\.\.\./)).not.toBeInTheDocument();
  });
});
