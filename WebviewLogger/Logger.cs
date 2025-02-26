using System.Collections.Concurrent;
using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace WebviewLogger
{
    internal class Logger
    {
    }

    #region HTTP Server Implementation

    public class LogViewerServer
    {
        private readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
        private readonly ManualResetEvent _logEvent = new ManualResetEvent(false);
        private readonly int _port;
        private bool _isRunning;
        private HttpListener _listener;

        public LogViewerServer(int port = 5121)
        {
            _port = port;
            _isRunning = false;
        }

        public void Start()
        {
            if (_isRunning)
                return;

            _isRunning = true;
            _listener = new HttpListener();

            // Make sure we're listening on all possible prefixes
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");

            try
            {
                _listener.Start();
                Task.Run(() => HttpServerLoop());
                Console.WriteLine($"Advanced log viewer started at http://localhost:{_port}/");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start log viewer server: {ex.Message}");
                _isRunning = false;
            }
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _listener.Stop();
            Console.WriteLine("Log viewer server stopped");
        }

        public void AddLogEntry(string logEntry)
        {
            _logQueue.Enqueue(logEntry);
            _logEvent.Set();
        }

        private void HttpServerLoop()
        {
            while (_isRunning)
            {
                try
                {
                    HttpListenerContext context = _listener.GetContext();
                    Task.Run(() => HandleRequest(context));
                }
                catch (HttpListenerException)
                {
                    // Listener was stopped
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling request: {ex.Message}");
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            // Get the URL path and normalize it
            string path = context.Request.Url.AbsolutePath.ToLowerInvariant();

            // Handle the request based on path
            if (path == "/" || path == "/index.html" || path == "/index.htm")
            {
                SendAdvancedHtmlPage(context);
            }
            else if (path == "/logs")
            {
                SendLogsStream(context);
            }
            else if (path == "/style.css")
            {
                SendStylesheet(context);
            }
            else if (path == "/app.js")
            {
                SendJavaScript(context);
            }
            else
            {
                Send404(context);
            }
        }


        private void SendLogsStream(HttpListenerContext context)
        {
            try
            {
                // Required headers for SSE
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/event-stream";
                context.Response.Headers.Add("Cache-Control", "no-cache");
                context.Response.Headers.Add("Connection", "keep-alive");
                context.Response.Headers.Add("Access-Control-Allow-Origin", "*");

                using (StreamWriter writer = new StreamWriter(context.Response.OutputStream, Encoding.UTF8) { AutoFlush = true })
                {
                    Console.WriteLine("Started sending log stream");

                    // Send an initial event to establish the connection
                    writer.Write("event: open\n\n");
                    //writer.Write("data: Connected to log stream\n\n");

                    // Send any queued logs immediately
                    while (_logQueue.TryDequeue(out string initialLog))
                    {
                        writer.Write("data: " + initialLog + "\n\n");
                    }

                    int keepaliveCounter = 0;

                    while (_isRunning)
                    {
                        // Wait for a new log entry or timeout after 10 seconds
                        bool newData = _logEvent.WaitOne(TimeSpan.FromSeconds(10));

                        if (newData)
                        {
                            Console.WriteLine($"Sending {_logQueue.Count} log entries to client");

                            while (_logQueue.TryDequeue(out string logEntry))
                            {
                                writer.Write("data: " + logEntry + "\n\n");
                            //    writer.WriteLine();
                            }

                            _logEvent.Reset();
                            keepaliveCounter = 0;
                        }
                        else
                        {
                            // Send a keepalive comment every 10 seconds
                            keepaliveCounter++;
                            writer.Write("event: keepalive\n");
                            writer.Write(": keepalive ping " + keepaliveCounter + "\n\n");
                            //writer.WriteLine();

                            Console.WriteLine($"Sent keepalive ping {keepaliveCounter}");
                        }

                        // Check if the client is still connected
                        if (context.Response.OutputStream.CanWrite == false)
                        {
                            Console.WriteLine("Client disconnected from log stream");
                            break;
                        }
                    }
                }
            }
            catch (HttpListenerException)
            {
                Console.WriteLine("Client disconnected from log stream (connection closed)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in log stream: {ex.Message}");
            }
        }

        private void SendAdvancedHtmlPage(HttpListenerContext context)
        {
            string html = @"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Advanced Logging Dashboard</title>
    <link rel='stylesheet' href='style.css'>
</head>
<body>
    <div class='app-container'>
        <header>
            <h1>Advanced Logging Dashboard</h1>
            <div class='connection-status'>
                <span id='status-indicator'></span>
                <span id='status-text'>Connecting...</span>
            </div>
        </header>
        
        <div class='toolbar'>
            <div class='search-container'>
                <input type='text' id='search-input' placeholder='Search logs...'>
                <button id='search-btn'>Search</button>
            </div>
            
            <div class='filter-container'>
                <div class='filter-group'>
                    <label>Level:</label>
                    <div class='filter-options' id='level-filters'>
                        <label><input type='checkbox' value='Debug' checked> Debug</label>
                        <label><input type='checkbox' value='Info' checked> Info</label>
                        <label><input type='checkbox' value='Warning' checked> Warning</label>
                        <label><input type='checkbox' value='Error' checked> Error</label>
                        <label><input type='checkbox' value='Critical' checked> Critical</label>
                    </div>
                </div>
                
                <div class='filter-group'>
                    <label>Source:</label>
                    <select id='source-filter'>
                        <option value=''>All Sources</option>
                    </select>
                </div>
                
                <div class='filter-group'>
                    <label>Category:</label>
                    <select id='category-filter'>
                        <option value=''>All Categories</option>
                    </select>
                </div>
                
                <button id='clear-filters-btn'>Clear Filters</button>
            </div>
        </div>
        
        <div class='content-area'>
            <div class='logs-container'>
                <table id='logs-table'>
                    <thead>
                        <tr>
                            <th>Time</th>
                            <th>Level</th>
                            <th>Source</th>
                            <th>Category</th>
                            <th>Message</th>
                        </tr>
                    </thead>
                    <tbody id='logs-body'></tbody>
                </table>
            </div>
            
            <div class='details-panel' id='details-panel'>
                <div class='details-header'>
                    <h3>Log Details</h3>
                    <button id='close-details-btn'>×</button>
                </div>
                <div class='details-content' id='details-content'>
                    <p>Select a log entry to view details</p>
                </div>
            </div>
        </div>
        
        <footer>
            <div class='controls'>
                <button id='pause-btn'>Pause</button>
                <button id='clear-btn'>Clear Logs</button>
                <button id='export-btn'>Export Logs</button>
            </div>
            <div class='stats'>
                <span id='logs-count'>0 logs</span>
                <span id='filtered-count'></span>
            </div>
        </footer>
    </div>
    
    <script src='app.js'></script>
</body>
</html>";

            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(html);

                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/html";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending HTML page: {ex.Message}");
            }
        }

        private void SendStylesheet(HttpListenerContext context)
        {
            string css = @"
:root {
    --bg-color: #f5f5f5;
    --header-bg: #2c3e50;
    --header-text: #ecf0f1;
    --panel-bg: #ffffff;
    --border-color: #ddd;
    --text-color: #333;
    --highlight-color: #3498db;
    --debug-color: #6c757d;
    --info-color: #17a2b8;
    --warning-color: #ffc107;
    --error-color: #dc3545;
    --critical-color: #6f42c1;
}

* {
    box-sizing: border-box;
    margin: 0;
    padding: 0;
}

body {
    font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
    background-color: var(--bg-color);
    color: var(--text-color);
    line-height: 1.6;
}

.app-container {
    display: flex;
    flex-direction: column;
    height: 100vh;
    max-height: 100vh;
    overflow: hidden;
}

/* Header */
header {
    background-color: var(--header-bg);
    color: var(--header-text);
    padding: 0.8rem 1.5rem;
    display: flex;
    justify-content: space-between;
    align-items: center;
}

header h1 {
    font-size: 1.5rem;
    font-weight: 500;
}

.connection-status {
    display: flex;
    align-items: center;
    font-size: 0.9rem;
}

#status-indicator {
    width: 10px;
    height: 10px;
    border-radius: 50%;
    margin-right: 8px;
    background-color: #dc3545;
}

#status-indicator.connected {
    background-color: #28a745;
}

/* Toolbar */
.toolbar {
    background-color: var(--panel-bg);
    border-bottom: 1px solid var(--border-color);
    padding: 1rem;
}

.search-container {
    display: flex;
    margin-bottom: 1rem;
}

.search-container input {
    flex: 1;
    padding: 0.5rem;
    border: 1px solid var(--border-color);
    border-radius: 4px 0 0 4px;
}

.search-container button {
    padding: 0.5rem 1rem;
    background-color: var(--highlight-color);
    color: white;
    border: none;
    border-radius: 0 4px 4px 0;
    cursor: pointer;
}

.filter-container {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 1rem;
}

.filter-group {
    display: flex;
    align-items: center;
    gap: 0.5rem;
}

.filter-options {
    display: flex;
    gap: 0.5rem;
}

select, button {
    padding: 0.4rem 0.8rem;
    border: 1px solid var(--border-color);
    border-radius: 4px;
}

button {
    background-color: #f8f9fa;
    cursor: pointer;
}

button:hover {
    background-color: #e9ecef;
}

/* Content Area */
.content-area {
    display: flex;
    flex: 1;
    overflow: hidden;
}

.logs-container {
    flex: 1;
    overflow: auto;
    padding: 1rem;
    background-color: var(--panel-bg);
}

#logs-table {
    width: 100%;
    border-collapse: collapse;
}

