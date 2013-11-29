//
// Reaper.cs
//
// Copyright (c) 2013 SerialsSolutions Medialab B.V.
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
//

//#define SIMULATE_OUT_OF_ORDER

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
using PcapDotNet.Packets.Http;


namespace Gopher
{
   internal class LostSoul
   {
      public PacketCommunicator Communicator;
      public LivePacketDevice Device;
      public Thread Thread;
   }

   /// <summary>
   /// Manages captures of live traffic.
   /// </summary>
   internal static class Reaper
   {
      static Reaper()
      {
         _limbo = new List<LostSoul>();
      }

      public static void Reap(LivePacketDevice captureDevice, Int32 portNumber = 80)
      {
         if (!portNumber.Between(0, 0x10000, BetweenFlags.None))
            throw new ArgumentOutOfRangeException("portNumber");

         CapturePort = portNumber;
         // Construct a filter to only capture traffic to specified port and to the interface's IP address (incoming traffic).
         var entry = new LostSoul {
            Communicator = captureDevice.Open(0x10000, PacketDeviceOpenAttributes.MaximumResponsiveness | PacketDeviceOpenAttributes.Promiscuous, 1000),
            Device = captureDevice,
            Thread = new Thread((x) => {
               var t = Thread.CurrentThread;
               t.Name = "Reaper";
               var e = (LostSoul)x;
               Debug.Assert(e.Communicator.DataLink.Kind == DataLinkKind.Ethernet);
               e.Communicator.SetFilter(Reaper.ConstructFilter(e.Device, portNumber));
               try { e.Communicator.ReceivePackets(0, PsychoPomp); }
               catch (Exception) { }
            })
         };
         _limbo.Add(entry);
         entry.Thread.Start(entry);

         return;
      }

      public static void ReapAll(Int32 portNumber = 80)
      {
         foreach (var device in ActivePacketDevices)
            Reap(device, portNumber);
      }

      private static void Stop(LostSoul entry)
      {
         entry.Communicator.Break();
         Thread.Yield();
         entry.Thread.Join(Timeouts.EndThreadRelaySite * 2);
         if (entry.Thread.IsAlive)
            entry.Thread.Abort();
         entry.Communicator.Dispose();
      }

      public static void Stop(LivePacketDevice device)
      {
         LostSoul entry = null;
         try {
            entry = _limbo.First((x) => x.Device == device);
         }
         catch (InvalidOperationException) {
            entry = null;
         }

         if (entry != null)
            Stop(entry);
      }

      public static void StopAll()
      {
         foreach (var entry in _limbo)
            Stop(entry);
         _limbo.Clear();
      }

      public static void RefreshActivePacketDevices()
      {
         _activeDevices = LivePacketDevice.AllLocalMachine.FilterActiveDevices();
      }


      //////////////////////////////////////////////////////////////////////////
      // Packet capture functionality

#if SIMULATE_OUT_OF_ORDER
      private static Random grimFortune = new Random();

