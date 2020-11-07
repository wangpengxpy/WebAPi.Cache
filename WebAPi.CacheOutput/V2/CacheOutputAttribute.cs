﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using WebAPi.CacheOutput.Core;
using WebAPi.CacheOutput.Core.Time;

namespace WebAPi.CacheOutput.V2
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class CacheOutputAttribute : ActionFilterAttribute
    {
        private const string CurrentRequestMediaType = "CacheOutput:CurrentRequestMediaType";
        protected static readonly MediaTypeHeaderValue DefaultMediaType = new MediaTypeHeaderValue("application/json") { CharSet = Encoding.UTF8.HeaderName };

        /// <summary>
        /// Cache enabled only for requests when Thread.CurrentPrincipal is not set
        /// </summary>
        public bool AnonymousOnly { get; set; }

        /// <summary>
        /// Corresponds to MustRevalidate HTTP header - indicates whether the origin server requires revalidation of a cache entry on any subsequent use when the cache entry becomes stale
        /// </summary>
        public bool MustRevalidate { get; set; }

        /// <summary>
        /// Do not vary cache by querystring values
        /// </summary>
        public bool ExcludeQueryStringFromCacheKey { get; set; }

        /// <summary>
        /// How long response should be cached on the server side (in seconds)
        /// </summary>
        public int ServerTimeSpan { get; set; }

        /// <summary>
        /// Corresponds to CacheControl MaxAge HTTP header (in seconds)
        /// </summary>
        public int ClientTimeSpan { get; set; }


        private int? _sharedTimeSpan = null;

        /// <summary>
        /// Corresponds to CacheControl Shared MaxAge HTTP header (in seconds)
        /// </summary>
        public int SharedTimeSpan
        {
            get // required for property visibility
            {
                if (!_sharedTimeSpan.HasValue)
                    throw new Exception("should not be called without value set");
                return _sharedTimeSpan.Value;
            }
            set { _sharedTimeSpan = value; }
        }

        /// <summary>
        /// Corresponds to CacheControl NoCache HTTP header
        /// </summary>
        public bool NoCache { get; set; }

        /// <summary>
        /// Corresponds to CacheControl Private HTTP header. Response can be cached by browser but not by intermediary cache
        /// </summary>
        public bool Private { get; set; }

        /// <summary>
        /// Class used to generate caching keys
        /// </summary>
        public Type CacheKeyGenerator { get; set; }

        /// <summary>
        /// Comma seperated list of HTTP headers to cache
        /// </summary>
        public string IncludeCustomHeaders { get; set; }

        /// <summary>
        /// If set to something else than an empty string, this value will always be used for the Content-Type header, regardless of content negotiation.
        /// </summary>
        public string MediaType { get; set; }

        // cache repository
        private IApiOutputCache _webApiCache;

        protected virtual void EnsureCache(HttpConfiguration config, HttpRequestMessage req)
        {
            _webApiCache = config.CacheOutputConfiguration().GetCacheOutputProvider(req);
        }

        internal IModelQuery<DateTime, CacheTime> CacheTimeQuery;

        protected virtual bool IsCachingAllowed(HttpActionContext actionContext, bool anonymousOnly)
        {
            if (anonymousOnly && Thread.CurrentPrincipal.Identity.IsAuthenticated)
            {
                return false;
            }

            if (actionContext.ActionDescriptor.GetCustomAttributes<IgnoreCacheOutputAttribute>().Any())
            {
                return false;
            }

            return actionContext.Request.Method == HttpMethod.Get;
        }

        protected virtual void EnsureCacheTimeQuery()
        {
            if (CacheTimeQuery == null) ResetCacheTimeQuery();
        }

        protected void ResetCacheTimeQuery()
        {
            CacheTimeQuery = new ShortTime(ServerTimeSpan, ClientTimeSpan, _sharedTimeSpan);
        }

        protected virtual MediaTypeHeaderValue GetExpectedMediaType(HttpConfiguration config, HttpActionContext actionContext)
        {
            if (!string.IsNullOrEmpty(MediaType))
            {
                return new MediaTypeHeaderValue(MediaType);
            }

            MediaTypeHeaderValue responseMediaType = null;

            var returnType = actionContext.ActionDescriptor.ReturnType;

            if (config.Services.GetService(typeof(IContentNegotiator)) is IContentNegotiator negotiator && returnType != typeof(HttpResponseMessage) && (returnType != typeof(IHttpActionResult) || typeof(IHttpActionResult).IsAssignableFrom(returnType)))
            {
                var negotiatedResult = negotiator.Negotiate(returnType, actionContext.Request, config.Formatters);

                if (negotiatedResult == null)
                {
                    return DefaultMediaType;
                }

                responseMediaType = negotiatedResult.MediaType;
                if (string.IsNullOrWhiteSpace(responseMediaType.CharSet))
                {
                    responseMediaType.CharSet = Encoding.UTF8.HeaderName;
                }
            }
            else
            {
                if (actionContext.Request.Headers.Accept != null)
                {
                    responseMediaType = actionContext.Request.Headers.Accept.FirstOrDefault();
                    if (responseMediaType == null || !config.Formatters.Any(x => x.SupportedMediaTypes.Any(value => value.MediaType == responseMediaType.MediaType)))
                    {
                        return DefaultMediaType;
                    }
                }
            }

            return responseMediaType;
        }

        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            if (actionContext == null) throw new ArgumentNullException("actionContext");

            if (!IsCachingAllowed(actionContext, AnonymousOnly)) return;

            var config = actionContext.Request.GetConfiguration();

            EnsureCacheTimeQuery();
            EnsureCache(config, actionContext.Request);

            var cacheKeyGenerator = config.CacheOutputConfiguration().GetCacheKeyGenerator(actionContext.Request, CacheKeyGenerator);

            var responseMediaType = GetExpectedMediaType(config, actionContext);
            actionContext.Request.Properties[CurrentRequestMediaType] = responseMediaType;
            var cachekey = cacheKeyGenerator.MakeCacheKey(actionContext, responseMediaType, ExcludeQueryStringFromCacheKey);

            if (!_webApiCache.Contains(cachekey)) return;

            var responseHeaders = _webApiCache.Get<Dictionary<string, List<string>>>(cachekey + Constants.CustomHeaders);
            var responseContentHeaders = _webApiCache.Get<Dictionary<string, List<string>>>(cachekey + Constants.CustomContentHeaders);

            if (actionContext.Request.Headers.IfNoneMatch != null)
            {
                var etag = _webApiCache.Get<string>(cachekey + Constants.EtagKey);
                if (etag != null && actionContext.Request.Headers.IfNoneMatch.Any(x => x.Tag == etag))
                {
                    var time = CacheTimeQuery.Execute(DateTime.Now);
                    var quickResponse = actionContext.Request.CreateResponse(HttpStatusCode.NotModified);
                    if (responseHeaders != null) AddCustomCachedHeaders(quickResponse, responseHeaders, responseContentHeaders);

                    SetEtag(quickResponse, etag);
                    ApplyCacheHeaders(quickResponse, time);
                    actionContext.Response = quickResponse;
                    return;
                }
            }

            var val = _webApiCache.Get<byte[]>(cachekey);
            if (val == null) return;

            var contenttype = _webApiCache.Get<MediaTypeHeaderValue>(cachekey + Constants.ContentTypeKey) ?? responseMediaType;
            var contentGeneration = _webApiCache.Get<string>(cachekey + Constants.GenerationTimestampKey);

            DateTimeOffset? contentGenerationTimestamp = null;
            if (contentGeneration != null && DateTimeOffset.TryParse(contentGeneration, out DateTimeOffset parsedContentGenerationTimestamp))
            {
                contentGenerationTimestamp = parsedContentGenerationTimestamp;
            };

            actionContext.Response = actionContext.Request.CreateResponse();
            actionContext.Response.Content = new ByteArrayContent(val);

            actionContext.Response.Content.Headers.ContentType = contenttype;
            var responseEtag = _webApiCache.Get<string>(cachekey + Constants.EtagKey);
            if (responseEtag != null) SetEtag(actionContext.Response, responseEtag);

            if (responseHeaders != null) AddCustomCachedHeaders(actionContext.Response, responseHeaders, responseContentHeaders);

            var cacheTime = CacheTimeQuery.Execute(DateTime.Now);
            ApplyCacheHeaders(actionContext.Response, cacheTime, contentGenerationTimestamp);
        }

        public override async Task OnActionExecutedAsync(HttpActionExecutedContext actionExecutedContext, CancellationToken cancellationToken)
        {
            if (actionExecutedContext.ActionContext.Response == null || !actionExecutedContext.ActionContext.Response.IsSuccessStatusCode) return;

            if (!IsCachingAllowed(actionExecutedContext.ActionContext, AnonymousOnly)) return;

            var actionExecutionTimestamp = DateTimeOffset.Now;
            var cacheTime = CacheTimeQuery.Execute(actionExecutionTimestamp.DateTime);
            if (cacheTime.AbsoluteExpiration > actionExecutionTimestamp)
            {
                var httpConfig = actionExecutedContext.Request.GetConfiguration();
                var config = httpConfig.CacheOutputConfiguration();
                var cacheKeyGenerator = config.GetCacheKeyGenerator(actionExecutedContext.Request, CacheKeyGenerator);

                var responseMediaType = actionExecutedContext.Request.Properties[CurrentRequestMediaType] as MediaTypeHeaderValue ?? GetExpectedMediaType(httpConfig, actionExecutedContext.ActionContext);
                var cachekey = cacheKeyGenerator.MakeCacheKey(actionExecutedContext.ActionContext, responseMediaType, ExcludeQueryStringFromCacheKey);

                if (!string.IsNullOrWhiteSpace(cachekey) && !(_webApiCache.Contains(cachekey)))
                {
                    SetEtag(actionExecutedContext.Response, CreateEtag(actionExecutedContext, cachekey, cacheTime));

                    var responseContent = actionExecutedContext.Response.Content;

                    if (responseContent != null)
                    {
                        var baseKey = config.MakeBaseCachekey(actionExecutedContext.ActionContext.ControllerContext.ControllerDescriptor.ControllerType.FullName, actionExecutedContext.ActionContext.ActionDescriptor.ActionName);
                        var contentType = responseContent.Headers.ContentType;
                        string etag = actionExecutedContext.Response.Headers.ETag.Tag;
                        //ConfigureAwait false to avoid deadlocks
                        var content = await responseContent.ReadAsByteArrayAsync().ConfigureAwait(false);

                        responseContent.Headers.Remove("Content-Length");

                        _webApiCache.Add(baseKey, string.Empty, cacheTime.AbsoluteExpiration);
                        _webApiCache.Add(cachekey, content, cacheTime.AbsoluteExpiration, baseKey);


                        _webApiCache.Add(cachekey + Constants.ContentTypeKey,
                                        contentType,
                                        cacheTime.AbsoluteExpiration, baseKey);


                        _webApiCache.Add(cachekey + Constants.EtagKey,
                                        etag,
                                        cacheTime.AbsoluteExpiration, baseKey);

                        _webApiCache.Add(cachekey + Constants.GenerationTimestampKey,
                                        actionExecutionTimestamp.ToString(),
                                        cacheTime.AbsoluteExpiration, baseKey);

                        if (!string.IsNullOrEmpty(IncludeCustomHeaders))
                        {
                            // convert to dictionary of lists to ensure thread safety if implementation of IEnumerable is changed
                            var headers = actionExecutedContext.Response.Headers.Where(h => IncludeCustomHeaders.Contains(h.Key))
                                .ToDictionary(x => x.Key, x => x.Value.ToList());

                            var contentHeaders = actionExecutedContext.Response.Content.Headers.Where(h => IncludeCustomHeaders.Contains(h.Key))
                                .ToDictionary(x => x.Key, x => x.Value.ToList());

                            _webApiCache.Add(cachekey + Constants.CustomHeaders,
                                            headers,
                                            cacheTime.AbsoluteExpiration, baseKey);

                            _webApiCache.Add(cachekey + Constants.CustomContentHeaders,
                                            contentHeaders,
                                            cacheTime.AbsoluteExpiration, baseKey);
                        }
                    }
                }
            }

            ApplyCacheHeaders(actionExecutedContext.ActionContext.Response, cacheTime, actionExecutionTimestamp);
        }

        protected virtual void ApplyCacheHeaders(HttpResponseMessage response, CacheTime cacheTime, DateTimeOffset? contentGenerationTimestamp = null)
        {
            if (cacheTime.ClientTimeSpan > TimeSpan.Zero || MustRevalidate || Private)
            {
                var cachecontrol = new CacheControlHeaderValue
                {
                    MaxAge = cacheTime.ClientTimeSpan,
                    SharedMaxAge = cacheTime.SharedTimeSpan,
                    MustRevalidate = MustRevalidate,
                    Private = Private
                };

                response.Headers.CacheControl = cachecontrol;
            }
            else if (NoCache)
            {
                response.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
                response.Headers.Add("Pragma", "no-cache");
            }
            if ((response.Content != null) && contentGenerationTimestamp.HasValue)
            {
                response.Content.Headers.LastModified = contentGenerationTimestamp.Value;
            }
        }

        protected virtual void AddCustomCachedHeaders(HttpResponseMessage response, Dictionary<string, List<string>> headers, Dictionary<string, List<string>> contentHeaders)
        {
            foreach (var headerKey in headers.Keys)
            {
                foreach (var headerValue in headers[headerKey])
                {
                    response.Headers.Add(headerKey, headerValue);
                }
            }

            foreach (var headerKey in contentHeaders.Keys)
            {
                foreach (var headerValue in contentHeaders[headerKey])
                {
                    response.Content.Headers.Add(headerKey, headerValue);
                }
            }
        }

        protected virtual string CreateEtag(HttpActionExecutedContext actionExecutedContext, string cachekey, CacheTime cacheTime)
        {
            return Guid.NewGuid().ToString();
        }

        private static void SetEtag(HttpResponseMessage message, string etag)
        {
            if (etag != null)
            {
                var eTag = new EntityTagHeaderValue(@"""" + etag.Replace("\"", string.Empty) + @"""");
                message.Headers.ETag = eTag;
            }
        }
    }
}
