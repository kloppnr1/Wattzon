using DataHub.Settlement.Infrastructure.Lifecycle;
using DataHub.Settlement.Infrastructure.Messaging;
using DataHub.Settlement.UnitTests;
using FluentAssertions;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

/// <summary>
/// Integration tests verifying that GetConversationAsync and GetConversationsAsync
/// include all messages for a process using a single correlation ID (including cancel messages).
/// </summary>
[Collection("Database")]
public class ConversationQueryTests
{
    private readonly ProcessRepository _processRepo;
    private readonly MessageRepository _messageRepo;
    private readonly MessageLog _messageLog;

    public ConversationQueryTests(TestDatabase db)
    {
        _processRepo = new ProcessRepository(TestDatabase.ConnectionString);
        _messageRepo = new MessageRepository(TestDatabase.ConnectionString);
        _messageLog = new MessageLog(TestDatabase.ConnectionString);
    }

    [Fact]
    public async Task GetConversationAsync_returns_all_messages_for_single_correlation_id()
    {
        var ct = CancellationToken.None;
        var clock = new TestClock();
        var sm = new Application.Lifecycle.ProcessStateMachine(_processRepo, clock);

        // 1. Create process and progress to cancellation_pending
        var request = await sm.CreateRequestAsync("571313100000099999", "supplier_switch", new DateOnly(2025, 6, 1), ct);
        await sm.MarkSentAsync(request.Id, "corr-conv-test-001", ct);
        await sm.MarkAcknowledgedAsync(request.Id, ct);
        await sm.MarkCancellationSentAsync(request.Id, ct);

        // 2. Record outbound for original BRS-001
        await _messageRepo.RecordOutboundRequestAsync(
            "supplier_switch", "571313100000099999", "corr-conv-test-001", "acknowledged_ok", ct);

        // 3. Record outbound for RSM-024 cancel (same correlation ID)
        await _messageRepo.RecordOutboundRequestAsync(
            "cancel_switch", "571313100000099999", "corr-conv-test-001", "sent", ct);

        // 4. Record inbound RSM-001 ack for original
        await _messageLog.RecordInboundAsync(
            "msg-rsm001-orig", "RSM-001", "corr-conv-test-001", "MasterData", 100, ct);
        await _messageLog.MarkProcessedAsync("msg-rsm001-orig", ct);

        // 5. Record inbound RSM-001 cancel ack (same correlation ID)
        await _messageLog.RecordInboundAsync(
            "msg-rsm001-cancel", "RSM-001", "corr-conv-test-001", "MasterData", 100, ct);
        await _messageLog.MarkProcessedAsync("msg-rsm001-cancel", ct);

        // 6. Query conversation — should include all messages
        var conversation = await _messageRepo.GetConversationAsync("corr-conv-test-001", ct);

        conversation.Should().NotBeNull();
        conversation!.Outbound.Should().HaveCount(2, "should include both RSM-001 and RSM-024 outbound");
        conversation.Inbound.Should().HaveCount(2, "should include both original and cancel RSM-001 inbound");

        conversation.Outbound.Should().OnlyContain(o => o.CorrelationId == "corr-conv-test-001");
        conversation.Inbound.Should().OnlyContain(i => i.CorrelationId == "corr-conv-test-001");
    }

    [Fact]
    public async Task GetConversationsAsync_counts_include_cancel_messages()
    {
        var ct = CancellationToken.None;
        var clock = new TestClock();
        var sm = new Application.Lifecycle.ProcessStateMachine(_processRepo, clock);

        // Create process with cancellation
        var request = await sm.CreateRequestAsync("571313100000088888", "supplier_switch", new DateOnly(2025, 7, 1), ct);
        await sm.MarkSentAsync(request.Id, "corr-conv-summary-001", ct);
        await sm.MarkAcknowledgedAsync(request.Id, ct);
        await sm.MarkCancellationSentAsync(request.Id, ct);

        // Record outbound for both (same correlation ID)
        await _messageRepo.RecordOutboundRequestAsync(
            "supplier_switch", "571313100000088888", "corr-conv-summary-001", "acknowledged_ok", ct);
        await _messageRepo.RecordOutboundRequestAsync(
            "cancel_switch", "571313100000088888", "corr-conv-summary-001", "sent", ct);

        // Record inbound for both (same correlation ID)
        await _messageLog.RecordInboundAsync(
            "msg-summary-orig", "RSM-001", "corr-conv-summary-001", "MasterData", 100, ct);
        await _messageLog.RecordInboundAsync(
            "msg-summary-cancel", "RSM-001", "corr-conv-summary-001", "MasterData", 100, ct);

        // Query conversations list
        var result = await _messageRepo.GetConversationsAsync(1, 50, ct);

        var conv = result.Items.FirstOrDefault(c => c.CorrelationId == "corr-conv-summary-001");
        conv.Should().NotBeNull();
        conv!.OutboundCount.Should().Be(2, "should count both original and cancel outbound");
        conv.InboundCount.Should().Be(2, "should count both original and cancel inbound");
    }
}
