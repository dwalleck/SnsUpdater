﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using SnsUpdater.API.Infrastructure.Filters;

namespace SnsUpdater.API
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Web API configuration and services
            config.Filters.Add(new CorrelationIdActionFilter());

            // Web API routes
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );
        }
    }
}
