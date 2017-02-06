﻿using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Ocelot.Infrastructure.RequestData;
using Ocelot.LoadBalancer.LoadBalancers;
using Ocelot.Logging;
using Ocelot.Middleware;
using Ocelot.QueryStrings.Middleware;
using Ocelot.ServiceDiscovery;

namespace Ocelot.LoadBalancer.Middleware
{
    public class LoadBalancingMiddleware : OcelotMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IOcelotLogger _logger;
        private readonly ILoadBalancerHouse _loadBalancerHouse;

        public LoadBalancingMiddleware(RequestDelegate next,
            IOcelotLoggerFactory loggerFactory,
            IRequestScopedDataRepository requestScopedDataRepository,
            ILoadBalancerHouse loadBalancerHouse) 
            : base(requestScopedDataRepository)
        {
            _next = next;
            _logger = loggerFactory.CreateLogger<QueryStringBuilderMiddleware>();
            _loadBalancerHouse = loadBalancerHouse;
        }

        public async Task Invoke(HttpContext context)
        {
            _logger.LogDebug("started calling load balancing middleware");

            var getLoadBalancer = _loadBalancerHouse.Get(DownstreamRoute.ReRoute.LoadBalancerKey);
            if(getLoadBalancer.IsError)
            {
                SetPipelineError(getLoadBalancer.Errors);
                return;
            }

            var hostAndPort = await getLoadBalancer.Data.Lease();
            if(hostAndPort.IsError)
            { 
                SetPipelineError(hostAndPort.Errors);
                return;
            }

            SetHostAndPortForThisRequest(hostAndPort.Data);

            _logger.LogDebug("calling next middleware");

            try
            {
                await _next.Invoke(context);

                getLoadBalancer.Data.Release(hostAndPort.Data);
            }
            catch (Exception)
            {
                getLoadBalancer.Data.Release(hostAndPort.Data);
                 _logger.LogDebug("error calling next middleware, exception will be thrown to global handler");
                throw;
            }

            _logger.LogDebug("succesfully called next middleware");
        }
    }
}
