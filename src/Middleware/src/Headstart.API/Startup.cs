using Avalara.AvaTax.RestClient;
using Flurl.Http;
using Flurl.Http.Configuration;
using Headstart.API.Commands;
using Headstart.API.Commands.Crud;
using Headstart.API.Commands.Zoho;
using Headstart.Common;
using Headstart.Common.Helpers;
using Headstart.Common.Models;
using Headstart.Common.Queries;
using Headstart.Common.Repositories;
using Headstart.Common.Services;
using Headstart.Common.Services.CMS;
using Headstart.Common.Services.Zoho;
using LazyCache;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OrderCloud.SDK;
using ordercloud.integrations.smartystreets;
using ordercloud.integrations.easypost;
using ordercloud.integrations.avalara;
using ordercloud.integrations.cardconnect;
using ordercloud.integrations.exchangerates;
using ordercloud.integrations.library;
using SendGrid;
using SmartyStreets;
using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.OpenApi.Models;
using OrderCloud.Catalyst;

namespace Headstart.API
{
    public class Startup
    {
        private readonly AppSettings _settings;

        public Startup(AppSettings settings)
        {
            _settings = settings;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            var cosmosConfig = new CosmosConfig(
                _settings.CosmosSettings.DatabaseName,
                _settings.CosmosSettings.EndpointUri,
                _settings.CosmosSettings.PrimaryKey,
                _settings.CosmosSettings.RequestTimeoutInSeconds
            );
            var cosmosContainers = new List<ContainerInfo>()
            {
                new ContainerInfo()
                {
                    Name = "rmas",
                    PartitionKey = "PartitionKey"
                }
            };

            var avalaraConfig = new AvalaraConfig()
            {
                BaseApiUrl = _settings.AvalaraSettings.BaseApiUrl,
                AccountID = _settings.AvalaraSettings.AccountID,
                LicenseKey = _settings.AvalaraSettings.LicenseKey,
                CompanyCode = _settings.AvalaraSettings.CompanyCode,
                CompanyID = _settings.AvalaraSettings.CompanyID
            };

            var blobStorage = ListPageMetaWithFacets 
            var currencyConfig = new BlobServiceConfig()
            {
                ConnectionString = _settings.BlobSettings.ConnectionString,
                Container = _settings.BlobSettings.ContainerNameExchangeRates
            };
            var middlewareErrorsConfig = new BlobServiceConfig()
            {
                ConnectionString = _settings.BlobSettings.ConnectionString,
                Container = "unhandled-errors-log"
            };

            var flurlClientFactory = new PerBaseUrlFlurlClientFactory();
            var smartyStreetsUsClient = new ClientBuilder(_settings.SmartyStreetSettings.AuthID, _settings.SmartyStreetSettings.AuthToken).BuildUsStreetApiClient();

            services
                .AddLazyCache()
                .ConfigureServices()
                .AddOrderCloudUserAuth<AppSettings>()
                .AddOrderCloudWebhookAuth(opts => opts.HashKey = _settings.OrderCloudSettings.WebhookHashKey)
                .InjectCosmosStore<LogQuery, OrchestrationLog>(cosmosConfig)
                .InjectCosmosStore<ReportTemplateQuery, ReportTemplate>(cosmosConfig)
                .AddCosmosDb(_settings.CosmosSettings.EndpointUri, _settings.CosmosSettings.PrimaryKey, _settings.CosmosSettings.DatabaseName, cosmosContainers)
                .Inject<IPortalService>()
                .Inject<ISmartyStreetsCommand>()
                .Inject<ICheckoutIntegrationCommand>()
                .Inject<IShipmentCommand>()
                .Inject<IOrderCommand>()
                .Inject<IPaymentCommand>()
                .Inject<IOrderSubmitCommand>()
                .Inject<IEnvironmentSeedCommand>()
                .Inject<IHSProductCommand>()
                .Inject<ILineItemCommand>()
                .Inject<IMeProductCommand>()
                .Inject<IHSCatalogCommand>()
                .Inject<ISendgridService>()
                .Inject<IHSSupplierCommand>()
                .Inject<ICreditCardCommand>()
                .Inject<ISupportAlertService>()
                .Inject<IOrderCalcService>()
                .Inject<ISupplierApiClientHelper>()
                .AddSingleton<ICMSClient>(new CMSClient(new CMSClientConfig() { BaseUrl = _settings.CMSSettings.BaseUrl }))
                .AddSingleton<ISendGridClient>(x => new SendGridClient(_settings.SendgridSettings.ApiKey))
                .AddSingleton<IFlurlClientFactory>(x => flurlClientFactory)
                .AddSingleton<DownloadReportCommand>()
                .Inject<IRMARepo>()
                .Inject<IZohoClient>()
                .AddSingleton<IZohoCommand>(z => new ZohoCommand(new ZohoClient(
                    new ZohoClientConfig()
                    {
                        ApiUrl = "https://books.zoho.com/api/v3",
                        AccessToken = _settings.ZohoSettings.AccessToken,
                        ClientId = _settings.ZohoSettings.ClientId,
                        ClientSecret = _settings.ZohoSettings.ClientSecret,
                        OrganizationID = _settings.ZohoSettings.OrgID
                    }, flurlClientFactory),
                    new OrderCloudClient(new OrderCloudClientConfig
                    {
                        ApiUrl = _settings.OrderCloudSettings.ApiUrl,
                        AuthUrl = _settings.OrderCloudSettings.ApiUrl,
                        ClientId = _settings.OrderCloudSettings.MiddlewareClientID,
                        ClientSecret = _settings.OrderCloudSettings.MiddlewareClientSecret,
                        Roles = new[] { ApiRole.FullAccess }
                    })))
                .AddSingleton<IOrderCloudIntegrationsExchangeRatesClient, OrderCloudIntegrationsExchangeRatesClient>()
                .AddSingleton<IExchangeRatesCommand>(provider => new ExchangeRatesCommand(currencyConfig, flurlClientFactory, provider.GetService<IAppCache>()))
                .AddSingleton<IAvalaraCommand>(x => new AvalaraCommand(
                    avalaraConfig,
                    new AvaTaxClient("four51_headstart", "v1", "four51_headstart", new Uri(avalaraConfig.BaseApiUrl)
                   ).WithSecurity(_settings.AvalaraSettings.AccountID, _settings.AvalaraSettings.LicenseKey)))
                .AddSingleton<IEasyPostShippingService>(x => new EasyPostShippingService(new EasyPostConfig() { APIKey = _settings.EasyPostSettings.APIKey }))
                .AddSingleton<ISmartyStreetsService>(x => new SmartyStreetsService(_settings.SmartyStreetSettings, smartyStreetsUsClient))
                .AddSingleton<IOrderCloudIntegrationsCardConnectService>(x => new OrderCloudIntegrationsCardConnectService(_settings.CardConnectSettings, flurlClientFactory))
                .AddSingleton<IOrderCloudClient>(provider => new OrderCloudClient(new OrderCloudClientConfig
                {
                    ApiUrl = _settings.OrderCloudSettings.ApiUrl,
                    AuthUrl = _settings.OrderCloudSettings.ApiUrl,
                    ClientId = _settings.OrderCloudSettings.MiddlewareClientID,
                    ClientSecret = _settings.OrderCloudSettings.MiddlewareClientSecret,
                    Roles = new[]
                    {
                        ApiRole.FullAccess
                    }
                }))
                .AddSwaggerGen(c =>
                {
                    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Headstart API", Version = "v1" });
                    c.CustomSchemaIds(x => x.FullName);
                });
            var serviceProvider = services.BuildServiceProvider();
            services
                .AddApplicationInsightsTelemetry(new ApplicationInsightsServiceOptions
                {
                    EnableAdaptiveSampling = false, // retain all data
                    InstrumentationKey = _settings.ApplicationInsightsSettings.InstrumentationKey
                });


            ServicePointManager.DefaultConnectionLimit = int.MaxValue;
            FlurlHttp.Configure(settings => settings.Timeout = TimeSpan.FromSeconds(_settings.FlurlSettings.TimeoutInSeconds));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            CatalystApplicationBuilder.CreateApplicationBuilder(app, env)
                .UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint($"/swagger", $"API v1");
                    c.RoutePrefix = string.Empty;
                });

