using System;
using System.Threading.Tasks;

namespace opc_ae_relay.mq
{
    public interface IMqProducer : IDisposable
    {
        Task StartAsync();
        Task SendAsync(string topic, string message);
        Task StopAsync();
    }
}
