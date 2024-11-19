// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;

namespace Azure.DataApiBuilder.Service
{
    public class Program
    {
        public static bool IsHttpsRedirectionDisabled { get; private set; }

        public static void Main(string[] args)
        {
            if (!StartEngine(args))
            {
                Environment.ExitCode = -1;
            }
        }

        public static bool StartEngine(string[] args)
        {
            // Unable to use ILogger because this code is invoked before LoggerFactory
            // is instantiated.
            Console.WriteLine("Starting the runtime engine...");
           try
            {
                IHost host = CreateHostBuilder(args).Build();

                // Access the host's configuration and environment information
                IConfiguration config = host.Services.GetRequiredService<IConfiguration>();
                IWebHost? webHost = host.Services.GetService<IWebHost>();

                // Initialize dictionary for values
                Dictionary<string, string?> values = new()
                {
                    { "Dab Host Mode", config["Runtime:Host:Mode"] ?? "N/A" },
                    { "Environment", config[HostDefaults.EnvironmentKey] ?? "N/A" },
                    { "HTTP Link", "N/A" },
                    { "HTTPS Link", "N/A" }
                };

                // Access server addresses
                IServer? server = webHost?.Services.GetService<IServer>();
                if (server is KestrelServer kestrelServer)
                {
                    var addresses = kestrelServer.Options.ListenOptions.Select(o => o.ToString());
                    values["HTTP Link"] = addresses.FirstOrDefault(addr => addr.StartsWith("http://")) ?? "N/A";
                    values["HTTPS Link"] = addresses.FirstOrDefault(addr => addr.StartsWith("https://")) ?? "N/A";
                }

                // Output the dictionary as a table
                Console.Out.WriteLine();
                Console.Out.WriteLine("{0,-20} | {1}", "Name", "Value");
                Console.Out.WriteLine(new string('-', 50));
                foreach (var entry in values)
                {
                    Console.Out.WriteLine("{0,-20} | {1}", entry.Key, entry.Value);
                }
                Console.Out.WriteLine();

                host.Run();
                return true;
            }

            // Catch exception raised by explicit call to IHostApplicationLifetime.StopApplication()
            catch (TaskCanceledException)
            {
                // Do not log the exception here because exceptions raised during startup
                // are already automatically written to the console.
                Console.Error.WriteLine("Unable to launch the Data API builder engine.");
                return false;
            }
            // Catch all remaining unhandled exceptions which may be due to server host operation.
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unable to launch the runtime due to: {ex}");
                return false;
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(builder =>
                {
                    AddConfigurationProviders(builder, args);
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    Startup.MinimumLogLevel = GetLogLevelFromCommandLineArgs(args, out Startup.IsLogLevelOverriddenByCli);
                    ILoggerFactory? loggerFactory = GetLoggerFactoryForLogLevel(Startup.MinimumLogLevel);
                    ILogger<Startup>? startupLogger = loggerFactory.CreateLogger<Startup>();
                    DisableHttpsRedirectionIfNeeded(args);
                    webBuilder.UseStartup(builder => new Startup(builder.Configuration, startupLogger));
                });
        }

