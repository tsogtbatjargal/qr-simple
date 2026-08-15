using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace QrSimple.Api.Tests;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task Unhandled_exception_returns_a_safe_generic_500_response()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        var handled = await handler.TryHandleAsync(context, new InvalidOperationException("connection string leaked here"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await JsonSerializer.DeserializeAsync<string>(context.Response.Body);

        Assert.DoesNotContain("connection string leaked here", body);
        Assert.Contains("unexpected error", body, StringComparison.OrdinalIgnoreCase);
    }
}
