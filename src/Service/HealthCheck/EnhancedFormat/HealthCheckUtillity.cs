// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    /// <summary>
    /// Creates a JSON response for the health check endpoint using the provided health report.
    /// If the response has already been created, it will be reused.
    /// </summary>
    public class HealthCheckUtlity
    {
        // Dependencies
        private ILogger? _logger;

        public HealthCheckUtlity(ILogger<HealthCheckUtlity>? logger)
        {
            _logger = logger;
        }

        public DabHealthCheckReport GetHealthCheckResponse(HealthReport healthReport, RuntimeConfig runtimeConfig)
        {
            // Create a JSON response for the health check endpoint using the provided health report.
            // If the response has already been created, it will be reused.
            if (runtimeConfig?.Runtime != null && runtimeConfig.Runtime?.Health != null && runtimeConfig.Runtime.Health.Enabled)
            {
                LogTrace("Enhanced Health check is enabled in the runtime configuration.");
                DabHealthCheckReport dabHealthCheckReport = new()
                {
                    HealthStatus = Config.ObjectModel.HealthStatus.Healthy
                };
                UpdateVersionAndAppName(ref dabHealthCheckReport, healthReport);
                UpdateDabConfigurationDetails(ref dabHealthCheckReport, runtimeConfig);
                UpdateHealthCheckDetails(ref dabHealthCheckReport, runtimeConfig);
                return dabHealthCheckReport;
            }

            return new DabHealthCheckReport
            {
                HealthStatus = Config.ObjectModel.HealthStatus.Unhealthy
            };
        }

        private static void UpdateDabConfigurationDetails(ref DabHealthCheckReport dabHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            dabHealthCheckReport.DabConfigurationDetails = new DabConfigurationDetails
            {
                Rest = runtimeConfig?.Runtime?.Rest != null && runtimeConfig.Runtime.Rest.Enabled,
                GraphQL = runtimeConfig?.Runtime?.GraphQL != null && runtimeConfig.Runtime.GraphQL.Enabled,
                Caching = runtimeConfig?.Runtime?.IsCachingEnabled ?? false,
                Telemetry = runtimeConfig?.Runtime?.Telemetry != null,
                Mode = runtimeConfig?.Runtime?.Host?.Mode ?? HostMode.Development,
                DabSchema = runtimeConfig != null ? runtimeConfig.Schema : string.Empty,
            };
        }

        private void UpdateHealthCheckDetails(ref DabHealthCheckReport dabHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            if (dabHealthCheckReport != null)
            {
                dabHealthCheckReport.HealthCheckResults = new DabHealthCheckResults()
                {
                    DataSourceHealthCheckResults = new List<HealthCheckResultEntry>(),
                    EntityHealthCheckResults = new List<HealthCheckResultEntry>(),
                };

                if (runtimeConfig != null)
                {
                    UpdateDataSourceHealthCheckResults(ref dabHealthCheckReport, runtimeConfig);
                    UpdateEntityHealthCheckResults(ref dabHealthCheckReport, runtimeConfig);
                }
            }
        }

        private void UpdateEntityHealthCheckResults(ref DabHealthCheckReport dabHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            if (runtimeConfig?.Entities != null && dabHealthCheckReport?.HealthCheckResults?.EntityHealthCheckResults != null)
            {
                foreach (KeyValuePair<string, Entity> Entity in runtimeConfig.Entities.Entities)
                {
                    DabHealthCheckConfig? healthConfig = Entity.Value?.Health;
                    if (healthConfig != null && healthConfig.Enabled)
                    {
                        string query = healthConfig.Query ?? string.Empty;
                        int responseTime = ExecuteSqlQuery(query);
                        if (responseTime <= healthConfig.ThresholdMs)
                        {
                            dabHealthCheckReport.HealthCheckResults.EntityHealthCheckResults.Add(new HealthCheckResultEntry
                            {
                                Name = Entity.Key,
                                ResponseTimeData = new ResponseTimeData
                                {
                                    ResponseTimeMs = responseTime,
                                    MaxAllowedResponseTimeMs = healthConfig.ThresholdMs
                                },
                                HealthStatus = Config.ObjectModel.HealthStatus.Healthy
                            });
                        }
                        else
                        {
                            dabHealthCheckReport.HealthCheckResults.EntityHealthCheckResults.Add(new HealthCheckResultEntry
                            {
                                Name = Entity.Key,
                                Exception = "The response time exceeded the threshold.",
                                ResponseTimeData = new ResponseTimeData
                                {
                                    ResponseTimeMs = responseTime,
                                    MaxAllowedResponseTimeMs = healthConfig.ThresholdMs
                                },
                                HealthStatus = Config.ObjectModel.HealthStatus.Unhealthy
                            });
                        }
                    }
                }
            }
        }

        private void UpdateDataSourceHealthCheckResults(ref DabHealthCheckReport dabHealthCheckReport, RuntimeConfig runtimeConfig)
        {
            if (runtimeConfig?.DataSource != null && runtimeConfig.DataSource?.Health != null && runtimeConfig.DataSource.Health.Enabled)
            {
                string query = runtimeConfig.DataSource?.Health.Query ?? string.Empty;
                int responseTime = ExecuteSqlQuery(query);
                if (dabHealthCheckReport?.HealthCheckResults?.DataSourceHealthCheckResults != null)
                {
                    if (responseTime <= runtimeConfig?.DataSource?.Health.ThresholdMs)
                    {
                        dabHealthCheckReport.HealthCheckResults.DataSourceHealthCheckResults.Add(new HealthCheckResultEntry
                        {
                            Name = runtimeConfig?.DataSource?.Health.Moniker,
                            ResponseTimeData = new ResponseTimeData
                            {
                                ResponseTimeMs = responseTime,
                                MaxAllowedResponseTimeMs = runtimeConfig?.DataSource?.Health.ThresholdMs
                            },
                            HealthStatus = Config.ObjectModel.HealthStatus.Healthy
                        });
                    }
                    else
                    {
                        dabHealthCheckReport.HealthCheckResults.DataSourceHealthCheckResults.Add(new HealthCheckResultEntry
                        {
                            Name = runtimeConfig?.DataSource?.Health.Moniker,
                            Exception = "The response time exceeded the threshold.",
                            ResponseTimeData = new ResponseTimeData
                            {
                                ResponseTimeMs = responseTime,
                                MaxAllowedResponseTimeMs = runtimeConfig?.DataSource?.Health.ThresholdMs
                            },
                            HealthStatus = Config.ObjectModel.HealthStatus.Unhealthy
                        });
                    }
                }
            }

        }

        private int ExecuteSqlQuery(string query)
        {
            // TODO: Update this function to execute the respected query wit the DB
            LogTrace($"Executing SQL query: {query}");
            return 5;
        }

        private void UpdateVersionAndAppName(ref DabHealthCheckReport response, HealthReport healthReport)
        {
            // Update the version and app name to the response.
            if (healthReport.Entries.TryGetValue(key: typeof(DabHealthCheck).Name, out HealthReportEntry healthReportEntry))
            {
                if (healthReportEntry.Data.TryGetValue(DabHealthCheck.DAB_VERSION_KEY, out object? versionValue) && versionValue is string versionNumber)
                {
                    response.Version = versionNumber;
                }
                else
                {
                    LogTrace("DabHealthCheck did not contain the version number in the HealthReport.");
                }

                if (healthReportEntry.Data.TryGetValue(DabHealthCheck.DAB_APPNAME_KEY, out object? appNameValue) && appNameValue is string appName)
                {
                    response.AppName = appName;
                }
                else
                {
                    LogTrace("DabHealthCheck did not contain the app name in the HealthReport.");
                }
            }
        }

        // <summary>
        /// Logs a trace message if a logger is present and the logger is enabled for trace events.
        /// </summary>
        /// <param name="message">Message to emit.</param>
        private void LogTrace(string message)
        {
            if (_logger is not null && _logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace(message);
            }
        }
    }
}
