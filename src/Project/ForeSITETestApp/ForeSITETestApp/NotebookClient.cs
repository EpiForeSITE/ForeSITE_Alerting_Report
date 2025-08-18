using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ForeSITETestApp
{
    /// <summary>
    /// Client for communicating with the Python Jupyter-like server
    /// </summary>
    public class NotebookClient : IDisposable
    {
        private readonly HttpClient _httpClient;

        public NotebookClient(HttpClient httpClient)
        {

            _httpClient = httpClient;
        }

        /// <summary>
        /// Execute Python code on the server
        /// </summary>
        /// <param name="code">Python code to execute</param>
        /// <param name="cellType">Type of cell (code or markdown)</param>
        /// <returns>Execution result</returns>
        public async Task<ExecutionResult> ExecuteCodeAsync(string code, string cellType = "code")
        {
            try
            {
                var requestData = new
                {
                    code = code,
                    cell_type = cellType
                };

                var json = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("/execute", content);
                var responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonConvert.DeserializeObject<ExecutionResult>(responseText);
                    return result ?? new ExecutionResult
                    {
                        Success = false,
                        Error = "Failed to deserialize response",
                        Output = responseText
                    };
                }
                else
                {
                    return new ExecutionResult
                    {
                        Success = false,
                        Error = $"Server Error: {response.StatusCode}",
                        Output = responseText
                    };
                }
            }
            catch (TaskCanceledException)
            {
                return new ExecutionResult
                {
                    Success = false,
                    Error = "Request timed out",
                    Output = ""
                };
            }
            catch (Exception ex)
            {
                return new ExecutionResult
                {
                    Success = false,
                    Error = $"Connection Error: {ex.Message}",
                    Output = ""
                };
            }
        }

        /// <summary>
        /// Get the current namespace variables from the server
        /// </summary>
        /// <returns>Namespace information</returns>
        public async Task<NamespaceInfo> GetNamespaceAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/namespace");
                var responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return JsonConvert.DeserializeObject<NamespaceInfo>(responseText) ?? new NamespaceInfo();
                }
                else
                {
                    return new NamespaceInfo
                    {
                        Error = $"Server Error: {response.StatusCode} - {responseText}"
                    };
                }
            }
            catch (Exception ex)
            {
                return new NamespaceInfo
                {
                    Error = $"Connection Error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Clear the server's namespace
        /// </summary>
        /// <returns>True if successful</returns>
        public async Task<bool> ClearNamespaceAsync()
        {
            try
            {
                var content = new StringContent("{}", Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("/clear_namespace", content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if the server is running and accessible
        /// </summary>
        /// <returns>True if server is accessible</returns>
        public async Task<bool> IsServerRunningAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/namespace");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Execute epidemiological analysis (original functionality)
        /// </summary>
        /// <param name="analysisRequest">Analysis parameters</param>
        /// <returns>Analysis result</returns>
        public async Task<EpidemiologicalResult> ExecuteEpidemiologicalAnalysisAsync(EpidemiologicalRequest analysisRequest)
        {
            try
            {
                var json = JsonConvert.SerializeObject(analysisRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("/epyapi", content);
                var responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return JsonConvert.DeserializeObject<EpidemiologicalResult>(responseText) ?? new EpidemiologicalResult
                    {
                        Status = "error",
                        Message = "Failed to deserialize response"
                    };
                }
                else
                {
                    return new EpidemiologicalResult
                    {
                        Status = "error",
                        Message = $"Server Error: {response.StatusCode} - {responseText}"
                    };
                }
            }
            catch (Exception ex)
            {
                return new EpidemiologicalResult
                {
                    Status = "error",
                    Message = $"Connection Error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Shutdown the server
        /// </summary>
        /// <returns>True if successful</returns>
        public async Task<bool> ShutdownServerAsync()
        {
            try
            {
                var content = new StringContent("{}", Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("/shutdown", content);
                return true; // Server will shut down regardless of response
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Add a data source variable to the Python namespace
        /// </summary>
        /// <param name="datasource">Data source name</param>
        /// <param name="variableName">Variable name to create</param>
        /// <param name="threshold">Threshold value for outbreak detection</param>
        /// <returns>Result of the operation</returns>
        public async Task<AddVariableResult> AddVariableAsync(string datasource, string variableName, int threshold = 1500)
        {
            try
            {
                var requestData = new
                {
                    datasource = datasource,
                    variable_name = variableName,
                    threshold = threshold
                };

                var json = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("/addvariable", content);
                var responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return JsonConvert.DeserializeObject<AddVariableResult>(responseText) ?? new AddVariableResult
                    {
                        Status = "error",
                        Message = "Failed to deserialize response"
                    };
                }
                else
                {
                    return new AddVariableResult
                    {
                        Status = "error",
                        Message = $"Server Error: {response.StatusCode} - {responseText}"
                    };
                }
            }
            catch (Exception ex)
            {
                return new AddVariableResult
                {
                    Status = "error",
                    Message = $"Connection Error: {ex.Message}"
                };
            }
        }

        public void Dispose()
        {
            //_httpClient?.Dispose();
        }
    }

}