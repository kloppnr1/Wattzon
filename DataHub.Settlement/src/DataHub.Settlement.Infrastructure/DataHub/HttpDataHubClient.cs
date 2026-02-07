using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DataHub.Settlement.Application.DataHub;

namespace DataHub.Settlement.Infrastructure.DataHub;

public sealed class HttpDataHubClient : IDataHubClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;

    private static readonly Dictionary<string, string> ProcessTypeToEndpoint = new()
    {
        ["supplier_switch"] = "requestchangeofsupplier",
        ["short_notice_switch"] = "requestchangeofsupplier",
        ["move_in"] = "requestchangeofsupplier",
        ["end_of_supply"] = "requestendofsupply",
        ["forced_end_of_supply"] = "requestendofsupply",
        ["move_out"] = "requestendofsupply",
        ["cancel_switch"] = "requestcancelchangeofsupplier",
        ["cancel_end_of_supply"] = "requestcancelchangeofsupplier",
    };

    public HttpDataHubClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<DataHubMessage?> PeekAsync(QueueName queue, CancellationToken ct)
    {
        var response = await _http.GetAsync($"/v1.0/cim/{queue}", ct);

        if (response.StatusCode == HttpStatusCode.NoContent)
            return null;

        response.EnsureSuccessStatusCode();
        var msg = await response.Content.ReadFromJsonAsync<PeekResponse>(JsonOptions, ct);
        if (msg is null)
            return null;

        return new DataHubMessage(msg.MessageId, msg.MessageType, msg.CorrelationId, msg.Content);
    }

    public async Task DequeueAsync(string messageId, CancellationToken ct)
    {
        var response = await _http.DeleteAsync($"/v1.0/cim/dequeue/{messageId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<DataHubResponse> SendRequestAsync(string processType, string cimPayload, CancellationToken ct)
    {
        var endpoint = ProcessTypeToEndpoint.GetValueOrDefault(processType, "requestchangeofsupplier");
        var content = new StringContent(cimPayload, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"/v1.0/cim/{endpoint}", content, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SendResponse>(JsonOptions, ct);
        return new DataHubResponse(
            result!.CorrelationId,
            result.Accepted,
            result.RejectReason);
    }

    private record PeekResponse(string MessageId, string MessageType, string? CorrelationId, string Content);
    private record SendResponse(string CorrelationId, bool Accepted, string? RejectReason = null);
}
