﻿/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/openiddict/openiddict-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using OpenIddict.Abstractions.Managers;
using Properties = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreConstants.Properties;

#if SUPPORTS_JSON_NODES
using System.Text.Json.Nodes;
#endif

namespace OpenIddict.Validation.AspNetCore;

[EditorBrowsable(EditorBrowsableState.Never)]
public static partial class OpenIddictValidationAspNetCoreHandlers
{
    public static ImmutableArray<OpenIddictValidationHandlerDescriptor> DefaultHandlers { get; } = ImmutableArray.Create([
        /*
         * Request top-level processing:
         */
        ResolveRequestUri.Descriptor,

        /*
         * Authentication processing:
         */
        ValidateHostHeader.Descriptor,
        ValidateDPoPHeader.Descriptor,
        ExtractAccessTokenFromAuthorizationHeader.Descriptor,
        ExtractAccessTokenFromBodyForm.Descriptor,
        ExtractAccessTokenFromQueryString.Descriptor,
        AttacDPoPNonceHeader<ProcessAuthenticationContext>.Descriptor,

        /*
         * Challenge processing:
         */
        ResolveHostChallengeProperties.Descriptor,
        AttachHostChallengeError.Descriptor,

        /*
         * Response processing:
         */
        AttachHttpResponseCode<ProcessChallengeContext>.Descriptor,
        AttachCacheControlHeader<ProcessChallengeContext>.Descriptor,
        AttachWwwAuthenticateHeader<ProcessChallengeContext>.Descriptor,
        AttacDPoPNonceHeader<ProcessChallengeContext>.Descriptor,
        ProcessChallengeErrorResponse<ProcessChallengeContext>.Descriptor,

        AttachHttpResponseCode<ProcessErrorContext>.Descriptor,
        AttachCacheControlHeader<ProcessErrorContext>.Descriptor,
        AttachWwwAuthenticateHeader<ProcessErrorContext>.Descriptor,
        AttacDPoPNonceHeader<ProcessErrorContext>.Descriptor,
        ProcessChallengeErrorResponse<ProcessErrorContext>.Descriptor
    ]);

    /// <summary>
    /// Contains the logic responsible for resolving the request URI from the ASP.NET Core environment.
    /// Note: this handler is not used when the OpenID Connect request is not initially handled by ASP.NET Core.
    /// </summary>
    public sealed class ResolveRequestUri : IOpenIddictValidationHandler<ProcessRequestContext>
    {
        /// <summary>
        /// Gets the default descriptor definition assigned to this handler.
        /// </summary>
        public static OpenIddictValidationHandlerDescriptor Descriptor { get; }
            = OpenIddictValidationHandlerDescriptor.CreateBuilder<ProcessRequestContext>()
                .AddFilter<RequireHttpRequest>()
                .UseSingletonHandler<ResolveRequestUri>()
                .SetOrder(int.MinValue + 50_000)
                .SetType(OpenIddictValidationHandlerType.BuiltIn)
                .Build();

        /// <inheritdoc/>
        public ValueTask HandleAsync(ProcessRequestContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // This handler only applies to ASP.NET Core requests. If the HTTP context cannot be resolved,
            // this may indicate that the request was incorrectly processed by another server stack.
            var request = context.Transaction.GetHttpRequest() ??
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0114));

            // OpenIddict supports both absolute and relative URIs for all its endpoints, but only absolute
            // URIs can be properly canonicalized by the BCL System.Uri class (e.g './path/../' is normalized
            // to './' once the URI is fully constructed). At this stage of the request processing, rejecting
            // requests that lack the host information (e.g because HTTP/1.0 was used and no Host header was
            // sent by the HTTP client) is not desirable as it would affect all requests, including requests
            // that are not meant to be handled by OpenIddict itself. To avoid that, a fake host is temporarily
            // used to build an absolute base URI and a request URI that will be used to determine whether the
            // received request matches one of the URIs assigned to an OpenIddict endpoint. If the request
            // is later handled by OpenIddict, an additional check will be made to require the Host header.

