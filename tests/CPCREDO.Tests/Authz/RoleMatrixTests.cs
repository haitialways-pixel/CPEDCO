using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace CPCREDO.Tests.Authz;

public sealed class RoleMatrixTests : IClassFixture<AuthzWebFactory>
{
    private readonly AuthzWebFactory _factory;

    public RoleMatrixTests(AuthzWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Caissier_post_member_is_403()
    {
        var client = _factory.CreateClient();
        var token = await _factory.LoginCaissierAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var me = await client.GetAsync("/api/auth/me");
        Assert.True(me.IsSuccessStatusCode, await me.Content.ReadAsStringAsync());

        var response = await client.PostAsJsonAsync(
            $"/api/v1/members/{Guid.NewGuid()}",
            new { firstName = "Anne", lastName = "Test" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Caissier_journal_reversal_is_403()
    {
        var client = _factory.CreateClient();
        var token = await _factory.LoginCaissierAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/v1/journals/{Guid.NewGuid()}/reversal",
            new { reason = "test" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ServiceClient_till_open_is_403()
    {
        var client = _factory.CreateClient();
        var token = await _factory.LoginServiceClientAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.PostAsJsonAsync(
            "/api/v1/tills/open",
            new { currencyCode = "HTG", openingFloat = 100 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
