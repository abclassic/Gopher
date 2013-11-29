using Mono.Options;
using PcapDotNet.Core;
using PcapDotNet.Core.Extensions;
using PcapDotNet.Packets;
using PcapDotNet.Packets.IpV4;
using PcapDotNet.Packets.Transport;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Typedefs
using PacketList = System.Collections.Generic.HashSet<PcapDotNet.Packets.Packet>;

namespace Gopher
{
   public enum OperationMode
   {
      Capture,
      Relay,
   }


   class Program
   {

      //////////////////////////////////////////////////////////////////////////

      private static void ConfigureThreadPool()
      {
         Int32 iProcCount = Environment.ProcessorCount;
         Int32 iMinWorkerThreads = Math.Max(16, 6 * iProcCount);
         Int32 iMinIOThreads = iProcCount;
         ThreadPool.SetMinThreads(iMinWorkerThreads, iMinIOThreads);
      }

      private static void ConfigureServicePointManager()
      {
         // Make sure we'll be able to set up enough outgoing connections.
         ServicePointManager.DefaultConnectionLimit = Int32.MaxValue;
         ServicePointManager.UseNagleAlgorithm = false;
      }

      static void Main(string[] args)
      {
         Boolean showHelp = false;
         List<String> relays = new List<String>();

         ConfigureThreadPool();
         ConfigureServicePointManager();

         if (!Reaper.IsAvailable) {
            Console.WriteLine("No interfaces found. Make sure WinPcap is installed and the NPF driver/service is running.");
            return;
         }

         //////////////////////////////////////////////////////////////////////////

         OptionSet p = new OptionSet()
            .Add("m|mode=", "Operation mode, either 'capture' or 'relay'.", option => { if (!Enum.TryParse(option, true, out OperationMode)) OperationMode = OperationMode.Capture; })
            .Add("c", "Capture mode, shortcut for --mode=capture", option => OperationMode = OperationMode.Capture)
            .Add("r", "Relay mode, shortcut for --mode=relay", option => OperationMode = OperationMode.Relay)
            .Add("u|upstream=", "Add upstream server, format is <host>[:port].", option => Upstream.Add(Utils.ResolveEndPoint(option)))
            .Add("p|port=", "The capture/relay listening port, default = 80.", option => MonitorPort = Int32.Parse(option))
            .Add("h|?", "Show usage.", _ => showHelp = true);

         try {
            p.Parse(args);
         }

         catch (OptionException) {
            p.WriteOptionDescriptions(Console.Out);
         }

         if (showHelp) {
            Console.WriteLine();
            Console.WriteLine("Capture mode.");
            Console.WriteLine("Incoming packets to the local webserver are intercepted and distributed to the specified upstream server(s). It is possible to specify multiple upstream servers by repeating the appropriate option for each server you would like to add.");
            Console.WriteLine();
            Console.WriteLine("Relay mode.");
            Console.WriteLine("The application is listening for replicated requests from a downstream instance running in capture mode. Since the relay then establishes a meaningful two-way connection with the upstream HTTP server, it does not make sense to specify more than one upstream server (yet). Future versions may allow for multiple upstream servers where then an upstream server can be yet another instance running in relay mode.");
            Console.WriteLine();
            Console.WriteLine("NOTES:");
            Console.WriteLine("It is not yet possible to bind/listen only to a specific IP adress on multihomed servers, capture more will collect any incoming packet regardless of destination IP and in relay mode the application will bind with 0.0.0.0.");
            Console.WriteLine("Please note that application is not (yet) IPv6 capable, it is explicitly limited to IPv4 only.");
            Console.WriteLine();
            p.WriteOptionDescriptions(Console.Out);

            return;
         }

         if (args.Length == 0) {
            Console.WriteLine("Call application with either -h or -? to show available options.");
            Console.WriteLine();
         }

         if (OperationMode == OperationMode.Relay && Upstream.Count == 0) {
            Console.WriteLine("ERROR: no upstream servers configured or failed to resolve.");
            return;
         }

         //////////////////////////////////////////////////////////////////////////

         Console.WriteLine("Run Settings:");
         Console.WriteLine(" Operation Mode . . . : {0}", OperationMode);
         Console.WriteLine(" Port Monitored . . . : {0}", MonitorPort);
         if (Upstream.Count > 0) {
            Console.Write(" Upstream Servers . . : ");
            foreach (var upstream in Upstream) { Console.WriteLine("{0}:{1}", upstream.Address, upstream.Port); Console.Write("                        "); }
            Console.Write("\r");
         }
         Console.WriteLine(" Hostname . . . . . . : {0}", Environment.MachineName);

         //////////////////////////////////////////////////////////////////////////

         Console.WriteLine();

         switch (OperationMode) {
            case OperationMode.Capture:
               RunCapture();
               break;
            case OperationMode.Relay:
               RunRelay();
               break;
            default:
               break;
         }

         return;
      }

      private static void RunRelay()
      {
         var relay = Upstream[0];

         ClientContext.RegisterOverride(ContextOverrideType.QueryString, "rctx", "[\"]rctx['\"]:['\"](.*?)['\"]", (s, c) => c.GetCookie("ASP.NET_SessionId"));


         Console.WriteLine("Relaying requests to http://{0}:{1}/", relay.Address, relay.Port);
         Fiddle.Start(relay);

         Console.WriteLine();
         Console.WriteLine("Press ENTER to quit.");
         Console.WriteLine();
         while (Console.ReadKey(intercept: true).Key != ConsoleKey.Enter) ;


         Console.WriteLine("Shutting down...");
         Fiddle.Stop();
      }


      private static void RunCapture()
      {

         Manager = new PacketManager(false);
         var writer = new NetWriter(Manager, Upstream);
         Reaper.PacketManager = Manager;

         foreach (var device in Reaper.ActivePacketDevices) {
            Console.WriteLine("Listening on: {0}", device.GetNetworkInterface().Name);
            Reaper.Reap(device, MonitorPort);
         }

         Console.WriteLine();
         Console.WriteLine("Press ENTER to quit.");
         Console.WriteLine();
         while (Console.ReadKey(intercept: true).Key != ConsoleKey.Enter) ;

         Console.WriteLine("Shutting down...");

         Reaper.StopAll();
         Reaper.PacketManager = null;

#if DEBUG
         Manager.DumpClients();
#endif

         writer.Dispose();
         Manager.Dispose();
      }

      //////////////////////////////////////////////////////////////////////////

      public static OperationMode OperationMode;
      public static Int32 MonitorPort = 80;
      public static IPEndPointCollection Upstream = new IPEndPointCollection();

      private static PacketManager Manager;

   }
}