      private static void PsychoPomp(Packet packet)
      {

         Console.WriteLine("++++++++++++++++++++");

         // Simulate percentage of delayed incoming packet.
         var tcp = packet.GetTcpDatagram();
         if (grimFortune.NextDouble() < 0.25 && !tcp.IsSynchronize) {
            var delay = grimFortune.Next(25, 200);
            Console.WriteLine("DELAY: packet #{0} with {1} ms.", tcp.SequenceNumber, delay);
            PacketManager.ScheduleTimerAction(delay, (x) => PsychoPompImpl((Packet)x), packet);
            return;
         }

         PsychoPompImpl(packet);
      }
#endif

#if SIMULATE_OUT_OF_ORDER
      [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.Synchronized)]
      private static void PsychoPompImpl(Packet packet)
#else
      private static void PsychoPomp(Packet packet)
#endif
      {
         IpV4Datagram ip = packet.GetIpV4Datagram();
         TcpDatagram tcp = (ip != null) ? ip.Tcp : null;

         Debug.Assert(ip != null && tcp != null);

#if DBG_REAPER_TRACE_PACKETS
         Console.WriteLine("--------------------");
         Console.WriteLine(packet.Timestamp.ToString("yyyy-MM-dd hh:mm:ss.fff") + " length:" + packet.Length + " seq:#" + tcp.SequenceNumber);
         Console.WriteLine(ip.Source + ":" + tcp.SourcePort + " -> " + ip.Destination + ":" + tcp.DestinationPort + " (" + tcp.ControlBits + ")");
#endif

         var manager = PacketManager;
         Boolean isServerToClient = (tcp.SourcePort == CapturePort);

         if (manager == null)
            return;

         // instead of (client-to-server).IsSyn, we need to trigger client registration on receiving a syn+ack from server back to client => confirmation of connection establishment.
         if (isServerToClient && tcp.IsSynchronize && tcp.IsAcknowledgment) {
            // Since this is a server => client packet, source and destination are swapped, creating a client ident needs to take that into account.
            manager.RegisterClient(new NetTuple(packet, reverseDirection: true), tcp.AcknowledgmentNumber);
            // Server's acknowledgmentnumber is client's initialsequencenumber.
            return;
         }

         // Further processing is limited to client => server packets only.
         if (isServerToClient)
            return;

         // Only queue the packet for sending if there's a payload.
         if (tcp.PayloadLength != 0) {
            //manager.Enqueue(packet, tcp.IsPush || tcp.IsFin || tcp.IsReset);
            manager.Enqueue(packet);
         }

#if DBG_REAPER_TRACE_HTTP
         var http = tcp.Http;
         if (tcp.PayloadLength != 0 && http != null && http.IsRequest) {
            var httpReq = (HttpRequestDatagram)packet.Ethernet.IpV4.Tcp.Http;
            if (!String.IsNullOrEmpty(httpReq.Uri) && manager.IsClientRegistered(packet))
               Console.WriteLine(String.Format("REQ: {0}", httpReq.Uri).Truncate(79, "..."));
         }
#endif

         if (tcp.IsSynchronize)
            return;


         if (tcp.IsFin) {
            manager.DeregisterClient(packet);
            return;
         }

         // If we receive a reset, we drop everything and signal any 
         if (tcp.IsReset) {
#if DBG_REAPER_TRACE_PACKETS
            Console.WriteLine("\t(TCP_RST)");
#endif
            manager.DeregisterClient(packet);
         }

         if (tcp.IsPush) {
#if DBG_REAPER_TRACE_PACKETS
            Console.WriteLine("\t(TCP_PSH)");
#endif
         }

         return;
      }


      //////////////////////////////////////////////////////////////////////////

      private static String ConstructFilter(LivePacketDevice packetDevice, Int32 portNumber)
      {
         if (!portNumber.Between(0, 0x10000, BetweenFlags.None))
            throw new ArgumentOutOfRangeException("portNumber", "Port number invalid. Range: 0 < portNumber < 65536.");

         var itf = packetDevice.GetNetworkInterface();

         // Current filter:
         // (incoming) destination host is <server> and destination port is <port>; or
         // (outgoing) source host is <server> and source port is <port> and TCP_SYN is set.

         var listAddrs = (from addr in itf.GetIPProperties().UnicastAddresses
                          where (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                          select String.Format("((ip dst host {0} and tcp dst port {1}) or (ip src host {0} and tcp src port {1} and tcp[tcpflags] & tcp-syn != 0))", addr.Address.ToString(), portNumber)).ToList();
         var filterHost = listAddrs.Count > 0 ? "(" + String.Join(" or ", listAddrs) + ")" : String.Empty;

         var listFilter = new List<String>();
         listFilter.Add("ip and tcp");
         if (!String.IsNullOrEmpty(filterHost))
            listFilter.Add(filterHost);

         return String.Join(" and ", listFilter);
      }


      //////////////////////////////////////////////////////////////////////////

      public static Boolean IsAvailable { get { return (LivePacketDevice.AllLocalMachine.Count > 0); } }
      public static List<LostSoul> Limbo { get { return _limbo; } }
      public static List<LivePacketDevice> ActivePacketDevices { get { if (_activeDevices == null) RefreshActivePacketDevices(); return _activeDevices; } }
      public static PacketManager PacketManager { get; set; }
      public static Int32 CapturePort { get; private set; }


      //////////////////////////////////////////////////////////////////////////

      // Contains devices currently listening on
      private static List<LostSoul> _limbo;

      // Cached list of devices that (from a network point of view) are active.
      private static List<LivePacketDevice> _activeDevices;
   }
}

