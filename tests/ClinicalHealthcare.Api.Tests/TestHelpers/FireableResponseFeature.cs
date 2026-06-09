using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace ClinicalHealthcare.Api.Tests;

/// <summary>
/// Test double for <see cref="IHttpResponseFeature"/> that exposes a
/// <see cref="FireAsync"/> method to manually trigger <c>OnStarting</c> callbacks.
/// This is required when testing middleware that registers <c>Response.OnStarting</c>
/// callbacks, because in-memory <see cref="Microsoft.AspNetCore.Http.DefaultHttpContext"/>
/// never starts the response automatically.
/// </summary>
internal sealed class FireableResponseFeature : IHttpResponseFeature
{
    private readonly List<(Func<object, Task> Cb, object State)> _callbacks = [];

    public int StatusCode { get; set; } = StatusCodes.Status200OK;
    public string? ReasonPhrase { get; set; }
    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
    public Stream Body { get; set; } = Stream.Null;
    public bool HasStarted { get; private set; }

    public void OnStarting(Func<object, Task> callback, object state)
        => _callbacks.Insert(0, (callback, state));

    public void OnCompleted(Func<object, Task> callback, object state) { }

    public async Task FireAsync()
    {
        if (HasStarted) return;
        HasStarted = true;
        foreach (var (cb, state) in _callbacks)
            await cb(state);
    }
}