        /// <summary>
        /// Using System.CommandLine Parser to parse args and return
        /// the correct log level. We save if there is a log level in args through
        /// the out param. For log level out of range we throw an exception.
        /// </summary>
        /// <param name="args">array that may contain log level information.</param>
        /// <param name="isLogLevelOverridenByCli">sets if log level is found in the args.</param>
        /// <returns>Appropriate log level.</returns>
        private static LogLevel GetLogLevelFromCommandLineArgs(string[] args, out bool isLogLevelOverridenByCli)
        {
            Command cmd = new(name: "start");
            Option<LogLevel> logLevelOption = new(name: "--LogLevel");
            cmd.AddOption(logLevelOption);
            ParseResult result = GetParseResult(cmd, args);
            bool matchedToken = result.Tokens.Count - result.UnmatchedTokens.Count - result.UnparsedTokens.Count > 1;
            LogLevel logLevel = matchedToken ? result.GetValueForOption<LogLevel>(logLevelOption) : LogLevel.Error;
            isLogLevelOverridenByCli = matchedToken ? true : false;

            if (logLevel is > LogLevel.None or < LogLevel.Trace)
            {
                throw new DataApiBuilderException(
                    message: $"LogLevel's valid range is 0 to 6, your value: {logLevel}, see: " +
                    $"https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.loglevel?view=dotnet-plat-ext-6.0",
                    statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            return logLevel;
        }

        /// <summary>
        /// Helper function returns ParseResult for a given command and
        /// arguments.
        /// </summary>
        /// <param name="cmd">The command.</param>
        /// <param name="args">The arguments.</param>
        /// <returns>ParsedResult</returns>
        private static ParseResult GetParseResult(Command cmd, string[] args)
        {
            CommandLineConfiguration cmdConfig = new(cmd);
            System.CommandLine.Parsing.Parser parser = new(cmdConfig);
            return parser.Parse(args);
        }

        /// <summary>
        /// Creates a LoggerFactory and add filter with the given LogLevel.
        /// </summary>
        /// <param name="logLevel">minimum log level.</param>
        /// <param name="appTelemetryClient">Telemetry client</param>
        public static ILoggerFactory GetLoggerFactoryForLogLevel(LogLevel logLevel, TelemetryClient? appTelemetryClient = null)
        {
            return LoggerFactory
                .Create(builder =>
                {
                    // Category defines the namespace we will log from,
                    // including all sub-domains. ie: "Azure" includes
                    // "Azure.DataApiBuilder.Service"
                    builder.AddFilter(category: "Microsoft", logLevel);
                    builder.AddFilter(category: "Azure", logLevel);
                    builder.AddFilter(category: "Default", logLevel);

                    // For Sending all the ILogger logs to Application Insights
                    if (Startup.AppInsightsOptions.Enabled && !string.IsNullOrWhiteSpace(Startup.AppInsightsOptions.ConnectionString))
                    {
                        builder.AddApplicationInsights(configureTelemetryConfiguration: (config) =>
                            {
                                config.ConnectionString = Startup.AppInsightsOptions.ConnectionString;
                                if (Startup.CustomTelemetryChannel is not null)
                                {
                                    config.TelemetryChannel = Startup.CustomTelemetryChannel;
                                }
                            },
                            configureApplicationInsightsLoggerOptions: (options) => { }
                        )
                        .AddFilter<ApplicationInsightsLoggerProvider>(category: string.Empty, logLevel);
                    }

                    builder.AddConsole();
                });
        }

        /// <summary>
        /// Use CommandLine parser to check for the flag `--no-https-redirect`.
        /// If it is present, https redirection is disabled.
        /// By Default it is enabled.
        /// </summary>
        /// <param name="args">array that may contain flag to disable https redirection.</param>
        private static void DisableHttpsRedirectionIfNeeded(string[] args)
        {
            Command cmd = new(name: "start");
            Option<string> httpsRedirectFlagOption = new(name: Startup.NO_HTTPS_REDIRECT_FLAG);
            cmd.AddOption(httpsRedirectFlagOption);
            ParseResult result = GetParseResult(cmd, args);
            if (result.Tokens.Count - result.UnmatchedTokens.Count - result.UnparsedTokens.Count > 0)
            {
                Console.WriteLine("Redirecting to https is disabled.");
                IsHttpsRedirectionDisabled = true;
                return;
            }

            IsHttpsRedirectionDisabled = false;
        }

        // This is used for testing purposes only. The test web server takes in a
        // IWebHostBuilder, instead of a IHostBuilder.
        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, builder) =>
            {
                AddConfigurationProviders(builder, args);
                DisableHttpsRedirectionIfNeeded(args);
            })
            .UseStartup<Startup>();

        // This is used for testing purposes only. The test web server takes in a
        // IWebHostBuilder, instead of a IHostBuilder.
        public static IWebHostBuilder CreateWebHostFromInMemoryUpdateableConfBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
            .UseStartup<Startup>();

        /// <summary>
        /// Adds the various configuration providers.
        /// </summary>
        /// <param name="configurationBuilder">The configuration builder.</param>
        /// <param name="args">The command line arguments.</param>
        private static void AddConfigurationProviders(
            IConfigurationBuilder configurationBuilder,
            string[] args)
        {
            configurationBuilder
                .AddEnvironmentVariables(prefix: FileSystemRuntimeConfigLoader.ENVIRONMENT_PREFIX)
                .AddCommandLine(args);
        }
    }
}
