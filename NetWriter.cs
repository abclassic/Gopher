using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PcapDotNet.Packets;

namespace Gopher
{
   internal class NetWriter : IDisposable
   {
      public NetWriter(PacketManager packetManager, IPEndPointCollection endpoints)
      {
         if (packetManager == null)
            throw new ArgumentNullException();
         _packetManager = packetManager;

         _endpoints = endpoints;

         _packetManager.ClientConnect += OnClientConnect;
         _packetManager.ClientDisconnect += OnClientDisconnect;
         _packetManager.SendPackets += OnSendPackets;

         _relaySites = new Dictionary<NetTuple, List<RelaySite>>();
      }

      public void Dispose()
      {
         if (_relaySites == null)
            return;

         _packetManager.ClientConnect -= OnClientConnect;
         _packetManager.ClientDisconnect -= OnClientDisconnect;
         _packetManager.SendPackets -= OnSendPackets;

         foreach (var t in _relaySites)
            foreach (var s in t.Value)
               s.Dispose();

         _relaySites.Clear();
         _relaySites = null;
      }


      //////////////////////////////////////////////////////////////////////////

      private void OnClientConnect(Object sender, ClientConnectionEventArgs eventArgs)
      {
         lock (_relaySites)
            _relaySites[eventArgs.Client] = _endpoints.Select<IPEndPoint, RelaySite>((x) => RelaySite.ConnectAsync(x.Address, x.Port)).Where(x => x != null).ToList();
      }

      private void OnClientDisconnect(Object sender, ClientConnectionEventArgs eventArgs)
      {
         List<RelaySite> sites = null;
         lock (_relaySites) {
            sites = _relaySites[eventArgs.Client];
            _relaySites.Remove(eventArgs.Client);
         }
         sites.ForEach(x => x.Dispose());
      }

      private void OnSendPackets(Object sender, ClientSendPacketEventArgs eventArgs)
      {
         // We do the actual sending on another thread (pool?)
         List<RelaySite> relays;

         lock (_relaySites)
            relays = _relaySites[eventArgs.Client];

         foreach (var relay in relays)
            relay.Enqueue(eventArgs.Packets);
      }

      private Dictionary<NetTuple, List<RelaySite>> _relaySites;
      private PacketManager _packetManager;
      private IPEndPointCollection _endpoints;
   }
}