#logs-table th, #logs-table td {
    padding: 0.6rem;
    text-align: left;
    border-bottom: 1px solid var(--border-color);
}

#logs-table th {
    background-color: #f8f9fa;
    position: sticky;
    top: 0;
    z-index: 10;
}

#logs-table tr {
    cursor: pointer;
}

#logs-table tr:hover {
    background-color: rgba(0,0,0,0.03);
}

.log-row.selected {
    background-color: rgba(52, 152, 219, 0.1);
}

/* Level indicators */
.level-indicator {
    display: inline-block;
    padding: 0.2rem 0.6rem;
    border-radius: 4px;
    font-size: 0.8rem;
    font-weight: 500;
    text-align: center;
    min-width: 80px;
}

.level-Debug {
    background-color: #e9ecef;
    color: var(--debug-color);
}

.level-Info {
    background-color: #d1ecf1;
    color: var(--info-color);
}

.level-Warning {
    background-color: #fff3cd;
    color: #856404;
}

.level-Error {
    background-color: #f8d7da;
    color: #721c24;
}

.level-Critical {
    background-color: #f5e8ff;
    color: var(--critical-color);
}

/* Details Panel */
.details-panel {
    width: 0;
    background-color: var(--panel-bg);
    border-left: 1px solid var(--border-color);
    transition: width 0.3s ease;
    overflow: hidden;
}

