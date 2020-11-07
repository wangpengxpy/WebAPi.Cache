using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Filters;
using WebAPi.CacheOutput.Core;

namespace WebAPi.CacheOutput.V2
{
    public abstract class BaseCacheAttribute : ActionFilterAttribute
    {
        // cache repository
        protected IApiOutputCache WebApiCache;

        protected virtual void EnsureCache(HttpConfiguration config, HttpRequestMessage req)
        {
            WebApiCache = config.CacheOutputConfiguration().GetCacheOutputProvider(req);
        }
    }
}
