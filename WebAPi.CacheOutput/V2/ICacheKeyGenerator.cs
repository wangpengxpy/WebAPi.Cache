using System.Net.Http.Headers;
using System.Web.Http.Controllers;

namespace WebAPi.CacheOutput.V2
{
    public interface ICacheKeyGenerator
    {
        string MakeCacheKey(HttpActionContext context, MediaTypeHeaderValue mediaType, bool excludeQueryString = false);
    }
}