.details-panel.active {
    width: 40%;
    min-width: 400px;
}

.details-header {
    padding: 1rem;
    background-color: #f8f9fa;
    border-bottom: 1px solid var(--border-color);
    display: flex;
    justify-content: space-between;
    align-items: center;
}

.details-header h3 {
    font-size: 1.1rem;
    font-weight: 500;
}

#close-details-btn {
    background: none;
    border: none;
    font-size: 1.5rem;
    cursor: pointer;
    color: #6c757d;
}

.details-content {
    padding: 1rem;
    overflow: auto;
    height: calc(100% - 50px);
}

.details-section {
    margin-bottom: 1rem;
}

.details-section h4 {
    font-size: 0.9rem;
    color: #6c757d;
    margin-bottom: 0.5rem;
    font-weight: 500;
}

.details-row {
    display: flex;
    margin-bottom: 0.5rem;
}

.details-label {
    width: 120px;
    font-weight: 500;
}

.details-value {
    flex: 1;
}

.data-container {
    background-color: #f8f9fa;
    padding: 1rem;
    border-radius: 4px;
    overflow: auto;
    max-height: 300px;
}

pre {
    margin: 0;
    white-space: pre-wrap;
    font-family: 'Consolas', 'Monaco', monospace;
    font-size: 0.9rem;
}

/* Footer */
footer {
    padding: 0.8rem 1.5rem;
    background-color: var(--panel-bg);
    border-top: 1px solid var(--border-color);
    display: flex;
    justify-content: space-between;
    align-items: center;
}

.controls {
    display: flex;
    gap: 0.5rem;
}

.stats {
    font-size: 0.9rem;
    color: #6c757d;
}