            (context.BaseUri, context.RequestUri) = request.Host switch
            {
                { HasValue: true } host => (
                    BaseUri: new Uri(request.Scheme + Uri.SchemeDelimiter + host + request.PathBase, UriKind.Absolute),
                    RequestUri: new Uri(request.GetEncodedUrl(), UriKind.Absolute)),

                { HasValue: false } => (
                    BaseUri: new UriBuilder
                    {
                        Scheme = request.Scheme,
                        Path = request.PathBase.ToUriComponent()
                    }.Uri,
                    RequestUri: new UriBuilder
                    {
                        Scheme = request.Scheme,
                        Path = (request.PathBase + request.Path).ToUriComponent(),
                        Query = request.QueryString.ToUriComponent()
                    }.Uri)
            };

            return default;
        }
    }

    /// <summary>
    /// Contains the logic responsible for validating the Host header extracted from the HTTP header.
    /// Note: this handler is not used when the OpenID Connect request is not initially handled by ASP.NET Core.
    /// </summary>
    public sealed class ValidateHostHeader : IOpenIddictValidationHandler<ProcessAuthenticationContext>
    {
        /// <summary>
        /// Gets the default descriptor definition assigned to this handler.
        /// </summary>
        public static OpenIddictValidationHandlerDescriptor Descriptor { get; }
            = OpenIddictValidationHandlerDescriptor.CreateBuilder<ProcessAuthenticationContext>()
                .AddFilter<RequireHttpRequest>()
                .UseSingletonHandler<ValidateHostHeader>()
                .SetOrder(int.MinValue + 50_000)
                .SetType(OpenIddictValidationHandlerType.BuiltIn)
                .Build();

        /// <inheritdoc/>
        public ValueTask HandleAsync(ProcessAuthenticationContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // This handler only applies to ASP.NET Core requests. If the HTTP context cannot be resolved,
            // this may indicate that the request was incorrectly processed by another server stack.
            var request = context.Transaction.GetHttpRequest() ??
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0114));

            // Don't require that a Host header be present if the issuer was set in the options.
            if (context.Options.Issuer is null && !request.Host.HasValue)
            {
                context.Reject(
                    error: Errors.InvalidRequest,
                    description: SR.FormatID2081(HeaderNames.Host),
                    uri: SR.FormatID8000(SR.ID2081));

                return default;
            }

            return default;
        }
    }

    /// <summary>
    /// Contains the logic responsible for extracting the access token from the standard HTTP Authorization header.
    /// Note: this handler is not used when the OpenID Connect request is not initially handled by ASP.NET Core.
    /// </summary>
    public sealed class ExtractAccessTokenFromAuthorizationHeader : IOpenIddictValidationHandler<ProcessAuthenticationContext>
    {
        /// <summary>
        /// Gets the default descriptor definition assigned to this handler.
        /// </summary>
        public static OpenIddictValidationHandlerDescriptor Descriptor { get; }
            = OpenIddictValidationHandlerDescriptor.CreateBuilder<ProcessAuthenticationContext>()
                .AddFilter<RequireHttpRequest>()
                .AddFilter<RequireAccessTokenExtracted>()
                .AddFilter<RequireAccessTokenExtractionFromAuthorizationHeaderEnabled>()
                .UseSingletonHandler<ExtractAccessTokenFromAuthorizationHeader>()
                .SetOrder(EvaluateValidatedTokens.Descriptor.Order + 500)
                .SetType(OpenIddictValidationHandlerType.BuiltIn)
                .Build();

        /// <inheritdoc/>
        public ValueTask HandleAsync(ProcessAuthenticationContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // If a token was already resolved, don't overwrite it.
            if (!string.IsNullOrEmpty(context.AccessToken))
            {
                return default;
            }

            // This handler only applies to ASP.NET Core requests. If the HTTP context cannot be resolved,
            // this may indicate that the request was incorrectly processed by another server stack.
            var request = context.Transaction.GetHttpRequest() ??
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0114));

            // Resolve the access token from the standard Authorization header.
            // See https://tools.ietf.org/html/rfc6750#section-2.1 for more information.
            string? header = request.Headers[HeaderNames.Authorization];
            if (!string.IsNullOrEmpty(header))
            {
                if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    context.AccessToken = header["Bearer ".Length..];
                }
                else if (header.StartsWith("DPoP ", StringComparison.OrdinalIgnoreCase))
                {
                    context.AccessToken = header["DPoP ".Length..];
                    context.Options.RequireDPoPValidation = true;
                }

                return default;
            }

            return default;
        }
    }

    /// <summary>
    /// Contains the logic responsible for extracting the access token from the standard access_token form parameter.
    /// Note: this handler is not used when the OpenID Connect request is not initially handled by ASP.NET Core.
    /// </summary>
    public sealed class ExtractAccessTokenFromBodyForm : IOpenIddictValidationHandler<ProcessAuthenticationContext>
    {
        /// <summary>
        /// Gets the default descriptor definition assigned to this handler.
        /// </summary>
        public static OpenIddictValidationHandlerDescriptor Descriptor { get; }
            = OpenIddictValidationHandlerDescriptor.CreateBuilder<ProcessAuthenticationContext>()
                .AddFilter<RequireHttpRequest>()
                .AddFilter<RequireAccessTokenExtracted>()
                .AddFilter<RequireAccessTokenExtractionFromBodyFormEnabled>()
                .UseSingletonHandler<ExtractAccessTokenFromBodyForm>()
                .SetOrder(ExtractAccessTokenFromAuthorizationHeader.Descriptor.Order + 1_000)
                .SetType(OpenIddictValidationHandlerType.BuiltIn)
                .Build();

        /// <inheritdoc/>
        public async ValueTask HandleAsync(ProcessAuthenticationContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // If a token was already resolved, don't overwrite it.
            if (!string.IsNullOrEmpty(context.AccessToken))
            {
                return;
            }

            // This handler only applies to ASP.NET Core requests. If the HTTP context cannot be resolved,
            // this may indicate that the request was incorrectly processed by another server stack.
            var request = context.Transaction.GetHttpRequest() ??
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0114));

            if (string.IsNullOrEmpty(request.ContentType) ||
                !request.ContentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Resolve the access token from the standard access_token form parameter.
            // See https://tools.ietf.org/html/rfc6750#section-2.2 for more information.
            var form = await request.ReadFormAsync(context.CancellationToken);
            if (form.TryGetValue(Parameters.AccessToken, out StringValues token))
            {
                context.AccessToken = token;

                return;
            }
        }
    }

    /// <summary>
    /// Contains the logic responsible for extracting the access token from the standard access_token query parameter.
    /// Note: this handler is not used when the OpenID Connect request is not initially handled by ASP.NET Core.
    /// </summary>
    public sealed class ExtractAccessTokenFromQueryString : IOpenIddictValidationHandler<ProcessAuthenticationContext>
    {
        /// <summary>
        /// Gets the default descriptor definition assigned to this handler.
        /// </summary>
        public static OpenIddictValidationHandlerDescriptor Descriptor { get; }
            = OpenIddictValidationHandlerDescriptor.CreateBuilder<ProcessAuthenticationContext>()
                .AddFilter<RequireHttpRequest>()
                .AddFilter<RequireAccessTokenExtracted>()
                .AddFilter<RequireAccessTokenExtractionFromQueryStringEnabled>()
                .UseSingletonHandler<ExtractAccessTokenFromQueryString>()
                .SetOrder(ExtractAccessTokenFromBodyForm.Descriptor.Order + 1_000)
                .SetType(OpenIddictValidationHandlerType.BuiltIn)
                .Build();

        /// <inheritdoc/>
        public ValueTask HandleAsync(ProcessAuthenticationContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // If a token was already resolved, don't overwrite it.
            if (!string.IsNullOrEmpty(context.AccessToken))
            {
                return default;
            }

            // This handler only applies to ASP.NET Core requests. If the HTTP context cannot be resolved,
            // this may indicate that the request was incorrectly processed by another server stack.
            var request = context.Transaction.GetHttpRequest() ??
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0114));

            // Resolve the access token from the standard access_token query parameter.
            // See https://tools.ietf.org/html/rfc6750#section-2.3 for more information.
            if (request.Query.TryGetValue(Parameters.AccessToken, out StringValues token))
            {
                context.AccessToken = token;

                return default;
            }

            return default;
        }
    }

    public sealed class ValidateDPoPHeader : IOpenIddictValidationHandler<ProcessAuthenticationContext>
    {
        private readonly IOptionsMonitor<OpenIddictValidationOptions> _options;

        private readonly IOpenIddictNonceManager? _nonceManager;

        public ValidateDPoPHeader(IOptionsMonitor<OpenIddictValidationOptions> options, IOpenIddictNonceManager nonceManager)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _nonceManager = nonceManager;
        }

        /// <summary>
        /// Gets the default descriptor definition assigned to this handler.
        /// </summary>
        public static OpenIddictValidationHandlerDescriptor Descriptor { get; }
            = OpenIddictValidationHandlerDescriptor.CreateBuilder<ProcessAuthenticationContext>()
                .AddFilter<RequireHttpRequest>()
                .AddFilter<RequireDPoPValidation>()
                .UseSingletonHandler<ValidateDPoPHeader>()
                .SetOrder(ExtractAccessTokenFromQueryString.Descriptor.Order + 1_000)
                .SetType(OpenIddictValidationHandlerType.BuiltIn)
                .Build();

        /// <inheritdoc/>
        public ValueTask HandleAsync(ProcessAuthenticationContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // This handler only applies to ASP.NET Core requests. If the HTTP context cannot be resolved,
            // this may indicate that the request was incorrectly processed by another server stack.
            var request = context.Transaction.GetHttpRequest() ??
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0114));

            // Reject requests that lack the DPoP header.
            if (!request.Headers.ContainsKey("dpop"))
            {
                context.Reject(error: Errors.InvalidRequest, description: "Please attach DPoP Proof");
                return default;
            }

            // Reject requests that include more than one DPoP header.
            if (request.Headers["dpop"].Count != 1)
            {
                context.Reject(error: Errors.InvalidRequest, description: "Don't include more than one DPoP header");
                return default;
            }

            var DPoPHeader = request.Headers["dpop"][0];
            var securityTokenHandler = context.Options.JsonWebTokenHandler;
            var token = securityTokenHandler.ReadJsonWebToken(DPoPHeader);


            var existenceOfRequiredClaims = token.TryGetHeaderValue<string>(Claims.Type, out var typeHeader) & 
                token.TryGetClaim(Claims.HttpMethod, out var htmClaim) &
                token.TryGetClaim(Claims.HttpTargetURI, out var htuClaim) &
                token.TryGetClaim(Claims.IssuedAt, out var iatClaim) &
                token.TryGetClaim(Claims.JwtId, out var jtiClaim);
            // Check the existence of all calims
            if (!existenceOfRequiredClaims)
            {
                context.Reject(error: Errors.InvalidDPoPProof,
                    description: "The DPoP header doesn't contain required claims");
                return default;
            }

            // Check the type claim
            if (typeHeader != JsonWebTokenTypes.DPoPProof)
            {
                context.Reject(error: Errors.InvalidDPoPProof,
                    description: "The type of the DPoP JWT is not dpop+jwt");
                return default;
            }

            // Check the HTTP method
            if (htmClaim.Value != request.Method)
            {
                context.Reject(error: Errors.InvalidDPoPProof,
                    description: "htm claims of DPoP JWT header doesn't match current request");
                return default;
            }

            // Check the HTTP target URI
            if (context is not { RequestUri.IsAbsoluteUri: true })
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0127));
            }

            if (htuClaim.Value != context.RequestUri.GetLeftPart(UriPartial.Path))
            {
                context.Reject(error: Errors.InvalidDPoPProof,
                    description: "htu claimsof DPoP JWT header doesn't match current request");
                return default;
            }

            // Check the nonce claim
            if (_nonceManager is not null)
            {
                if (!token.TryGetClaim(Claims.Nonce, out var nonceClaim))
                {
                    context.Reject(error: Errors.InvalidDPoPProof,
                        description: "The DPoP header doesn't contain nonce claim");
                    return default;
                }

                if (!_nonceManager.ValidateNonce(nonceClaim.Value))
                {
                    context.Reject(error: Errors.InvalidDPoPProof,
                        description: "The DPoP header doesn't has valid nonce claim");
                    return default;
                }
            }

            context.DPoPProof = DPoPHeader;

            return default;
        }
    }

    /// <summary>
    /// Contains the logic responsible for resolving the context-specific properties and parameters stored in the
    /// ASP.NET Core authentication properties specified by the application that triggered the challenge operation.
    /// Note: this handler is not used when the OpenID Connect request is not initially handled by ASP.NET Core.
    /// </summary>
    public sealed class ResolveHostChallengeProperties : IOpenIddictValidationHandler<ProcessChallengeContext>
    {
        /// <summary>
        /// Gets the default descriptor definition assigned to this handler.
        /// </summary>
        public static OpenIddictValidationHandlerDescriptor Descriptor { get; }
            = OpenIddictValidationHandlerDescriptor.CreateBuilder<ProcessChallengeContext>()
                .AddFilter<RequireHttpRequest>()
                .UseSingletonHandler<ResolveHostChallengeProperties>()
                .SetOrder(AttachHostChallengeError.Descriptor.Order - 500)
                .SetType(OpenIddictValidationHandlerType.BuiltIn)
                .Build();

        /// <inheritdoc/>
        public ValueTask HandleAsync(ProcessChallengeContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var properties = context.Transaction.GetProperty<AuthenticationProperties>(typeof(AuthenticationProperties).FullName!);
            if (properties is { Items.Count: > 0 })
            {
                foreach (var property in properties.Items)
                {
                    context.Properties[property.Key] = property.Value;
                }
            }

            if (properties is { Parameters.Count: > 0 })
            {
                foreach (var parameter in properties.Parameters)
                {
                    context.Parameters[parameter.Key] = parameter.Value switch
                    {
                        OpenIddictParameter value => value,
                        JsonElement         value => new OpenIddictParameter(value),
                        bool                value => new OpenIddictParameter(value),
                        int                 value => new OpenIddictParameter(value),
                        long                value => new OpenIddictParameter(value),
                        string              value => new OpenIddictParameter(value),
                        string[]            value => new OpenIddictParameter(value),

    #if SUPPORTS_JSON_NODES
                        JsonNode            value => new OpenIddictParameter(value),
    #endif
                        _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0115))
                    };
                }
            }

            return default;
        }
    }

    /// <summary>
    /// Contains the logic responsible for attaching the error details using the ASP.NET Core authentication properties.
    /// Note: this handler is not used when the OpenID Connect request is not initially handled by ASP.NET Core.
    /// </summary>
    public sealed class AttachHostChallengeError : IOpenIddictValidationHandler<ProcessChallengeContext>
    {
        /// <summary>
        /// Gets the default descriptor definition assigned to this handler.
        /// </summary>
        public static OpenIddictValidationHandlerDescriptor Descriptor { get; }
            = OpenIddictValidationHandlerDescriptor.CreateBuilder<ProcessChallengeContext>()
                .AddFilter<RequireHttpRequest>()
                .UseSingletonHandler<AttachHostChallengeError>()
                .SetOrder(AttachDefaultChallengeError.Descriptor.Order - 500)
                .SetType(OpenIddictValidationHandlerType.BuiltIn)
                .Build();

        /// <inheritdoc/>
        public ValueTask HandleAsync(ProcessChallengeContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var properties = context.Transaction.GetProperty<AuthenticationProperties>(typeof(AuthenticationProperties).FullName!);
            if (properties is not null)
            {
                context.Response.Error = properties.GetString(Properties.Error);
                context.Response.ErrorDescription = properties.GetString(Properties.ErrorDescription);
                context.Response.ErrorUri = properties.GetString(Properties.ErrorUri);
                context.Response.Scope = properties.GetString(Properties.Scope);
            }

            return default;
        }
    }

    /// <summary>
    /// Contains the logic responsible for attaching an appropriate HTTP status code.
    /// Note: this handler is not used when the OpenID Connect request is not initially handled by ASP.NET Core.
    /// </summary>
    public sealed class AttachHttpResponseCode<TContext> : IOpenIddictValidationHandler<TContext> where TContext : BaseRequestContext
    {
        /// <summary>
        /// Gets the default descriptor definition assigned to this handler.
        /// </summary>
        public static OpenIddictValidationHandlerDescriptor Descriptor { get; }
            = OpenIddictValidationHandlerDescriptor.CreateBuilder<TContext>()
                .AddFilter<RequireHttpRequest>()
                .UseSingletonHandler<AttachHttpResponseCode<TContext>>()
                .SetOrder(100_000)
                .SetType(OpenIddictValidationHandlerType.BuiltIn)
                .Build();

        /// <inheritdoc/>
        public ValueTask HandleAsync(TContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            Debug.Assert(context.Transaction.Response is not null, SR.GetResourceString(SR.ID4007));

            // This handler only applies to ASP.NET Core requests. If the HTTP context cannot be resolved,
            // this may indicate that the request was incorrectly processed by another server stack.
            var response = context.Transaction.GetHttpRequest()?.HttpContext.Response ??
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0114));

            response.StatusCode = context.Transaction.Response.Error switch
            {
                // Note: the default code may be replaced by another handler (e.g when doing redirects).
                null or { Length: 0 } => 200,

                Errors.InvalidToken or Errors.MissingToken => 401,

                Errors.InsufficientAccess or Errors.InsufficientScope => 403,

                Errors.ServerError => 500,

                _ => 400
            };

            return default;
        }
    }

    /// <summary>
    /// Contains the logic responsible for attaching the appropriate HTTP response cache headers.
    /// Note: this handler is not used when the OpenID Connect request is not initially handled by ASP.NET Core.
    /// </summary>
    public sealed class AttachCacheControlHeader<TContext> : IOpenIddictValidationHandler<TContext> where TContext : BaseRequestContext
    {
        /// <summary>
        /// Gets the default descriptor definition assigned to this handler.
        /// </summary>
        public static OpenIddictValidationHandlerDescriptor Descriptor { get; }
            = OpenIddictValidationHandlerDescriptor.CreateBuilder<TContext>()
                .AddFilter<RequireHttpRequest>()
                .UseSingletonHandler<AttachCacheControlHeader<TContext>>()
                .SetOrder(AttachHttpResponseCode<TContext>.Descriptor.Order + 1_000)
                .SetType(OpenIddictValidationHandlerType.BuiltIn)
                .Build();

        /// <inheritdoc/>
        public ValueTask HandleAsync(TContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // This handler only applies to ASP.NET Core requests. If the HTTP context cannot be resolved,
            // this may indicate that the request was incorrectly processed by another server stack.
            var response = context.Transaction.GetHttpRequest()?.HttpContext.Response ??
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0114));

            // Prevent the response from being cached.
            response.Headers[HeaderNames.CacheControl] = "no-store";
            response.Headers[HeaderNames.Pragma] = "no-cache";
            response.Headers[HeaderNames.Expires] = "Thu, 01 Jan 1970 00:00:00 GMT";

            return default;
        }
    }

    /// <summary>
    /// Contains the logic responsible for attaching errors details to the WWW-Authenticate header.
    /// Note: this handler is not used when the OpenID Connect request is not initially handled by ASP.NET Core.
    /// </summary>
    public sealed class AttachWwwAuthenticateHeader<TContext> : IOpenIddictValidationHandler<TContext> where TContext : BaseRequestContext
    {
        private readonly IOptionsMonitor<OpenIddictValidationAspNetCoreOptions> _options;

        public AttachWwwAuthenticateHeader(IOptionsMonitor<OpenIddictValidationAspNetCoreOptions> options)
            => _options = options ?? throw new ArgumentNullException(nameof(options));

        /// <summary>
        /// Gets the default descriptor definition assigned to this handler.
        /// </summary>
        public static OpenIddictValidationHandlerDescriptor Descriptor { get; }
            = OpenIddictValidationHandlerDescriptor.CreateBuilder<TContext>()
                .AddFilter<RequireHttpRequest>()
                .UseSingletonHandler<AttachWwwAuthenticateHeader<TContext>>()
                .SetOrder(AttachCacheControlHeader<TContext>.Descriptor.Order + 1_000)
                .SetType(OpenIddictValidationHandlerType.BuiltIn)
                .Build();

        /// <inheritdoc/>
        public ValueTask HandleAsync(TContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            Debug.Assert(context.Transaction.Response is not null, SR.GetResourceString(SR.ID4007));

            // This handler only applies to ASP.NET Core requests. If the HTTP context cannot be resolved,
            // this may indicate that the request was incorrectly processed by another server stack.
            var response = context.Transaction.GetHttpRequest()?.HttpContext.Response ??
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0114));

            if (string.IsNullOrEmpty(context.Transaction.Response.Error))
            {
                return default;
            }

            // Note: unlike the server stack, the validation stack doesn't expose any endpoint
            // and thus never returns responses containing a formatted body (e.g a JSON response).
            //
            // As such, all errors - even errors indicating an invalid request - are returned
            // as part of the standard WWW-Authenticate header, as defined by the specification.
            // See https://datatracker.ietf.org/doc/html/rfc6750#section-3 for more information.

            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);

            // If a realm was configured in the options, attach it to the parameters.
            if (_options.CurrentValue.Realm is string { Length: > 0 } realm)
            {
                parameters[Parameters.Realm] = realm;
            }

            foreach (var parameter in context.Transaction.Response.GetParameters())
            {
                // Note: the error details are only included if the error was not caused by a missing token, as recommended
                // by the OAuth 2.0 bearer specification: https://tools.ietf.org/html/rfc6750#section-3.1.
                if (context.Transaction.Response.Error is Errors.MissingToken &&
                    parameter.Key is Parameters.Error            or
                                     Parameters.ErrorDescription or
                                     Parameters.ErrorUri)
                {
                    continue;
                }

                // Ignore values that can't be represented as unique strings.
                var value = (string?) parameter.Value;
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                parameters[parameter.Key] = value;
            }

            var builder = new StringBuilder(Schemes.Bearer);

            foreach (var parameter in parameters)
            {
                builder.Append(' ');
                builder.Append(parameter.Key);
                builder.Append('=');
                builder.Append('"');
                builder.Append(parameter.Value.Replace("\"", "\\\""));
                builder.Append('"');
                builder.Append(',');
            }

            // If the WWW-Authenticate header ends with a comma, remove it.
            if (builder[^1] == ',')
            {
                builder.Remove(builder.Length - 1, 1);
            }

            response.Headers.Append(HeaderNames.WWWAuthenticate, builder.ToString());

            return default;
        }
    }
    
    public sealed class AttacDPoPNonceHeader<TContext> : IOpenIddictValidationHandler<TContext> where TContext : BaseRequestContext
    {
        private readonly IOptionsMonitor<OpenIddictValidationOptions> _options;

        private readonly IOpenIddictNonceManager? _nonceManager;

        public AttacDPoPNonceHeader(IOptionsMonitor<OpenIddictValidationOptions> options, IOpenIddictNonceManager nonceManager)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _nonceManager = nonceManager ?? throw new ArgumentNullException(nameof(nonceManager));
        }

        /// <summary>
        /// Gets the default descriptor definition assigned to this handler.
        /// </summary>
        public static OpenIddictValidationHandlerDescriptor Descriptor { get; }
            = OpenIddictValidationHandlerDescriptor.CreateBuilder<TContext>()
                .AddFilter<RequireHttpRequest>()
                .UseSingletonHandler<AttacDPoPNonceHeader<TContext>>()
                .SetOrder(AttachWwwAuthenticateHeader<TContext>.Descriptor.Order + 1_000)
                .SetType(OpenIddictValidationHandlerType.BuiltIn)
                .Build();

        /// <inheritdoc/>
        public ValueTask HandleAsync(TContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            //Debug.Assert(context.Transaction.Response is not null, SR.GetResourceString(SR.ID4007));

            // This handler only applies to ASP.NET Core requests. If the HTTP context cannot be resolved,
            // this may indicate that the request was incorrectly processed by another server stack.
            var response = context.Transaction.GetHttpRequest()?.HttpContext.Response ??
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0114));

            if (_nonceManager != null)
            {
                string dpopNonce = _nonceManager.GetLatestNonce();

                if (dpopNonce != null)
                {
                    response.Headers.Append(ResponseHeaders.DPoPNonce, dpopNonce);
                }
            }

            return default;
        }
    }

    /// <summary>
    /// Contains the logic responsible for processing challenge responses that contain a WWW-Authenticate header.
    /// Note: this handler is not used when the OpenID Connect request is not initially handled by ASP.NET Core.
    /// </summary>
    public sealed class ProcessChallengeErrorResponse<TContext> : IOpenIddictValidationHandler<TContext> where TContext : BaseRequestContext
    {
        /// <summary>
        /// Gets the default descriptor definition assigned to this handler.
        /// </summary>
        public static OpenIddictValidationHandlerDescriptor Descriptor { get; }
            = OpenIddictValidationHandlerDescriptor.CreateBuilder<TContext>()
                .AddFilter<RequireHttpRequest>()
                .UseSingletonHandler<ProcessChallengeErrorResponse<TContext>>()
                .SetOrder(500_000)
                .SetType(OpenIddictValidationHandlerType.BuiltIn)
                .Build();

        /// <inheritdoc/>
        public ValueTask HandleAsync(TContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // This handler only applies to ASP.NET Core requests. If the HTTP context cannot be resolved,
            // this may indicate that the request was incorrectly processed by another server stack.
            var response = context.Transaction.GetHttpRequest()?.HttpContext.Response ??
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0114));

            // If the response doesn't contain a WWW-Authenticate header, don't return an empty response.
            if (!response.Headers.ContainsKey(HeaderNames.WWWAuthenticate))
            {
                return default;
            }

            context.Logger.LogInformation(SR.GetResourceString(SR.ID6141), context.Transaction.Response);
            context.HandleRequest();

            return default;
        }
    }
}
