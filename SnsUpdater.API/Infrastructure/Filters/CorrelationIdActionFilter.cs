using System;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace SnsUpdater.API.Infrastructure.Filters
{
    public class CorrelationIdActionFilter : ActionFilterAttribute
    {
        public const string CorrelationIdHeader = "X-Correlation-Id";
        public const string CorrelationIdKey = "CorrelationId";

        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            var correlationId = Guid.NewGuid().ToString();

            // Check if correlation ID is provided in request headers
            if (actionContext.Request.Headers.Contains(CorrelationIdHeader))
            {
                var headerValue = actionContext.Request.Headers.GetValues(CorrelationIdHeader);
                if (headerValue != null)
                {
                    correlationId = string.Join(",", headerValue);
                }
            }

            // Store in request properties for use throughout the request
            actionContext.Request.Properties[CorrelationIdKey] = correlationId;

            base.OnActionExecuting(actionContext);
        }

        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
        {
            if (actionExecutedContext.Response != null && 
                actionExecutedContext.Request.Properties.ContainsKey(CorrelationIdKey))
            {
                var correlationId = actionExecutedContext.Request.Properties[CorrelationIdKey].ToString();
                actionExecutedContext.Response.Headers.Add(CorrelationIdHeader, correlationId);
            }

            base.OnActionExecuted(actionExecutedContext);
        }
    }
}