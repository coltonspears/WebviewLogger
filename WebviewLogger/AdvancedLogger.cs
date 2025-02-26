using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace WebviewLogger
{
    #region Logger Core

    /// <summary>
    /// Advanced global logger with rich object support and filtering capabilities
    /// </summary>
    public static class AdvancedLogger
    {
        // Singleton instance of the HTTP log viewer
        private static LogViewerServer _logServer;

        // Fallback log storage in case of viewer failure
        private static readonly ConcurrentQueue<LogRecord> _backupLogs = new ConcurrentQueue<LogRecord>();

        // Maximum number of backup logs to store
        private static readonly int MAX_BACKUP_LOGS = 5000;

        // Initialize flag
        private static bool _initialized = false;
        private static readonly object _initLock = new object();

        // Flag to track if the log viewer is working
        private static bool _viewerOperational = true;

        // Last time we tried to recover the viewer
        private static DateTime _lastRecoveryAttempt = DateTime.MinValue;

        // Default server port
        private static int _serverPort = 5199;

        /// <summary>
        /// Initialize the global logger
        /// </summary>
        /// <param name="port">HTTP port for the log viewer</param>
        /// <param name="autoRecover">Automatically try to recover from failures</param>
        /// <returns>True if initialization was successful</returns>
        public static bool Initialize(int port = 19867, bool autoRecover = true)
        {
            lock (_initLock)
            {
                if (_initialized)
                    return true;

                _serverPort = port;

                try
                {
                    _logServer = new LogViewerServer(port);
                    _logServer.Start();

                    // Log the initialization
                    Log("AdvancedLogger initialized successfully", LogLevel.Info, "System", "Logging");

                    _initialized = true;
                    _viewerOperational = true;

                    // Try to forward any logs that were captured before initialization
                    if (_backupLogs.Count > 0)
                    {
                        DrainBackupLogs();
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to initialize AdvancedLogger: {ex.Message}");
                    _initialized = false;
                    _viewerOperational = false;
                    return false;
                }
            }
        }

        /// <summary>
        /// Returns the URL of the log viewer web interface
        /// </summary>
        public static string GetViewerUrl()
        {
            return $"http://localhost:{_serverPort}/";
        }

        /// <summary>
        /// Log a message with a specific log level and context information
        /// </summary>
        public static void Log(
            string message,
            LogLevel level = LogLevel.Info,
            string source = null,
            string category = null,
            object data = null,
            int? threadId = null,
            [CallerMemberName] string methodName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            // Get source location
            string fileName = Path.GetFileName(filePath);
            string location = $"{fileName}:{methodName}:{lineNumber}";

            // Create a log record
            var record = new LogRecord
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Source = source ?? "Unknown",
                Category = category ?? "General",
                Location = location,
                ThreadId = threadId ?? Thread.CurrentThread.ManagedThreadId,
                Data = data
            };

            LogRecordToServer(record);
        }

        /// <summary>
        /// Log an exception with context information
        /// </summary>
        public static void LogException(
            Exception exception,
            string message = null,
            string source = null,
            string category = null,
            object data = null,
            [CallerMemberName] string methodName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            string errorMessage = message ?? $"Exception: {exception.Message}";

            // Create a combined data object with exception details
            var exceptionData = new Dictionary<string, object>
            {
                ["Exception"] = new
                {
                    Type = exception.GetType().Name,
                    Message = exception.Message,
                    StackTrace = exception.StackTrace,
                    InnerException = exception.InnerException?.Message
                }
            };

            // Add custom data if provided
            if (data != null)
            {
                exceptionData["CustomData"] = data;
            }

            Log(
                errorMessage,
                LogLevel.Error,
                source,
                category ?? "Exception",
                exceptionData,
                methodName: methodName,
                filePath: filePath,
                lineNumber: lineNumber
            );
        }

        private static void LogRecordToServer(LogRecord record)
        {
            // Always write to console as a fallback
            Console.WriteLine($"[{record.Level}] {record.Message}");

            // Try to forward to the log viewer if it's initialized and operational
            if (_initialized && _viewerOperational)
            {
                try
                {
                    // Convert the record to JSON

                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = false,
                    };
                    options.Converters.Add(new JsonStringEnumConverter());

                    string json = JsonSerializer.Serialize(record, options);
                    _logServer.AddLogEntry(json);
                }
                catch (Exception ex)
                {
                    // Mark the viewer as non-operational
                    _viewerOperational = false;

                    // Store the failure reason
                    _backupLogs.Enqueue(new LogRecord
                    {
                        Id = Guid.NewGuid().ToString(),
                        Timestamp = DateTime.Now,
                        Level = LogLevel.Error,
                        Message = $"Log viewer failed: {ex.Message}",
                        Source = "System",
                        Category = "Logging"
                    });

                    // Store the original record in the backup queue
                    _backupLogs.Enqueue(record);

                    // Try to recover if it's been at least 1 minute since the last attempt
                    if ((DateTime.Now - _lastRecoveryAttempt).TotalMinutes >= 1)
                    {
                        TryRecover();
                    }
                }
            }
            else
            {
                // Server not initialized or operational, store in backup queue
                _backupLogs.Enqueue(record);

                // Ensure we don't exceed maximum backup size
                while (_backupLogs.Count > MAX_BACKUP_LOGS)
                {
                    _backupLogs.TryDequeue(out _);
                }

                // Try to initialize if not yet initialized
                if (!_initialized && !_viewerOperational)
                {
                    Initialize();
                }
                // Try to recover if it's been at least 1 minute since the last attempt
                else if (!_viewerOperational && (DateTime.Now - _lastRecoveryAttempt).TotalMinutes >= 1)
                {
                    TryRecover();
                }
            }
        }

        // Convenience methods for different log levels
        public static void Info(string message, string source = null, string category = null, object data = null) =>
            Log(message, LogLevel.Info, source, category, data);

        public static void Warning(string message, string source = null, string category = null, object data = null) =>
            Log(message, LogLevel.Warning, source, category, data);

        public static void Error(string message, string source = null, string category = null, object data = null) =>
            Log(message, LogLevel.Error, source, category, data);

        public static void Debug(string message, string source = null, string category = null, object data = null) =>
            Log(message, LogLevel.Debug, source, category, data);

        public static void Critical(string message, string source = null, string category = null, object data = null) =>
            Log(message, LogLevel.Critical, source, category, data);

        /// <summary>
        /// Try to recover the log viewer if it's not operational
        /// </summary>
        private static void TryRecover()
        {
            _lastRecoveryAttempt = DateTime.Now;

            try
            {
                // Try to close and restart the log server
                _logServer.Stop();
                Thread.Sleep(1000);
                _logServer.Start();

                // Mark as operational and try to forward any pending logs
                _viewerOperational = true;
                DrainBackupLogs();

                // Log successful recovery
                Log("Log viewer recovered successfully", LogLevel.Info, "System", "Logging");
            }
            catch (Exception ex)
            {
                // Keep the server marked as non-operational
                _viewerOperational = false;
                Console.WriteLine($"Failed to recover log viewer: {ex.Message}");
            }
        }

        /// <summary>
        /// Try to forward logs from the backup queue to the log viewer
        /// </summary>
        private static void DrainBackupLogs()
        {
            if (!_viewerOperational || !_initialized)
                return;

            try
            {
                // Get a snapshot of the current backup logs
                var logsToForward = new List<LogRecord>();
                while (_backupLogs.Count > 0 && _backupLogs.TryDequeue(out var record))
                {
                    logsToForward.Add(record);
                }

                // Send them to the log viewer
                foreach (var record in logsToForward)
                {
                    string json = JsonSerializer.Serialize(record);
                    _logServer.AddLogEntry(json);
                }

                if (logsToForward.Count > 0)
                {
                    Console.WriteLine($"Forwarded {logsToForward.Count} backup logs to the viewer");
                }
            }
            catch (Exception ex)
            {
                // If forwarding fails, mark as non-operational again
                _viewerOperational = false;
                Console.WriteLine($"Failed to forward backup logs: {ex.Message}");
            }
        }

        /// <summary>
        /// Shutdown the global logger
        /// </summary>
        public static void Shutdown()
        {
            lock (_initLock)
            {
                if (_initialized)
                {
                    try
                    {
                        _logServer.Stop();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error shutting down log viewer: {ex.Message}");
                    }

                    _initialized = false;
                    _viewerOperational = false;
                }
            }
        }
    }
}
#endregion