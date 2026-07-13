using System;
using opcLearn.config;
using opcLearn.core;
using Serilog;

namespace YokogawaAE
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                Config.initAll();
                OpcAeClientRun.runOPC();
                OpcAeClientRun.StartOpcWatchDog();
                WebConfig.Start();
                WebConfig.WaitStop();
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}