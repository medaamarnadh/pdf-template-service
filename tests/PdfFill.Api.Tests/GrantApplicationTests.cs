using System.Net;

namespace PdfFill.Api.Tests;

public class GrantApplicationTests : IClassFixture<TestHost>
{
    private readonly TestHost _host;
    public GrantApplicationTests(TestHost host) => _host = host;

    [Fact]
    public async Task Demo_data_is_served_from_the_renamed_path()
    {
        var portal = await _host.PortalClientAsync();
        var r = await portal.GetAsync("/api/v1/grant-applications/5001");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("applicant_name", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Old_demo_path_no_longer_serves_business_data()
    {
        var portal = await _host.PortalClientAsync();
        var r = await portal.GetAsync("/api/v1/applications/5001");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}