            app.MapWhen(context =>
                context.Request.Path.Value.StartsWith("/api"),
                HandleMiddleware
            );

            app.MapWhen(context =>
             (string.Equals(context.Request.Host.Host, _settings.UI.BaseBuyerUrl, StringComparison.InvariantCultureIgnoreCase) ||
             string.Equals(context.Request.Host.Host, $"www.{_settings.UI.BaseBuyerUrl}", StringComparison.InvariantCultureIgnoreCase)),
             HandleBuyerApp
         );

            app.MapWhen(context =>
                (
                string.Equals(context.Request.Host.Host, _settings.UI.BaseAdminUrl, StringComparison.InvariantCultureIgnoreCase) ||
                string.Equals(context.Request.Host.Host, $"www.{ _settings.UI.BaseAdminUrl}", StringComparison.InvariantCultureIgnoreCase)
                ),
                HandleSellerApp
            );

        }
        private void HandleBuyerApp(IApplicationBuilder app)
        {
           
            app.Run(async context =>
            {
                var index = await blob.ReadTextFileBlobAsync("buyerweb", "index.html");

                var cdnUrl = settings.CDNBaseUrl + '/';

                context.Response.ContentType = "text/html; charset=utf-8";

                var writeToIndex = $@"<script>cdnbasepath = '{cdnUrl}';</script>";

                var modifiedIndex = index
                    .Replace("https://mycdn/path/", cdnUrl + "buyerweb/")
                    .Replace("<!--APP_CONFIG_PLACEHOLDER-->", writeToIndex);
      

                if (!settings.ASPNETCORE_ENVIRONMENT.Contains("production"))
                {
                    // index production website only
                    modifiedIndex =
                        modifiedIndex.Replace("<head>", "<head><meta name=\"robots\" content=\"noindex\">");
                }

                if (settings.ASPNETCORE_ENVIRONMENT.Contains("production"))
                {
                    if (settings.Brand == "mgr")
                    {
                        // HEAD
                        string googleTagManager = @"<!-- Google Tag Manager -->
                            <script>(function(w,d,s,l,i){w[l]=w[l]||[];w[l].push({'gtm.start':
                            new Date().getTime(),event:'gtm.js'});var f=d.getElementsByTagName(s)[0],
                            j=d.createElement(s),dl=l!='dataLayer'?'&l='+l:'';j.async=true;j.src=
                            'https://www.googletagmanager.com/gtm.js?id='+i+dl;f.parentNode.insertBefore(j,f);
                            })(window,document,'script','dataLayer','GTM-KDSC2TZ');
                            </script>
                            <!-- End Google Tag Manager -->";

                        string googleSiteVerification = @"<meta name='google-site-verification' content='tSWNNVPL529Voqm7dKkaC-EFKgag8BAkXfmYsbi_6jw'/>";

                        string bingMeta = @"<meta name=\'msvalidate.01\' content=\'FD038863559CF2E09CC79C5947C94AFA\' />";

                        string facebookPixel = @"<!-- Facebook Pixel Code -->
                            <script>
                            !function(f,b,e,v,n,t,s)
                            {if(f.fbq)return;n=f.fbq=function(){n.callMethod?
                            n.callMethod.apply(n,arguments):n.queue.push(arguments)};
                            if(!f._fbq)f._fbq=n;n.push=n;n.loaded=!0;n.version='2.0';
                            n.queue=[];t=b.createElement(e);t.async=!0;
                            t.src=v;s=b.getElementsByTagName(e)[0];
                            s.parentNode.insertBefore(t,s)}(window, document,'script',
                            'https://connect.facebook.net/en_US/fbevents.js');
                            fbq('init', '421394501793275');
                            fbq('track', 'PageView');
                            </script>
                            <noscript><img height='1' width='1' style='display: none'
                            src = 'https://www.facebook.com/tr?id=421394501793275&ev=PageView&noscript=1'/>
                            </noscript>";

                        string brightEdgeScript = @"<script src='//cdn.bc0a.com/autopilot/f00000000212186/autopilot_sdk.js'></script>";

                        List<string> headScripts = new List<string>();
                        headScripts.Add(googleTagManager);
                        headScripts.Add(googleSiteVerification);
                        headScripts.Add(bingMeta);
                        headScripts.Add(facebookPixel);
                        headScripts.Add(brightEdgeScript);
                        headScripts.Add(GetTikTokScript());

                        modifiedIndex = AddScriptsToIndex("head", headScripts, modifiedIndex);

                        // BODY
                        string googleTagManagerIFrame = @"<!-- Google Tag Manager (noscript) -->
                            <noscript><iframe src='https://www.googletagmanager.com/ns.html?id=GTM-KDSC2TZ' height = '0' width = '0' style = 'display:none;visibility:hidden'></iframe></noscript>";

                        List<string> bodyScripts = new List<string>();
                        bodyScripts.Add(googleTagManagerIFrame);

                        modifiedIndex = AddScriptsToIndex("body", bodyScripts, modifiedIndex);
                    }
                    if (settings.Brand == "pias")
                    {
                        // HEAD
                        string googleSiteVerification = @"<meta name='google-site-verification' content='0fmP-PZhyv7pnrHdhQB60dIPlYIVf6jkPUnFpEQjNVc'/>";

                        string facebookPixel = @"<!-- Facebook Pixel Code -->
                            <script>
                            !function(f,b,e,v,n,t,s)
                            {if(f.fbq)return;n=f.fbq=function(){n.callMethod?
                            n.callMethod.apply(n,arguments):n.queue.push(arguments)};
                            if(!f._fbq)f._fbq=n;n.push=n;n.loaded=!0;n.version='2.0';
                            n.queue=[];t=b.createElement(e);t.async=!0;
                            t.src=v;s=b.getElementsByTagName(e)[0];
                            s.parentNode.insertBefore(t,s)}(window, document,'script',
                            'https://connect.facebook.net/en_US/fbevents.js');
                            fbq('init', '2079878468945820');
                            fbq('track', 'PageView');
                            </script>
                            <noscript><img height='1' width='1' style='display: none'
                            src = 'https://www.facebook.com/tr?id=2079878468945820&ev=PageView&noscript=1'/>
                            </noscript>";

                        string brightEdgeScript = @"<script src='//cdn.bc0a.com/autopilot/f00000000211790/autopilot_sdk.js'></script>";

                        List<string> headScripts = new List<string>();
                        headScripts.Add(googleSiteVerification);
                        headScripts.Add(facebookPixel);
                        headScripts.Add(brightEdgeScript);
                        headScripts.Add(GetTikTokScript());

                        modifiedIndex = AddScriptsToIndex("head", headScripts, modifiedIndex); ;
                    }

                }
                await context.Response.WriteAsync(modifiedIndex);
            });
        }

        private void HandleSellerApp(IApplicationBuilder app)
        {
            var blob = providerService.GetService<BlobService>();

            app.Run(async context =>
            {
                var index = await blob.ReadTextFileBlobAsync("sellerweb", "index.html");

                //                {
                //    "hostedApp": true,
                //    "sellerID": "HEADSTARTDEMO_TEST",
                //    "sellerName": "HEADSTARTDEMO Admin",
                //    "clientID": "8AD1E585-DB17-4814-897F-83E4AC9B3CB4",
                //    "middlewareUrl": "https://headstartdemo-middleware-test.azurewebsites.net",
                //    "cmsUrl": "https://ordercloud-cms-test.azurewebsites.net",
                //    "appname": "Headstart Demo Admin",
                //    "translateBlobUrl": "https://readmetest.blob.core.windows.net/ngx-translate/i18n/",
                //    "blobStorageUrl": "https://readmetest.blob.core.windows.net",
                //    "orderCloudApiUrl": "https://sandboxapi.ordercloud.io",
                //    "orderCloudApiVersion": "v1",
                //    "buyerConfigs": {
                //                        "Default Buyer": {
                //                            "clientID": "2F33BE12-D914-419C-B3D0-41AEFB72BE93",
                //            "buyerUrl": "https://headstartdemo-buyer-ui-test.azurewebsites.net/"
                //                        }
                //                    },
                //    "superProductFieldsToMonitor": []
                //}
                context.Response.ContentType = "text/html";
                string[] appConfigParts =
                {
                        $"window.hostedApp='{_settings.BlobSettings.HostUrl}'",
                        $"window.sellerID='{_settings.SellerClientID}'",
                        $"window.sellerName = '{_settings.Brand}'",
                        $"window.clientID = '{_settings.ocApiUrl}'",
                        $"window.middlewareUrl='{_settings.ImagePath}'",
                        $"window.cmsUrl='{_settings.BuyerClientID}';",
                        $"window.appname='{_settings.BuyerID}'",
                        $"window.translateBlobUrl='{_settings.InstrumentationKey}'",
                        $"window.blobStorageUrl='{_settings.HeadstartApiUrl}'",
                        $"window.orderCloudApiUrl='{_settings.BuyerDomain}'",
                        _settings.Brand.ToLower() == "pias" ? $"window.splitInventorySuppliers='{_settings.SyncSuppliersSplitInv}'" : "",
                        _settings.ASPNETCORE_ENVIRONMENT.Contains("production") ? $"window.treefortApiUrl = '{_settings.TreefortApiUrl}'" : "",
                };

                var writeToIndex = "<script>";
                writeToIndex += string.Join(";\n", appConfigParts);
                writeToIndex += "</script>";

                var modifiedIndex = index
                    .Replace("https://mycdn/path/", cdnUrl + "sellerweb/")
                    .Replace("<!--APP_CONFIG_PLACEHOLDER-->", writeToIndex)
                    .Replace("<head>", "<head><meta name=\"robots\" content=\"noindex\">");

                await context.Response.WriteAsync(modifiedIndex);
            });
        }

        private void HandleMiddleware(IApplicationBuilder app)
        {
            app.UseMvc();
        }
    }
}