/* Responsive */
@media (max-width: 768px) {
    .filter-container {
        flex-direction: column;
        align-items: flex-start;
    }
    
    .details-panel.active {
        width: 100%;
        position: absolute;
        top: 0;
        right: 0;
        bottom: 0;
        z-index: 1000;
    }
}";

            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(css);

                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/css";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending CSS: {ex.Message}");
            }
        }
        private void SendJavaScript(HttpListenerContext context)
        {
            string js = @"
// Log storage
let allLogs = [];
let filteredLogs = [];
let sources = new Set();
let categories = new Set();
let isPaused = false;
let selectedLogId = null;

// DOM elements
const logsTable = document.getElementById('logs-body');
const logsCount = document.getElementById('logs-count');
const filteredCount = document.getElementById('filtered-count');
const statusIndicator = document.getElementById('status-indicator');
const statusText = document.getElementById('status-text');
const detailsPanel = document.getElementById('details-panel');
const detailsContent = document.getElementById('details-content');
const pauseBtn = document.getElementById('pause-btn');
const clearBtn = document.getElementById('clear-btn');
const exportBtn = document.getElementById('export-btn');
const searchInput = document.getElementById('search-input');
const searchBtn = document.getElementById('search-btn');
const sourceFilter = document.getElementById('source-filter');
const categoryFilter = document.getElementById('category-filter');
const levelFilters = document.getElementById('level-filters').querySelectorAll('input[type=""checkbox""]');
const clearFiltersBtn = document.getElementById('clear-filters-btn');
const closeDetailsBtn = document.getElementById('close-details-btn');

// Initialize the page
function init() {
    setupEventListeners();
    connectToLogStream();
    updateStats();
}

// Setup all event listeners
function setupEventListeners() {
    // Button actions
    pauseBtn.addEventListener('click', togglePause);
    clearBtn.addEventListener('click', clearLogs);
    exportBtn.addEventListener('click', exportLogs);
    closeDetailsBtn.addEventListener('click', hideDetails);
    
    // Search and filters
    searchBtn.addEventListener('click', applyFilters);
    searchInput.addEventListener('keyup', function(e) {
        if (e.key === 'Enter') applyFilters();
    });
    
    sourceFilter.addEventListener('change', applyFilters);
    categoryFilter.addEventListener('change', applyFilters);
    clearFiltersBtn.addEventListener('click', clearFilters);
    
    // Level filters
    levelFilters.forEach(checkbox => {
        checkbox.addEventListener('change', applyFilters);
    });
}

// Connect to the SSE log stream
function connectToLogStream() {
    statusText.textContent = 'Connecting...';
    statusIndicator.classList.remove('connected');
    
    const eventSource = new EventSource('/logs');
    
    eventSource.onopen = function() {
        statusText.textContent = 'Connected';
        statusIndicator.classList.add('connected');
    };
    
    eventSource.addEventListener('message', (e) => {
        if (!isPaused) {
            try {
                const logRecord = JSON.parse(event.data);
                processLogRecord(logRecord);
            } catch (error) {
                console.error('Failed to parse log data:', error);
            }
        }
    });

    eventSource.addEventListener('keepalive', (e) => {
        if (!isPaused) {
            console.log('Received keepalive ping');
        }
    });

    
    eventSource.onerror = function() {
        statusText.textContent = 'Disconnected';
        statusIndicator.classList.remove('connected');
        
        // Close the current connection
        eventSource.close();
        
        // Try to reconnect after a delay
        setTimeout(connectToLogStream, 5000);
    };
}

// Process a new log record
function processLogRecord(logRecord) {
    // Add to the full logs array
    allLogs.unshift(logRecord); // Add to beginning for newest-first
    
    // Update sources and categories for filters
    if (logRecord.Source && !sources.has(logRecord.Source)) {
        sources.add(logRecord.Source);
        updateSourceFilter();
    }
    
    if (logRecord.Category && !categories.has(logRecord.Category)) {
        categories.add(logRecord.Category);
        updateCategoryFilter();
    }
    
    // Check if it passes current filters
    if (logPassesFilters(logRecord)) {
        filteredLogs.unshift(logRecord);
        renderLogRow(logRecord);
    }
    
    // Update stats
    updateStats();
    
    // Limit the number of logs to prevent memory issues
    if (allLogs.length > 10000) {
        allLogs = allLogs.slice(0, 9000);
        applyFilters(); // Re-render with the reduced set
    }
}

// Check if a log passes the current filters
function logPassesFilters(log) {
    // Check level filter - more forgiving check
    const levelValue = log.Level || '';
    const levelCheckboxes = Array.from(levelFilters);
    const levelChecked = levelCheckboxes.some(cb => 
        levelValue === cb.value && cb.checked
    );
    
    if (!levelChecked) {
        console.log(""Log rejected by level filter:"", log.Level);
        return false;
    }
    
    // Check source filter
    const selectedSource = sourceFilter.value;
    if (selectedSource && log.Source !== selectedSource) return false;
    
    // Check category filter
    const selectedCategory = categoryFilter.value;
    if (selectedCategory && log.Category !== selectedCategory) return false;
    
    // Check search text
    const searchText = searchInput.value.toLowerCase();
    if (searchText) {
        const messageMatch = log.Message && log.Message.toLowerCase().includes(searchText);
        const sourceMatch = log.Source && log.Source.toLowerCase().includes(searchText);
        const categoryMatch = log.Category && log.Category.toLowerCase().includes(searchText);
        const locationMatch = log.Location && log.Location.toLowerCase().includes(searchText);
        
        if (!(messageMatch || sourceMatch || categoryMatch || locationMatch)) {
            return false;
        }
    }
    
    return true;
}

// Render a log row in the table
function renderLogRow(log) {
    const row = document.createElement('tr');
    row.className = 'log-row';
    row.setAttribute('data-id', log.Id);
    
    // Time column
    const timeCell = document.createElement('td');
    const time = new Date(log.Timestamp);
    timeCell.textContent = time.toLocaleTimeString();
    timeCell.title = time.toLocaleString();
    
    // Level column
    const levelCell = document.createElement('td');
    const levelSpan = document.createElement('span');
    levelSpan.className = `level-indicator level-${log.Level}`;
    levelSpan.textContent = log.Level;
    levelCell.appendChild(levelSpan);
    
    // Source column
    const sourceCell = document.createElement('td');
    sourceCell.textContent = log.Source || '-';
    
    // Category column
    const categoryCell = document.createElement('td');
    categoryCell.textContent = log.Category || '-';
    
    // Message column
    const messageCell = document.createElement('td');
    messageCell.textContent = log.Message;
    
    // Add all cells to the row
    row.appendChild(timeCell);
    row.appendChild(levelCell);
    row.appendChild(sourceCell);
    row.appendChild(categoryCell);
    row.appendChild(messageCell);
    
    // Add click handler to show details
    row.addEventListener('click', () => showLogDetails(log.Id));
    
    // Add to table (at the beginning)
    if (logsTable.firstChild) {
        logsTable.insertBefore(row, logsTable.firstChild);
    } else {
        logsTable.appendChild(row);
    }
}

// Apply all filters and search
function applyFilters() {
    // Clear the table
    logsTable.innerHTML = '';
    
    // Apply filters to all logs
    filteredLogs = allLogs.filter(logPassesFilters);
    
    // Render the filtered logs
    filteredLogs.forEach(log => renderLogRow(log));
    
    // Update stats
    updateStats();
    
    // Hide details panel if no logs match
    if (filteredLogs.length === 0) {
        hideDetails();
    }
}

// Update the source filter dropdown
function updateSourceFilter() {
    const currentSelection = sourceFilter.value;
    
    // Clear options except the first one
    while (sourceFilter.options.length > 1) {
        sourceFilter.remove(1);
    }
    
    // Add sorted options
    Array.from(sources).sort().forEach(source => {
        const option = document.createElement('option');
        option.value = source;
        option.textContent = source;
        sourceFilter.appendChild(option);
    });
    
    // Restore selection if possible
    if (currentSelection && Array.from(sources).includes(currentSelection)) {
        sourceFilter.value = currentSelection;
    }
}

// Update the category filter dropdown
function updateCategoryFilter() {
    const currentSelection = categoryFilter.value;
    
    // Clear options except the first one
    while (categoryFilter.options.length > 1) {
        categoryFilter.remove(1);
    }
    
    // Add sorted options
    Array.from(categories).sort().forEach(category => {
        const option = document.createElement('option');
        option.value = category;
        option.textContent = category;
        categoryFilter.appendChild(option);
    });
    
    // Restore selection if possible
    if (currentSelection && Array.from(categories).includes(currentSelection)) {
        categoryFilter.value = currentSelection;
    }
}

// Clear all filters
function clearFilters() {
    // Reset search box
    searchInput.value = '';
    
    // Reset dropdowns
    sourceFilter.value = '';
    categoryFilter.value = '';
    
    // Check all level checkboxes
    levelFilters.forEach(checkbox => {
        checkbox.checked = true;
    });
    
    // Apply the cleared filters
    applyFilters();
}

// Show log details in the side panel
function showLogDetails(logId) {
    // Find the log by ID
    const log = allLogs.find(l => l.Id === logId);
    if (!log) return;
    
    // Update selected row
    const rows = document.querySelectorAll('.log-row');
    rows.forEach(row => row.classList.remove('selected'));
    document.querySelector(`[data-id=""${logId}""]`)?.classList.add('selected');
    
    // Store selected ID
    selectedLogId = logId;
    
    // Build details HTML
    let html = `
        <div class=""details-section"">
            <div class=""details-row"">
                <div class=""details-label"">Time:</div>
                <div class=""details-value"">${new Date(log.Timestamp).toLocaleString()}</div>
            </div>
            <div class=""details-row"">
                <div class=""details-label"">Level:</div>
                <div class=""details-value""><span class=""level-indicator level-${log.Level}"">${log.Level}</span></div>
            </div>
            <div class=""details-row"">
                <div class=""details-label"">Source:</div>
                <div class=""details-value"">${log.Source || '-'}</div>
            </div>
            <div class=""details-row"">
                <div class=""details-label"">Category:</div>
                <div class=""details-value"">${log.Category || '-'}</div>
            </div>
            <div class=""details-row"">
                <div class=""details-label"">Location:</div>
                <div class=""details-value"">${log.Location || '-'}</div>
            </div>
            <div class=""details-row"">
                <div class=""details-label"">Thread ID:</div>
                <div class=""details-value"">${log.ThreadId || '-'}</div>
            </div>
        </div>
        
        <div class=""details-section"">
            <h4>Message</h4>
            <div class=""details-value"">${log.Message}</div>
        </div>
    `;
    
    // Add data object if present
    if (log.Data) {
        html += `
            <div class=""details-section"">
                <h4>Additional Data</h4>
                <div class=""data-container"">
                    <pre>${formatJson(log.Data)}</pre>
                </div>
            </div>
        `;
    }
    
    // Update content and show panel
    detailsContent.innerHTML = html;
    detailsPanel.classList.add('active');
}

// Format a JSON object for display
function formatJson(obj) {
    try {
        if (typeof obj === 'string') {
            // Try to parse if it's a JSON string
            return JSON.stringify(JSON.parse(obj), null, 2);
        } else {
            return JSON.stringify(obj, null, 2);
        }
    } catch (e) {
        return JSON.stringify(obj, null, 2);
    }
}

// Hide the details panel
function hideDetails() {
    detailsPanel.classList.remove('active');
    selectedLogId = null;
    
    // Remove selected class from all rows
    const rows = document.querySelectorAll('.log-row');
    rows.forEach(row => row.classList.remove('selected'));
}

// Toggle pause state
function togglePause() {
    isPaused = !isPaused;
    pauseBtn.textContent = isPaused ? 'Resume' : 'Pause';
    statusText.textContent = isPaused ? 'Paused' : 'Connected';
}

// Clear all logs
function clearLogs() {
    if (confirm('Are you sure you want to clear all logs?')) {
        allLogs = [];
        filteredLogs = [];
        logsTable.innerHTML = '';
        updateStats();
        hideDetails();
    }
}

// Export logs to JSON file
function exportLogs() {
    const logsToExport = filteredLogs.length > 0 ? filteredLogs : allLogs;
    const jsonStr = JSON.stringify(logsToExport, null, 2);
    const blob = new Blob([jsonStr], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    
    const a = document.createElement('a');
    a.href = url;
    a.download = `logs-export-${new Date().toISOString().slice(0, 19).replace(/:/g, '-')}.json`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}

// Update the log count statistics
function updateStats() {
    logsCount.textContent = `${allLogs.length} total logs`;
    
    if (filteredLogs.length !== allLogs.length) {
        filteredCount.textContent = `${filteredLogs.length} shown`;
    } else {
        filteredCount.textContent = '';
    }
}

// Initialize when the page loads
document.addEventListener('DOMContentLoaded', init);";

            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(js);

                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/javascript";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending JavaScript: {ex.Message}");
            }
        }

        private void Send404(HttpListenerContext context)
        {
            try
            {
                string notFound = "404 - Not Found: The requested resource does not exist.";
                byte[] buffer = Encoding.UTF8.GetBytes(notFound);

                context.Response.StatusCode = 404;
                context.Response.ContentType = "text/plain";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending 404 response: {ex.Message}");
            }
        }
    }
}
#endregion