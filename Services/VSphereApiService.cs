using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;

namespace VCenterMigrationTool.Services
{
    public class VSphereApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<VSphereApiService> _logger;
        private readonly Dictionary<string, string> _sessionTokens = new();

        public VSphereApiService(HttpClient httpClient, ILogger<VSphereApiService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            
            // Configure HttpClient for vSphere API
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "VCenterMigrationTool/1.0");
        }

        /// <summary>
        /// Authenticate and get session token for vCenter API calls
        /// </summary>
        public async Task<(bool success, string sessionToken)> AuthenticateAsync(VCenterConnectionInfo connection, string password)
        {
            try
            {
                var connectionKey = $"{connection.ServerAddress}:{connection.Username}";
                
                // Check if we already have a valid session
                if (_sessionTokens.ContainsKey(connectionKey))
                {
                    var existingToken = _sessionTokens[connectionKey];
                    if (await ValidateSessionAsync(connection.ServerAddress, existingToken))
                    {
                        return (true, existingToken);
                    }
                    _sessionTokens.Remove(connectionKey);
                }

                var baseUrl = $"https://{connection.ServerAddress}/api";
                var authUrl = $"{baseUrl}/session";

                // Create basic auth header
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connection.Username}:{password}"));
                
                using var request = new HttpRequestMessage(HttpMethod.Post, authUrl);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogDebug("vCenter API authentication response: {Content}", content);
                    
                    try
                    {
                        var sessionToken = content.Trim('"'); // Remove quotes from JSON string if present
                        
                        // Handle case where response might be JSON object instead of just a string
                        if (content.StartsWith("{"))
                        {
                            var jsonData = JsonSerializer.Deserialize<JsonElement>(content);
                            if (jsonData.TryGetProperty("value", out var valueElement))
                            {
                                sessionToken = valueElement.GetString() ?? "";
                            }
                            else if (jsonData.TryGetProperty("session_id", out var sessionElement))
                            {
                                sessionToken = sessionElement.GetString() ?? "";
                            }
                        }
                        
                        if (string.IsNullOrEmpty(sessionToken))
                        {
                            _logger.LogError("Empty session token received from vCenter API: {Server}", connection.ServerAddress);
                            return (false, string.Empty);
                        }
                        
                        _sessionTokens[connectionKey] = sessionToken;
                        _logger.LogInformation("Successfully authenticated to vCenter API: {Server}", connection.ServerAddress);
                        
                        return (true, sessionToken);
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogError(jsonEx, "Failed to parse vCenter API authentication response. Content: {Content}", content);
                        return (false, string.Empty);
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to authenticate to vCenter API: {Server} - {StatusCode}", 
                        connection.ServerAddress, response.StatusCode);
                    return (false, string.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error authenticating to vCenter API: {Server}", connection.ServerAddress);
                return (false, string.Empty);
            }
        }

        /// <summary>
        /// Validate if a session token is still valid
        /// </summary>
        private async Task<bool> ValidateSessionAsync(string serverAddress, string sessionToken)
        {
            try
            {
                var baseUrl = $"https://{serverAddress}/api";
                var validationUrl = $"{baseUrl}/session";

                using var request = new HttpRequestMessage(HttpMethod.Get, validationUrl);
                request.Headers.Add("vmware-api-session-id", sessionToken);

                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get basic connection status using vSphere API
        /// </summary>
        public async Task<(bool isConnected, string version, string build)> GetConnectionStatusAsync(VCenterConnectionInfo connection, string password)
        {
            try
            {
                var (authSuccess, sessionToken) = await AuthenticateAsync(connection, password);
                if (!authSuccess)
                {
                    return (false, string.Empty, string.Empty);
                }

                var baseUrl = $"https://{connection.ServerAddress}/api";
                var aboutUrl = $"{baseUrl}/appliance/system/version";

                using var request = new HttpRequestMessage(HttpMethod.Get, aboutUrl);
                request.Headers.Add("vmware-api-session-id", sessionToken);

                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogDebug("vCenter API version response: {Content}", content);
                    
                    try
                    {
                        var versionInfo = JsonSerializer.Deserialize<JsonElement>(content);
                        
                        var version = versionInfo.TryGetProperty("version", out var versionElement) ? versionElement.GetString() ?? "" : "";
                        var build = versionInfo.TryGetProperty("build", out var buildElement) ? buildElement.GetString() ?? "" : "";
                        
                        return (true, version, build);
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogError(jsonEx, "Failed to parse vCenter API version response. Content: {Content}", content);
                        return (false, string.Empty, string.Empty);
                    }
                }
                
                return (false, string.Empty, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting connection status from vCenter API: {Server}", connection.ServerAddress);
                return (false, string.Empty, string.Empty);
            }
        }

        /// <summary>
        /// Get basic inventory counts using vSphere API
        /// </summary>
        public async Task<InventoryCounts> GetInventoryCountsAsync(VCenterConnectionInfo connection, string password)
        {
            var counts = new InventoryCounts();
            
            try
            {
                var (authSuccess, sessionToken) = await AuthenticateAsync(connection, password);
                if (!authSuccess)
                {
                    return counts;
                }

                var baseUrl = $"https://{connection.ServerAddress}/api";
                
                // Get VM count
                counts.VmCount = await GetResourceCountAsync(baseUrl, sessionToken, "vcenter/vm");
                
                // Get Host count  
                counts.HostCount = await GetResourceCountAsync(baseUrl, sessionToken, "vcenter/host");
                
                // Get Cluster count
                counts.ClusterCount = await GetResourceCountAsync(baseUrl, sessionToken, "vcenter/cluster");
                
                // Get Datacenter count
                counts.DatacenterCount = await GetResourceCountAsync(baseUrl, sessionToken, "vcenter/datacenter");
                
                // Get Datastore count
                counts.DatastoreCount = await GetResourceCountAsync(baseUrl, sessionToken, "vcenter/datastore");

                _logger.LogInformation("Retrieved inventory counts for {Server}: VMs={VmCount}, Hosts={HostCount}, Clusters={ClusterCount}", 
                    connection.ServerAddress, counts.VmCount, counts.HostCount, counts.ClusterCount);
                    
                return counts;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting inventory counts from vCenter API: {Server}", connection.ServerAddress);
                return counts;
            }
        }

        /// <summary>
        /// Get count of a specific resource type
        /// </summary>
        private async Task<int> GetResourceCountAsync(string baseUrl, string sessionToken, string endpoint)
        {
            try
            {
                var url = $"{baseUrl}/{endpoint}";
                
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("vmware-api-session-id", sessionToken);

                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogDebug("vCenter API {Endpoint} response: {Content}", endpoint, content);
                    
                    try
                    {
                        var jsonData = JsonSerializer.Deserialize<JsonElement>(content);
                        
                        // Handle case where API returns object with "value" property
                        if (jsonData.ValueKind == JsonValueKind.Object && jsonData.TryGetProperty("value", out var valueElement) && valueElement.ValueKind == JsonValueKind.Array)
                        {
                            return valueElement.GetArrayLength();
                        }
                        // Handle case where API returns direct array
                        else if (jsonData.ValueKind == JsonValueKind.Array)
                        {
                            return jsonData.GetArrayLength();
                        }
                        else
                        {
                            _logger.LogWarning("vCenter API response for {Endpoint} unexpected format. Expected object with 'value' property or direct array. Actual ValueKind: {ValueKind}. Response: {Content}", endpoint, jsonData.ValueKind, content);
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogError(jsonEx, "Failed to parse vCenter API response for {Endpoint}. Content: {Content}", endpoint, content);
                    }
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting count for endpoint: {Endpoint}", endpoint);
                return 0;
            }
        }

        /// <summary>
        /// Logout and invalidate session
        /// </summary>
        public async Task LogoutAsync(VCenterConnectionInfo connection)
        {
            try
            {
                var connectionKey = $"{connection.ServerAddress}:{connection.Username}";
                
                if (_sessionTokens.TryGetValue(connectionKey, out var sessionToken))
                {
                    var baseUrl = $"https://{connection.ServerAddress}/api";
                    var logoutUrl = $"{baseUrl}/session";

                    using var request = new HttpRequestMessage(HttpMethod.Delete, logoutUrl);
                    request.Headers.Add("vmware-api-session-id", sessionToken);

                    await _httpClient.SendAsync(request);
                    _sessionTokens.Remove(connectionKey);
                    
                    _logger.LogInformation("Logged out from vCenter API: {Server}", connection.ServerAddress);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during logout from vCenter API: {Server}", connection.ServerAddress);
            }
        }
    }

    /// <summary>
    /// vCenter connection information for API calls
    /// </summary>
    public class VCenterConnectionInfo
    {
        public string ServerAddress { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
    }

    /// <summary>
    /// Basic inventory counts from vSphere API
    /// </summary>
    public class InventoryCounts
    {
        public int VmCount { get; set; }
        public int HostCount { get; set; }
        public int ClusterCount { get; set; }
        public int DatacenterCount { get; set; }
        public int DatastoreCount { get; set; }
        public int ResourcePoolCount { get; set; }
        public int NetworkCount { get; set; }
    }
}