using System;
using System.Text.Json.Serialization;

namespace WebviewLogger
{
    /// <summary>
    /// Represents a rich log record with detailed context information
    /// </summary>
    public class LogRecord
    {
        public string Id { get; set; }
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; }
        public string Source { get; set; }
        public string Category { get; set; }
        public string Location { get; set; }
        public int ThreadId { get; set; }
        public object Data { get; set; }
    }
}
