using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PcapDotNet.Core;
using PcapDotNet.Core.Extensions;
using PcapDotNet.Packets;
using PcapDotNet.Packets.IpV4;
using PcapDotNet.Packets.Transport;

namespace Gopher
{
   [Obsolete("Superseded by FiddlerCore implementation")]
   public static class RelayServer
   {
      static RelayServer()
      {
         _clientRelayRequests = new Dictionary<IPEndPoint, HttpWebRequest>();
      }

      public static void Start(IPEndPoint upstreamServer, Int32 listenPort)
      {
         if (upstreamServer == null || !upstreamServer.Validate())
            throw new ArgumentOutOfRangeException("upstreamServer");
         if (!listenPort.Between(0, 0x10000, BetweenFlags.None))
            throw new ArgumentOutOfRangeException("listenPort");

         _upstreamServer = upstreamServer;
         _listenPort = listenPort;

         // Build the URL on which HTTP requests should be monitored. (could use 
         String serverURL = String.Format("http://+:{0}/", listenPort);

         _listener = new HttpListener();
         _listener.Prefixes.Add(serverURL);

         try {
            _listener.Start();
         }
         catch (Exception e) {
            Console.WriteLine("*** Exception occurred during startup: {0}.", e.Message);
            Console.WriteLine("Please make sure the program is running with Administrative privileges and all listening ports are not in use by any other application.");
            _listener = null;
            throw;
         }

         if (_listener == null)
            return;

         ResetScannerStart(listenPort);
         _clientRelayRequests.Clear();

         _requestSemaphore = new Semaphore(ConcurrentRequests, ConcurrentRequests);
         _listener.BeginGetContext(Dispatcher, _listener);
      }

      public static void Stop()
      {
         if (_listener == null)
            return;

         ResetScannerStop();

         _listener.Stop();
         _listener = null;

         _clientRelayRequests.Clear();

         _requestSemaphore = null;
         _waitCount = 0;
      }

      private static void Dispatcher(IAsyncResult result)
      {
         try {
            HttpListener listener = (HttpListener)result.AsyncState;
            if (!listener.IsListening)
               return;
            ThreadPool.QueueUserWorkItem(RequestHandler, listener.EndGetContext(result));
            listener.BeginGetContext(Dispatcher, listener);
         }
         catch (Exception) { }
      }

      private static void RequestHandler(Object state)
      {
         var context = state as HttpListenerContext;
         var request = context.Request;
         var response = context.Response;

         Interlocked.Increment(ref _waitCount);
         _requestSemaphore.WaitOne();

         Interlocked.Decrement(ref _waitCount);
         try {
            response.StatusCode = (Int32)((_waitCount < WaitQueueSize) ? RelayRequest.ProcessRequest(request, response, _upstreamServer) : HttpStatusCode.ServiceUnavailable);
         }

         catch (Exception) {
            response.StatusCode = (Int32)HttpStatusCode.InternalServerError;
         }

         finally {
            _requestSemaphore.Release();
         }

         try {
            if (response.OutputStream.CanWrite)
               response.OutputStream.Close();
         }
         catch (Exception) { }
      }

      internal static void RegisterRelayRequest(IPEndPoint client, HttpWebRequest request)
      {
         if (client == null || request == null)
            return;

         lock (_clientRelayRequests) {
            if (!_clientRelayRequests.ContainsKey(client))
               _clientRelayRequests[client] = request;
         }
         return;
      }

      internal static void DeregisterRelayRequest(IPEndPoint client)
      {
         if (client == null)
            return;

         lock (_clientRelayRequests)
            _clientRelayRequests.Remove(client);
      }

      internal static HttpWebRequest GetRelayRequest(IPEndPoint client)
      {
         if (client == null)
            return null;

         lock (_clientRelayRequests)
            return _clientRelayRequests.ContainsKey(client) ? _clientRelayRequests[client] : null;
      }


      //////////////////////////////////////////////////////////////////////////

      private static void ResetScannerStart(Int32 portNumber)
      {
         if (_captureCommunicators != null)
            return;

         _captureCommunicators = new List<PacketCommunicator>();

         // Setup PacketCommunicators
         foreach (var device in LivePacketDevice.AllLocalMachine.FilterActiveDevices()) {
            var comm = device.Open(0x10000, PacketDeviceOpenAttributes.MaximumResponsiveness | PacketDeviceOpenAttributes.Promiscuous, 1000);
            if (comm != null && comm.DataLink.Kind == DataLinkKind.Ethernet) {
               _captureCommunicators.Add(comm);
               comm.SetFilter(ConstructFilter(device, portNumber));
            }
         }

         if (_captureCommunicators.Count == 0) {
            _captureCommunicators = null;
            return;
         }

         // Start packet capture.
         _captureCommunicators.ForEach((x) => ThreadPool.QueueUserWorkItem(_ => {
               try { x.ReceivePackets(0, (p) => {
                        IpV4Datagram ip = p.GetIpV4Datagram();
                        HttpWebRequest req = null;
                        var ep = new IPEndPoint(IPAddress.NetworkToHostOrder((Int32)ip.Source.ToValue()), ip.Tcp.SourcePort);
                        lock (_clientRelayRequests) req = _clientRelayRequests.ContainsKey(ep) ? _clientRelayRequests[ep] : null;
                        if (req != null) ThreadPool.QueueUserWorkItem((r) => { try { ((HttpWebRequest)r).Abort(); } catch (Exception) { } }, req);
                  });
               } catch (Exception) { }
            }));
         
      }

      private static void ResetScannerStop()
      {
         if (_captureCommunicators == null)
            return;
         _captureCommunicators.ForEach((x) => { x.Break(); Thread.Yield(); x.Dispose(); });
         _captureCommunicators = null;
      }


      //////////////////////////////////////////////////////////////////////////

      private static String ConstructFilter(LivePacketDevice packetDevice, Int32 portNumber)
      {
         if (!portNumber.Between(0, 0x10000, BetweenFlags.None))
            throw new ArgumentOutOfRangeException("portNumber", "Port number invalid. Range: 0 < portNumber < 65536.");

         var itf = packetDevice.GetNetworkInterface();

         // Filter:
         // ip and tcp; and
         // destination host is <server> and destination port is <port>; and
         // tcp-fin or tcp-rst

         // ((ip dst host {0} and tcp dst port {1}) or (ip src host {0} and tcp src port {1} and ))

         var listAddrs = (from addr in itf.GetIPProperties().UnicastAddresses
                          where (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                          select String.Format("ip dst host {0} and tcp dst port {1}", addr.Address.ToString(), portNumber)).ToList();
         var filterHost = listAddrs.Count > 0 ? "(" + String.Join(" or ", listAddrs) + ")" : String.Empty;

         var listFilter = new List<String>();
         listFilter.Add("ip and tcp");
         listFilter.Add("((tcp[tcpflags] & tcp-fin != 0) or (tcp[tcpflags] & tcp-rst != 0))");
         if (!String.IsNullOrEmpty(filterHost))
            listFilter.Add(filterHost);
         return String.Join(" and ", listFilter);
      }


      //////////////////////////////////////////////////////////////////////////

      private static Int32 ConcurrentRequests = 2048;
      private static Int32 WaitQueueSize = 2048;

      private static HttpListener _listener;
      private static Int32 _listenPort;
      private static IPEndPoint _upstreamServer;
      private static Int32 _waitCount = 0;
      private static Semaphore _requestSemaphore;

      private static Dictionary<IPEndPoint, HttpWebRequest> _clientRelayRequests;
      private static List<PacketCommunicator> _captureCommunicators;

   }
}
