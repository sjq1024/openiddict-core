﻿/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/openiddict/openiddict-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace OpenIddict.Validation;

public static partial class OpenIddictValidationEvents
{
    /// <summary>
    /// Represents an abstract base class used for certain event contexts.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public abstract class BaseContext
    {
        /// <summary>
        /// Creates a new instance of the <see cref="BaseContext"/> class.
        /// </summary>
        protected BaseContext(OpenIddictValidationTransaction transaction)
            => Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));

        /// <summary>
        /// Gets the environment associated with the current request being processed.
        /// </summary>
        public OpenIddictValidationTransaction Transaction { get; }

        /// <summary>
        /// Gets or sets the cancellation token that will be
        /// used to determine if the operation was aborted.
        /// </summary>
        /// <remarks>
        /// Note: for security reasons, this property shouldn't be used by event
        /// handlers to abort security-sensitive operations. As such, it is
        /// recommended to use this property only for user-dependent operations.
        /// </remarks>
        public CancellationToken CancellationToken
        {
            get => Transaction.CancellationToken;
            set => Transaction.CancellationToken = value;
        }

        /// <summary>
        /// Gets or sets the endpoint type that handled the request, if applicable.
        /// </summary>
        public OpenIddictValidationEndpointType EndpointType
        {
            get => Transaction.EndpointType;
            set => Transaction.EndpointType = value;
        }

        /// <summary>
        /// Gets or sets the request <see cref="Uri"/> of the current transaction, if available.
        /// </summary>
        public Uri? RequestUri
        {
            get => Transaction.RequestUri;
            set => Transaction.RequestUri = value;
        }

        /// <summary>
        /// Gets or sets the base <see cref="Uri"/> of the host, if available.
        /// </summary>
        public Uri? BaseUri
        {
            get => Transaction.BaseUri;
            set => Transaction.BaseUri = value;
        }

        /// <summary>
        /// Gets or sets the server configuration used for the current request.
        /// </summary>
        public OpenIddictConfiguration Configuration
        {
            get => Transaction.Configuration;
            set => Transaction.Configuration = value;
        }

        /// <summary>
        /// Gets the logger responsible for logging processed operations.
        /// </summary>
        public ILogger Logger => Transaction.Logger;

        /// <summary>
        /// Gets the OpenIddict validation options.
        /// </summary>
        public OpenIddictValidationOptions Options => Transaction.Options;
    }

    /// <summary>
    /// Represents an abstract base class used for certain event contexts.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public abstract class BaseRequestContext : BaseContext
    {
        /// <summary>
        /// Creates a new instance of the <see cref="BaseRequestContext"/> class.
        /// </summary>
        protected BaseRequestContext(OpenIddictValidationTransaction transaction)
            : base(transaction)
        {
        }

        /// <summary>
        /// Gets a boolean indicating whether the request was fully handled.
        /// </summary>
        public bool IsRequestHandled { get; private set; }

        /// <summary>
        /// Gets a boolean indicating whether the request processing was skipped.
        /// </summary>
        public bool IsRequestSkipped { get; private set; }

        /// <summary>
        /// Marks the request as fully handled. Once declared handled,
        /// a request shouldn't be processed further by the underlying host.
        /// </summary>
        public void HandleRequest() => IsRequestHandled = true;

        /// <summary>
        /// Marks the request as skipped. Once declared skipped, a request
        /// shouldn't be processed further by OpenIddict but should be allowed
        /// to go through the next components in the processing pipeline
        /// (if this pattern is supported by the underlying host).
        /// </summary>
        public void SkipRequest() => IsRequestSkipped = true;
    }

    /// <summary>
    /// Represents an abstract base class used for certain event contexts.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public abstract class BaseExternalContext : BaseValidatingContext
    {
        /// <summary>
        /// Creates a new instance of the <see cref="BaseRequestContext"/> class.
        /// </summary>
        protected BaseExternalContext(OpenIddictValidationTransaction transaction)
            : base(transaction)
        {
        }

        /// <summary>
        /// Gets or sets the URI of the external endpoint to communicate with.
        /// </summary>
        public Uri? RemoteUri { get; set; }
    }

    /// <summary>
    /// Represents an abstract base class used for certain event contexts.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public abstract class BaseValidatingContext : BaseRequestContext
    {
        /// <summary>
        /// Creates a new instance of the <see cref="BaseValidatingContext"/> class.
        /// </summary>
        protected BaseValidatingContext(OpenIddictValidationTransaction transaction)
            : base(transaction)
        {
        }

        /// <summary>
        /// Gets a boolean indicating whether the request will be rejected.
        /// </summary>
        public bool IsRejected { get; protected set; }

        /// <summary>
        /// Gets or sets the "error" parameter returned to the client application.
        /// </summary>
        public string? Error { get; private set; }

        /// <summary>
        /// Gets or sets the "error_description" parameter returned to the client application.
        /// </summary>
        public string? ErrorDescription { get; private set; }

        /// <summary>
        /// Gets or sets the "error_uri" parameter returned to the client application.
        /// </summary>
        public string? ErrorUri { get; private set; }

        /// <summary>
        /// Rejects the request.
        /// </summary>
        /// <param name="error">The "error" parameter returned to the client application.</param>
        /// <param name="description">The "error_description" parameter returned to the client application.</param>
        /// <param name="uri">The "error_uri" parameter returned to the client application.</param>
        public virtual void Reject(string? error = null, string? description = null, string? uri = null)
        {
            Error = error;
            ErrorDescription = description;
            ErrorUri = uri;

            IsRejected = true;
        }
    }

    /// <summary>
    /// Represents an event called when processing an incoming request.
    /// </summary>
    public sealed class ProcessRequestContext : BaseValidatingContext
    {
        /// <summary>
        /// Creates a new instance of the <see cref="ProcessRequestContext"/> class.
        /// </summary>
        public ProcessRequestContext(OpenIddictValidationTransaction transaction)
            : base(transaction)
        {
        }
    }

    /// <summary>
    /// Represents an event called when processing an errored response.
    /// </summary>
    public sealed class ProcessErrorContext : BaseRequestContext
    {
        /// <summary>
        /// Creates a new instance of the <see cref="ProcessErrorContext"/> class.
        /// </summary>
        public ProcessErrorContext(OpenIddictValidationTransaction transaction)
            : base(transaction)
        {
        }

        /// <summary>
        /// Gets or sets the request, or <see langword="null"/> if it couldn't be extracted.
        /// </summary>
        public OpenIddictRequest? Request
        {
            get => Transaction.Request;
            set => Transaction.Request = value;
        }

        /// <summary>
        /// Gets or sets the response.
        /// </summary>
        public OpenIddictResponse Response
        {
            get => Transaction.Response!;
            set => Transaction.Response = value;
        }

        /// <summary>
        /// Gets or sets the error returned to the caller.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Gets or sets the error description returned to the caller.
        /// </summary>
        public string? ErrorDescription { get; set; }

        /// <summary>
        /// Gets or sets the error URI returned to the caller.
        /// </summary>
        public string? ErrorUri { get; set; }

        /// <summary>
        /// Gets the additional parameters returned to the caller.
        /// </summary>
        public Dictionary<string, OpenIddictParameter> Parameters { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Represents an event called when processing an authentication operation.
    /// </summary>
    public sealed class ProcessAuthenticationContext : BaseValidatingContext
    {
        /// <summary>
        /// Creates a new instance of the <see cref="ProcessAuthenticationContext"/> class.
        /// </summary>
        public ProcessAuthenticationContext(OpenIddictValidationTransaction transaction)
            : base(transaction)
        {
        }

        /// <summary>
        /// Gets or sets the request.
        /// </summary>
        public OpenIddictRequest Request
        {
            get => Transaction.Request!;
            set => Transaction.Request = value;
        }

        /// <summary>
        /// Gets or sets the URI of the introspection endpoint, if applicable.
        /// </summary>
        public Uri? IntrospectionEndpoint { get; set; }

        /// <summary>
        /// Gets or sets a boolean indicating whether an introspection request should be sent.
        /// </summary>
        public bool SendIntrospectionRequest { get; set; }

        /// <summary>
        /// Gets or sets the principal extracted from the access token, if applicable.
        /// </summary>
        public ClaimsPrincipal? AccessTokenPrincipal { get; set; }

        /// <summary>
        /// Gets or sets the access token to validate, if applicable.
        /// </summary>
        public string? AccessToken { get; set; }

        public string? DPoPProof { get; set; }

        /// <summary>
        /// Gets or sets a boolean indicating whether an access
        /// token should be extracted from the current context.
        /// </summary>
        /// <remarks>
        /// Note: overriding the value of this property is generally not recommended.
        /// </remarks>
        public bool ExtractAccessToken { get; set; }

        /// <summary>
        /// Gets or sets a boolean indicating whether an access token
        /// must be resolved for the authentication to be considered valid.
        /// </summary>
        /// <remarks>
        /// Note: overriding the value of this property is generally not recommended.
        /// </remarks>
        public bool RequireAccessToken { get; set; }

        /// <summary>
        /// Gets or sets a boolean indicating whether the access
        /// token extracted from the current context should be validated.
        /// </summary>
        /// <remarks>
        /// Note: overriding the value of this property is generally not recommended.
        /// </remarks>
        public bool ValidateAccessToken { get; set; }

        /// <summary>
        /// Gets or sets a boolean indicating whether an invalid access token
        /// will cause the authentication demand to be rejected or will be ignored.
        /// </summary>
        /// <remarks>
        /// Note: overriding the value of this property is generally not recommended.
        /// </remarks>
        public bool RejectAccessToken { get; set; }

        /// <summary>
        /// Gets or sets the request sent to the introspection endpoint, if applicable.
        /// </summary>
        public OpenIddictRequest? IntrospectionRequest { get; set; }

        /// <summary>
        /// Gets or sets the response returned by the introspection endpoint, if applicable.
        /// </summary>
        public OpenIddictResponse? IntrospectionResponse { get; set; }

        /// <summary>
        /// Gets or sets a boolean indicating whether a client assertion
        /// token should be generated (and optionally included in the request).
        /// </summary>
        /// <remarks>
        /// Note: overriding the value of this property is generally not recommended.
        /// </remarks>
        public bool GenerateClientAssertion { get; set; }

        /// <summary>
        /// Gets or sets a boolean indicating whether the generated client
        /// assertion should be included as part of the request.
        /// </summary>
        /// <remarks>
        /// Note: overriding the value of this property is generally not recommended.
        /// </remarks>
        public bool IncludeClientAssertion { get; set; }

        /// <summary>
        /// Gets or sets the generated client assertion, if applicable.
        /// The client assertion will only be returned if
        /// <see cref="IncludeClientAssertion"/> is set to <see langword="true"/>.
        /// </summary>
        public string? ClientAssertion { get; set; }

        /// <summary>
        /// Gets or sets type of the generated client assertion, if applicable.
        /// The client assertion type will only be returned if
        /// <see cref="IncludeClientAssertion"/> is set to <see langword="true"/>.
        /// </summary>
        public string? ClientAssertionType { get; set; }

        /// <summary>
        /// Gets or sets the principal containing the claims that will be
        /// used to create the client assertion, if applicable.
        /// </summary>
        public ClaimsPrincipal? ClientAssertionPrincipal { get; set; }
    }

    /// <summary>
    /// Represents an event called when processing a challenge operation.
    /// </summary>
    public sealed class ProcessChallengeContext : BaseValidatingContext
    {
        /// <summary>
        /// Creates a new instance of the <see cref="ProcessChallengeContext"/> class.
        /// </summary>
        public ProcessChallengeContext(OpenIddictValidationTransaction transaction)
            : base(transaction)
        {
        }

        /// <summary>
        /// Gets or sets the request.
        /// </summary>
        public OpenIddictRequest Request
        {
            get => Transaction.Request!;
            set => Transaction.Request = value;
        }

        /// <summary>
        /// Gets or sets the response.
        /// </summary>
        public OpenIddictResponse Response
        {
            get => Transaction.Response!;
            set => Transaction.Response = value;
        }

        /// <summary>
        /// Gets the user-defined authentication properties, if available.
        /// </summary>
        public Dictionary<string, string?> Properties { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets the additional parameters returned to the caller.
        /// </summary>
        public Dictionary<string, OpenIddictParameter> Parameters { get; } = new(StringComparer.Ordinal);
    }
}
