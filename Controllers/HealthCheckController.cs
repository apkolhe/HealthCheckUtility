using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Azure.Platform.HealthCheck.Controllers
{
    public class LivenessEndpoint
    {
        public string Url { get; set; }
        public string ResponseTimeoutThresholdInMs { get; set; }
    }
    public class HealthCheckEndpointController : Controller
    {

        private readonly IConfiguration _configuration;

        private readonly ILogger _logger;

        private const string ConfigKey = "status";

        private const string ConfigExpectedValue = "azure";

        private const string SecretConfigKey = "password-password";

        private const string SecretConfigExpectedValue = "londonbridgeisfallingdown";

        private string MachineName = Environment.MachineName;

        private static readonly HttpClient client = new HttpClient();

        private static Dictionary<string, Tuple<int, DateTime>> ErroredUrls = new Dictionary<string, Tuple<int, DateTime>>();

        private static LivenessEndpoint[] endpoints;

        private static string failedDuration;

        private static string failedUrlthresholdCount;

        private static string unhealthyStatusThresholdPercentage;

        private static string responseTimeoutThresholdInMs;


        public HealthCheckEndpointController(IConfiguration configuration, ILogger<HealthCheckEndpointController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet]
        [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
        [Route("/failedping1")]
        public async Task<IActionResult> Failedping1()
        {
            //return Ok();
            return NotFound(((int)HttpStatusCode.NotFound).ToString());
        }

        [HttpGet]
        [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
        [Route("/failedping2")]
        public async Task<IActionResult> Failedping2()
        {
            return Ok();
            //return NotFound(((int)HttpStatusCode.NotFound).ToString());
        }

        [HttpGet]
        [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
        [Route("/failedping3")]
        public async Task<IActionResult> Failedping3()
        {
            //return Ok();
            return NotFound(((int)HttpStatusCode.NotFound).ToString());
        }

        [HttpGet]
        [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
        [Route("/failedping4")]
        public async Task<IActionResult> Failedping4()
        {
            return NotFound(((int)HttpStatusCode.NotFound).ToString());
        }

        [HttpGet]
        [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
        [Route("/ping")]
        public async Task<IActionResult> GetStandardHealthCheck()
        {
            endpoints = _configuration.GetSection("livenessEndpoint:urls").Get<LivenessEndpoint[]>();
            unhealthyStatusThresholdPercentage = _configuration["unhealthyStatusThresholdPercentage"];
            responseTimeoutThresholdInMs = _configuration["responseTimeoutThresholdInMs"];

            foreach (var endpoint in endpoints)
            {
                var response = await CheckUrl(endpoint.Url);

                var responseThreshold = Convert.ToInt64(endpoint.ResponseTimeoutThresholdInMs);

                if ((responseThreshold != 0 && response.Item1 > responseThreshold) || response.Item2 != HttpStatusCode.OK)
                {
                    _logger.LogWarning($"Adding Failed Url - {endpoint} to the errored endpoint list");
                    //Maintain a list of Errored URLs
                    await AddUrlFailUrl(endpoint.Url);
                    // Check if errored endpoint failed y times in z minutes and alert needs to be triggered
                    if (IsAlertTriggered(endpoint.Url))
                    {
                        // Trigger alert
                        Console.WriteLine("Alert Sent");
                        _logger.LogError($"Alert has been triggered for {endpoint} failure");
                    }

                }
            }

            if (CheckUnhealthyStatusThreshold() > Convert.ToInt32(unhealthyStatusThresholdPercentage))
            {
                _logger.LogError("HealthCheck failed");
                return new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
            }

            return Ok(((int)HttpStatusCode.OK).ToString());
        }

        private async Task<Tuple<long, HttpStatusCode>> CheckUrl(string url)
        {
            Stopwatch watch = new Stopwatch();
            watch.Start();
            var response = await client.GetAsync(url);  // make call to the URL to obtain status
            watch.Stop();
            var result = new Tuple<long, HttpStatusCode>(watch.ElapsedMilliseconds, response.StatusCode);
            _logger.LogInformation($"Response returned in {watch.ElapsedMilliseconds} with status code {response.StatusCode} for {url}");
            return result;
        }

        private async Task AddUrlFailUrl(string url)
        {
            failedDuration = _configuration["failedDurationInSeconds"];
            failedUrlthresholdCount = _configuration["failedUrlthresholdCount"];

            // check endpoint exists in the list
            var result = ErroredUrls.Any(u => u.Key == url);

            if (ErroredUrls.Count == 0 || result == false)
            {
                ErroredUrls.Add(url, new Tuple<int, DateTime>(1, DateTime.Now));

            }
            else
            {
                var errorUrl = ErroredUrls.Where(u => u.Key == url);
                var errorCount = errorUrl.Select(v => v.Value.Item1).SingleOrDefault();
                var lastErrorDateTime = errorUrl.Select(v => v.Value.Item2).SingleOrDefault();

                var totalSecondsSinceLastFailure = Math.Abs((lastErrorDateTime - DateTime.Now).TotalSeconds);

                if (totalSecondsSinceLastFailure <= Convert.ToInt32(failedDuration))
                {
                    var newCount = ++errorCount;
                    ErroredUrls[url] = Tuple.Create(newCount, DateTime.Now);
                    _logger.LogWarning($"Url {url} failed {newCount} times in last {totalSecondsSinceLastFailure} seconds of previous failure");
                }
                else
                {
                    ErroredUrls[url] = Tuple.Create(0, DateTime.Now); // reset error count to 0 error outside fail duration
                }
            }
        }

        private bool IsAlertTriggered(string url)
        {
            failedUrlthresholdCount = _configuration["failedUrlthresholdCount"];
            return ErroredUrls.Any(u => u.Key.Equals(url) && u.Value.Item1 > Convert.ToInt32(failedUrlthresholdCount));
        }

        private int CheckUnhealthyStatusThreshold()
        {
            var errorUrlCount = ErroredUrls.Where(u => u.Value.Item1 > Convert.ToInt32(failedUrlthresholdCount)).Count();
            var totalUrlCount = endpoints.Count();

            var value = (double)(errorUrlCount * 100 / totalUrlCount);
            var percentage = Convert.ToInt32(Math.Round(value, 0));

            return percentage;
        }

        [HttpGet]
        [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
        [Route("/ping-secrets-vault")]
        public IActionResult GetSecrets()
        {
            var secretPassword = _configuration.GetValue<string>(SecretConfigKey);
            if (!string.IsNullOrWhiteSpace(secretPassword)
                && secretPassword.ToLowerInvariant().Equals(SecretConfigExpectedValue.ToLowerInvariant()))
            {
                return Ok(secretPassword);
            }

            var notFoundResult = new { MachineName, HttpStatusCode.ServiceUnavailable };
            return NotFound(notFoundResult);
        }
    }
}