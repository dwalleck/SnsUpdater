using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using Unity;
using SnsUpdater.API.Infrastructure.Telemetry;

namespace SnsUpdater.API
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            // Initialize OpenTelemetry
            TelemetryConfiguration.Initialize();

            // Initialize Unity first
            UnityConfig.RegisterComponents();
            
            // Then activate Unity for MVC
            UnityMvcActivator.Start();

            AreaRegistration.RegisterAllAreas();
            GlobalConfiguration.Configure(WebApiConfig.Register);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            // Start background service
            StartBackgroundServices();
        }

        protected void Application_End()
        {
            // Shutdown OpenTelemetry
            TelemetryConfiguration.Shutdown();
            
            // Dispose Unity container
            UnityMvcActivator.Shutdown();
        }

        private void StartBackgroundServices()
        {
            var backgroundService = UnityConfig.Container.Resolve<Infrastructure.BackgroundServices.SnsBackgroundService>();
            backgroundService.Start();
        }
    }
}
