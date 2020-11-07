using System;

namespace WebAPi.CacheOutput.Core.Time
{
    public class CacheTime
    {
        // client cache length in seconds
        public TimeSpan ClientTimeSpan { get; set; }

        public TimeSpan? SharedTimeSpan { get; set; }

        public DateTimeOffset AbsoluteExpiration { get; set; }
    }
}
