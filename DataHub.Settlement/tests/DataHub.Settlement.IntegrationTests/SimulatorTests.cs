using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DataHub.Settlement.Simulator;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DataHub.Settlement.IntegrationTests;

public class SimulatorTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _client;

    public SimulatorTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Token_endpoint_returns_bearer_token()
    {
        var response = await _client.PostAsync("/oauth2/v2.0/token", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("access_token").GetString().Should().StartWith("sim-token-");
        json.GetProperty("token_type").GetString().Should().Be("Bearer");
    }

    [Fact]
    public async Task Scenario_loads_and_queue_peek_returns_message()
    {
        await _client.PostAsync("/admin/reset", null);
        var loadResponse = await _client.PostAsync("/admin/scenario/sunshine", null);
        loadResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var peekResponse = await _client.GetAsync("/v1.0/cim/MasterData");
        peekResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var msg = await peekResponse.Content.ReadFromJsonAsync<PeekResponse>(JsonOptions);
        msg!.MessageType.Should().Be("RSM-001");
    }

    [Fact]
    public async Task Dequeue_removes_message()
    {
        await _client.PostAsync("/admin/reset", null);
        await _client.PostAsync("/admin/scenario/sunshine", null);

        // Peek to get message ID (first message is RSM-001)
        var peekResponse = await _client.GetAsync("/v1.0/cim/MasterData");
        var msg = await peekResponse.Content.ReadFromJsonAsync<PeekResponse>(JsonOptions);

        // Dequeue
        var dequeueResponse = await _client.DeleteAsync($"/v1.0/cim/dequeue/{msg!.MessageId}");
        dequeueResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Peek again — next message should be RSM-028 (sunshine has RSM-001, RSM-028, RSM-031, RSM-022)
        var peekResponse2 = await _client.GetAsync("/v1.0/cim/MasterData");
        peekResponse2.StatusCode.Should().Be(HttpStatusCode.OK);
        var msg2 = await peekResponse2.Content.ReadFromJsonAsync<PeekResponse>(JsonOptions);
        msg2!.MessageType.Should().Be("RSM-028");
    }

    [Fact]
    public async Task Move_in_scenario_loads_and_returns_master_data()
    {
        await _client.PostAsync("/admin/reset", null);
        var loadResponse = await _client.PostAsync("/admin/scenario/move_in", null);
        loadResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var peekResponse = await _client.GetAsync("/v1.0/cim/MasterData");
        peekResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var msg = await peekResponse.Content.ReadFromJsonAsync<PeekResponse>(JsonOptions);
        msg!.MessageType.Should().Be("RSM-022");
    }

    [Fact]
    public async Task Move_out_scenario_loads_and_returns_master_data()
    {
        await _client.PostAsync("/admin/reset", null);
        var loadResponse = await _client.PostAsync("/admin/scenario/move_out", null);
        loadResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var peekResponse = await _client.GetAsync("/v1.0/cim/MasterData");
        peekResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var msg = await peekResponse.Content.ReadFromJsonAsync<PeekResponse>(JsonOptions);
        msg!.MessageType.Should().Be("RSM-022");
    }

    private record PeekResponse(string MessageId, string MessageType, string? CorrelationId, string Content);
}
