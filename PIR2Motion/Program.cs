using Iot.Device.Hcsr501;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Device.Gpio;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace PIR2Motion
{
    class Program
    {       
       
        public static async Task Main(string[] args)
        {
            var builder = new HostBuilder()
               .ConfigureHostConfiguration(configHost =>
                {
                    configHost.AddEnvironmentVariables(prefix: "ASPNETCORE_");
                    configHost.AddCommandLine(args);
               })
              .ConfigureAppConfiguration((hostingContext, config) =>
              {
                  config.AddJsonFile("appsettings.json", optional: true)
                   .AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true); // optional extra provider;

                  config.AddEnvironmentVariables();

                  if (args != null)
                  {
                      config.AddCommandLine(args);
                  }
              })
              .ConfigureServices((hostContext, services) =>
              {
                  services.Configure<MotionDetectionConfig>(hostContext.Configuration.GetSection("MotionDetection"));

                  //https://github.com/aspnet/Extensions/issues/553#issuecomment-505538902
                  services.AddSingleton<IPIRService, PIRService>();
                  services.AddSingleton<IHostedService>(p => p.GetService<IPIRService>());
                  services.AddSingleton<IHostedService, ProcessMan>();
              })
              .ConfigureLogging((hostingContext, logging) => {
                  logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                  logging.AddConsole();
              });
            
            await builder.RunConsoleAsync();
        }
    }
   

    public interface IPIRService : IHostedService
    {
        event EventHandler MotionAlert;
        event EventHandler MotionStop;
    }

    public class MotionDetectionConfig
    {
        public string SaveFolder { get; set; } 
    }

    internal class ProcessMan : IHostedService, IDisposable
    {
        private readonly ILogger logger;
        private Process proc;
        private readonly IPIRService pirsvc;
        private bool stop;
        private readonly IOptions<MotionDetectionConfig> mdCfg;

        public ProcessMan(ILogger<ProcessMan> logger,
                          IPIRService pirsvc,
                          IOptions<MotionDetectionConfig> mdCfg)
        {
            this.logger = logger;
            this.pirsvc = pirsvc;
            this.mdCfg = mdCfg;
        }

        public void Dispose()
        {
            proc?.Dispose();
            proc = null;
            pirsvc.MotionAlert -= Pirsvc_MotionAlert;
            pirsvc.MotionStop -= Pirsvc_MotionStop;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            pirsvc.MotionAlert += Pirsvc_MotionAlert;
            pirsvc.MotionStop += Pirsvc_MotionStop;
            stop = false;
            await Task.Run(() => MotionDetectionProc(dryrun:true));
            this.logger.LogInformation(this.GetType().Name + " ready");
        }

        private async void Pirsvc_MotionAlert(object sender, EventArgs e)
        {
            stop = false;
            logger.LogInformation($"Motion registered");
            await Task.Run(()=>MotionDetectionProc());                      
        }

        private void MotionDetectionProc(bool dryrun=false)
        {
            try
            {
                while (!stop)
                {
                    string filename = $"v{DateTime.Now.ToString("yyyyMMddTHHmmss")}.mkv";
                    string localPath = Path.Combine(mdCfg.Value.SaveFolder, filename);
                    ProcessStartInfo piRaspiVid = new ProcessStartInfo("/bin/sh", $"motionalert.sh {localPath} {filename}")
                    { UseShellExecute = false, RedirectStandardOutput = true };
                    logger.LogInformation($"{nameof(piRaspiVid)}: {piRaspiVid.FileName} {piRaspiVid.Arguments}");
                    proc = Process.Start(piRaspiVid);
                    logger.LogInformation($"{nameof(piRaspiVid)}: {proc.StandardOutput.ReadToEnd()}");
                    proc.WaitForExit();
                    
                    IEnumerable<FileInfo> fiToDelete = new DirectoryInfo(mdCfg.Value.SaveFolder).EnumerateFiles("*.mkv");
                    if (!dryrun)
                        fiToDelete = fiToDelete.Where(fi => fi.CreationTime < DateTime.Now.AddDays(-7));
                    else
                    {
                        fiToDelete = fiToDelete.Where(fi => fi.Name == filename);
                        stop = true;
                    }
                    foreach (FileInfo fi in fiToDelete)
                    {
                        fi.Delete();
                        logger.LogInformation($"File {fi.FullName} deleted");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in motion alert loop");
                if (dryrun)
                    throw;
            }
        }

        private void Pirsvc_MotionStop(object sender, EventArgs e)
        {
            logger.LogInformation($"Stopping motion triggered recording");
            stop = true;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return Task.CompletedTask;
        }

        
    }

    class PIRService : IPIRService
    {
        static readonly int PIR_PIN = 4;
        Hcsr501 sensor;
        private readonly ILogger logger;

        public PIRService(ILogger<PIRService> logger)
        {
            this.logger = logger;
        }

        public void Dispose()
        {
            sensor?.Dispose();
            sensor = null;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            sensor = new Hcsr501(PIR_PIN, PinNumberingScheme.Logical);
            sensor.Hcsr501ValueChanged += Sensor_Hcsr501ValueChanged;
            this.logger.LogInformation(this.GetType().Name + " ready");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return Task.CompletedTask;
        }

        public event EventHandler MotionAlert;
        public event EventHandler MotionStop;

        private void Sensor_Hcsr501ValueChanged(object sender, Hcsr501ValueChangedEventArgs e)
        {
            if (e.PinValue == PinValue.High)
            {
                this.logger.LogInformation("PIR: motion detected!");
                MotionAlert?.Invoke(this, new EventArgs());
            }
            else
            {
                this.logger.LogInformation("PIR: motion ended");
                MotionStop?.Invoke(this, new EventArgs());
            }
        }
    }
}
