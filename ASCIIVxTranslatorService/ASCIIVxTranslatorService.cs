using System.ComponentModel;
using System.ServiceProcess;
using System.Threading;

namespace ASCIIVxTranslatorService
{
    public class ASCIIVxTranslatorService : ServiceBase
    {
        private readonly object _serviceLock = new object();
        private Thread _serviceThread;

        public ASCIIVxTranslatorService()
        {
            this.CanStop = true;
            this.ServiceName = "ASCIIVxTranslatorService";
        }

        protected override void OnStart(string[] args)
        {
            lock (this._serviceLock)
            {
                // Create and start the service thread
                this._serviceThread = new Thread(this.RunService);
                this._serviceThread.Start();
            }

            System.Diagnostics.Trace.WriteLine("Waiting for VxEventServer instances to initialize");
            int waitTime = 100 * 10 * 10; // 10 seconds
            while (!VxEventServer.VxEventServerManager.Instance.Initialized)
            {
                Thread.Sleep(100);
                waitTime = waitTime - 100;
                if (waitTime < 0)
                {
                    System.Diagnostics.Trace.WriteLine("VxEventServer instances failed to initialize");
                    break;
                }
                RequestAdditionalTime(100);
            }
        }

        protected override void OnStop()
        {
            Thread thread = null;

            lock (this._serviceLock)
            {
                // Keep a local reference of the thread then set it to null and pulse the lock
                // This will tell the thread to stop
                thread = this._serviceThread;
                this._serviceThread = null;
                Monitor.Pulse(this._serviceLock);
            }

            // If we told a thread to stop then join it to wait for it to exit
            if (thread != null)
                thread.Join();
        }

        private void RunService()
        {
            lock (_serviceLock)
            {
                using (VxEventServer.VxEventServerManager.Instance.Init())
                {
                    if (!VxEventServer.VxEventServerManager.Instance.Initialized)
                        return;

                    Monitor.Wait(_serviceLock);
                }
            }
        }
    }  
}
