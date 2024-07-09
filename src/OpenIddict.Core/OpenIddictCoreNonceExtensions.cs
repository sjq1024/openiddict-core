using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Core.NonceUtils;

namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.Options;
using OpenIddict.Abstractions.Managers;

public static class OpenIddictCoreNonceExtensions
{
    /// <summary>
    /// Registers the OpenIddict core services in the DI container.
    /// </summary>
    /// <param name="builder">The services builder used by OpenIddict to register new services.</param>
    /// <remarks>This extension can be safely called multiple times.</remarks>
    /// <returns>The <see cref="OpenIddictBuilder"/> instance.</returns>
    public static OpenIddictBuilder AddNonce(this OpenIddictBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.Services.AddLogging();
        builder.Services.AddMemoryCache();
        builder.Services.AddOptions();

        builder.Services.AddSingleton<IOpenIddictNonceManager, OpenIddictNonceManager>();
        builder.Services.AddHostedService<OpenIddictNonceRefresher>();

        return builder;
    }
}