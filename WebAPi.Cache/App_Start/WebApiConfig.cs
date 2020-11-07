using System.Web.Http;
using WebAPi.CacheOutput.Core;
using WebAPi.CacheOutput.V2;

namespace WebAPi.Cache
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Web API 配置和服务
            GlobalConfiguration.Configuration.Formatters.XmlFormatter.SupportedMediaTypes.Clear();

            var cacheConfig = config.CacheOutputConfiguration();
            cacheConfig.RegisterCacheOutputProvider(() => new MemoryCacheDefault());

            // Web API 路由
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );
        }
    }
}
