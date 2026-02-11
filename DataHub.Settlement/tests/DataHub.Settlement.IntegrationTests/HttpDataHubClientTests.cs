using DataHub.Settlement.Application.DataHub;
using DataHub.Settlement.Infrastructure.DataHub;
using DataHub.Settlement.Simulator;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

public class HttpDataHubClientTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpDataHubClient _sut;
    private readonly HttpClient _admin;

    public HttpDataHubClientTests(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        _sut = new HttpDataHubClient(client);
        _admin = factory.CreateClient();
    }

    [Fact]
    public async Task Peek_returns_message_after_scenario_load()
    {
        await _admin.PostAsync("/admin/reset", null);
        await _admin.PostAsync("/admin/scenario/sunshine", null);

        var msg = await _sut.PeekAsync(QueueName.MasterData, CancellationToken.None);

        msg.Should().NotBeNull();
        msg!.MessageType.Should().Be("RSM-022");
        msg.RawPayload.Should().Contain("571313100000012345");
    }

    [Fact]
    public async Task SendRequest_returns_accepted_response()
    {
        await _admin.PostAsync("/admin/reset", null);

        var response = await _sut.SendRequestAsync("supplier_switch", "{\"test\":true}", CancellationToken.None);

        response.Accepted.Should().BeTrue();
        response.CorrelationId.Should().NotBeNullOrEmpty();
    }
}
