﻿using ErpNet.FP.Core.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;

namespace ErpNet.FP.Server
{
    public class Service
    {
        private static readonly string DebugLogFileName = @"debug.log";

        private static IEnumerable<IPAddress> GetLocalV4Addresses()
        {
            return from iface in NetworkInterface.GetAllNetworkInterfaces()
                   where iface.OperationalStatus == OperationalStatus.Up
                   from address in iface.GetIPProperties().UnicastAddresses
                   where address.Address.AddressFamily == AddressFamily.InterNetwork
                   select address.Address;
        }

        public static string GetVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return (version != null) ? version.ToString() : "unknown";
        }

        public static void Main(string[] args)
        {
            var pathToContentRoot = Directory.GetCurrentDirectory();

            if (!(Debugger.IsAttached))
            {
                var location = Assembly.GetExecutingAssembly().Location;
                pathToContentRoot = Path.GetDirectoryName(location) ?? pathToContentRoot;
                Directory.SetCurrentDirectory(pathToContentRoot);
            }

            EnsureAppSettingsJson(pathToContentRoot);

            // Setup debug logs
            try
            {
                var builder = CreateHostBuilder(
                pathToContentRoot,
                args.Where(arg => arg != "--console").ToArray());

                var host = builder.Build();

                var logStream = new FileStream(
                        EnsureDebugLogHistory(pathToContentRoot),
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Read);

                if (Debugger.IsAttached)
                {
                    Log.Setup(host.Services.GetRequiredService<ILogger<Service>>());

                    // Create a TextWriterTraceListener object that takes a stream.
                    TextWriterTraceListener textListener;
                    textListener = new TextWriterTraceListener(logStream);
                    Trace.Listeners.Add(textListener);
                    Trace.AutoFlush = true;
                } 
                else
                {
                    Log.Setup(new StreamWriter(logStream));
                }

                Log.Information($"Starting the service, version {GetVersion()}...");

                host.Run();

                Log.Information("Stopping the service.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while creating debug.log file: {ex.Message}");
                return;
            }
        }

        public static IHostBuilder CreateHostBuilder(string pathToContentRoot, string[] args) =>
            Host.CreateDefaultBuilder(args)
#if Windows
            .UseWindowsService()
#endif
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                var env = hostingContext.HostingEnvironment;
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder
                .UseKestrel()
                .UseContentRoot(pathToContentRoot)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                })
                .ConfigureKestrel((hostingContext, options) =>
                {
                    options.Configure(hostingContext.Configuration.GetSection("Kestrel"));

                    // Overriding some of the config values 
                    options.AllowSynchronousIO = false;
                    options.Limits.MaxRequestBodySize = 500 * 1024;
                })
                .ConfigureLogging((hostingContext, logging) =>
                {
                    logging
                        .AddConfiguration(hostingContext.Configuration.GetSection("Logging"))
                        .AddDebug()
                        .AddEventSourceLogger();
                })
                .UseStartup<Startup>();
            });


        public static string EnsureDebugLogHistory(string pathToContentRoot)
        {
            var debugLogFolder = Path.Combine(pathToContentRoot, "wwwroot", "debug");
            var debugLogFilePath = Path.Combine(debugLogFolder, DebugLogFileName);
            Directory.CreateDirectory(debugLogFolder);
            if (File.Exists(debugLogFilePath))
            {
                for (var i = 9; i > 1; i--)
                {
                    if (File.Exists($"{debugLogFilePath}.{i - 1}.zip"))
                    {
                        File.Move($"{debugLogFilePath}.{i - 1}.zip", $"{debugLogFilePath}.{i}.zip", true);
                    }
                }
                // Zip the file
                using (var zip = ZipFile.Open($"{debugLogFilePath}.1.zip", ZipArchiveMode.Create))
                    zip.CreateEntryFromFile(debugLogFilePath, DebugLogFileName);
            }
            return debugLogFilePath;
        }

        public static void EnsureAppSettingsJson(string pathToContentRoot)
        {
            var appSettingsJsonFilePath = Path.Combine(pathToContentRoot, "appsettings.json");

            if (!File.Exists(appSettingsJsonFilePath))
            {
                var appSettingsDevelopmentJsonFilePath = Path.Combine(pathToContentRoot, "appsettings.Development.json");
                File.Copy(appSettingsDevelopmentJsonFilePath, appSettingsJsonFilePath);
            }
        }
    }
}