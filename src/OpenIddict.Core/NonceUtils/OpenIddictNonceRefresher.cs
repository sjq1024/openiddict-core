using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions.Managers;
using Timer = System.Timers.Timer;

namespace OpenIddict.Core.NonceUtils;

public class OpenIddictNonceRefresher : IHostedService
{
    private readonly IOpenIddictNonceManager openIddictNonceManager;

    private readonly ILogger logger;

    private readonly Timer nonceRefreshTimer;

    public OpenIddictNonceRefresher(ILogger<OpenIddictNonceRefresher> logger, IOpenIddictNonceManager openIddictNonceManager)
    {
        this.logger = logger;
        this.openIddictNonceManager = openIddictNonceManager;
        this.nonceRefreshTimer = new Timer(TimeSpan.FromMinutes(2).TotalMilliseconds);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        this.logger.LogInformation($"Starting Subscription refresh service");
        this.nonceRefreshTimer.Elapsed += this.DoNonceRefreshTask;
        this.nonceRefreshTimer.Enabled = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    private void DoNonceRefreshTask(object? source, ElapsedEventArgs e)
    {
        this.logger.LogInformation($"Refreshing Nonce");
        this.openIddictNonceManager.GenerateAndAddNonce();
        this.openIddictNonceManager.CleanExpiredNonces();
    }
}
