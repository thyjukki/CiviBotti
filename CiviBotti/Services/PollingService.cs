namespace CiviBotti.Services;

using System;
using Abstract;
using Microsoft.Extensions.Logging;

public class PollingService(IServiceProvider serviceProvider, ILogger<PollingService> logger)
    : PollingServiceBase<ReceiverService>(serviceProvider, logger);