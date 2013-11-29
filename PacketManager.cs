using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PcapDotNet.Core;
using PcapDotNet.Core.Extensions;
using PcapDotNet.Packets;
using PcapDotNet.Packets.IpV4;
using PcapDotNet.Packets.Transport;

using PacketList = System.Collections.Generic.LinkedList<PcapDotNet.Packets.Packet>;
using PacketListNode = System.Collections.Generic.LinkedListNode<PcapDotNet.Packets.Packet>;
using InboxTuple = System.Tuple<PcapDotNet.Packets.Packet, System.Boolean>;

namespace Gopher
{
   internal class ClientDataRecord
   {
      // Last date/time packet has been seen from this client.
      public DateTime LastSeen;
      //
      public UInt32 InitialSequenceNumber = 0;
      // Expected next sequence number, if an incoming packet does not match, it's put on the queue. 
      public UInt32 NextSequenceNumber;
      // Incoming packets with lower sequence numbers are to be discarded, unable to properly send.
      public UInt32 LastTransmittedSeqNumber;
   }

   /// <summary>
   /// The PacketManager maintains lists of sorted packets per tuple and ordered insertion of incoming packets.
   /// </summary>
   internal class PacketManager : IDisposable
   {
      public EventHandler<ClientConnectionEventArgs> ClientConnect;
      public EventHandler<ClientConnectionEventArgs> ClientDisconnect;
      public EventHandler<ClientSendPacketEventArgs> SendPackets;


      //////////////////////////////////////////////////////////////////////////

      public PacketManager(Boolean asyncQueue = true)
      {
         IsAsynchronous = asyncQueue;

         _clientPackets = new Dictionary<NetTuple, LinkedList<Packet>>();
         _clientDataRecords = new Dictionary<NetTuple, ClientDataRecord>();
         _clientClosed = new HashSet<NetTuple>();

         // Connect event to signal manager disposal.
         _shutdownEvent = new ManualResetEvent(false);

         // Connect thread and associated items to manage packet classification and queuing.
         if (asyncQueue) {
            _inbox = new Queue<InboxTuple>();

            _threadShuffler = new Thread(Shuffler);
            _shuffleEvent = new AutoResetEvent(false);
            _shuffledEvent = new ManualResetEvent(false);
            _threadShuffler.Start();
         }

         _threadOrphans = new Thread(Purgatory);
         _threadOrphans.Start();

      }


      //////////////////////////////////////////////////////////////////////////
      // Event handling

      private void Fire_SendPackets(Packet packet, List<Packet> packets)
      {
         if (SendPackets != null)
            Fire_SendPackets(new NetTuple(packet), packets);

      }
      private void Fire_SendPackets(NetTuple client, List<Packet> packets)
      {
#if DBG_TRACE_PACKETMANAGER_EVENTS
         Console.WriteLine("PacketManager.SendPackets(({0}:{1})", client.SourceAddress, client.SourcePort);
#endif
         try {
            if (SendPackets != null)
               SendPackets(null, new ClientSendPacketEventArgs() { Client = client, Packets = packets });
         }
         catch (Exception) {
         }
      }

      private void Fire_ClientConnect(Packet packet)
      {
         if (ClientConnect != null)
            Fire_ClientConnect(new NetTuple(packet));
      }
      private void Fire_ClientConnect(NetTuple client)
      {
#if DBG_TRACE_PACKETMANAGER_EVENTS
         Console.WriteLine("PacketManager.ClientConnect({0}:{1})", client.SourceAddress, client.SourcePort);
#endif
         try {
            if (ClientConnect != null)
               ClientConnect(null, new ClientConnectionEventArgs() { Client = client });
         }
         catch (Exception) { }
      }

      private void Fire_ClientDisconnect(Packet packet)
      {
         if (ClientDisconnect != null)
            Fire_ClientDisconnect(new NetTuple(packet));
      }
      private void Fire_ClientDisconnect(NetTuple client)
      {
#if DBG_TRACE_PACKETMANAGER_EVENTS
         Console.WriteLine("PacketManager.ClientDisconnect({0}:{1})", client.SourceAddress, client.SourcePort);
#endif
         try {
            if (ClientDisconnect != null)
               ClientDisconnect(null, new ClientConnectionEventArgs() { Client = client });
         }
         catch (Exception) { }
      }


      //////////////////////////////////////////////////////////////////////////

      public void DumpClients()
      {
         lock (_clientPackets) {
            foreach (var client in _clientPackets) {
               Console.WriteLine("Client: {0}:{1}", client.Key.SourceAddress.ToString(), client.Key.SourcePort);
            }
         }
      }

      public void RegisterClient(Packet packet, UInt32 initialSequenceNumber, UInt32 nextSequenceNumber = 0)
      {
         RegisterClient(new NetTuple(packet), initialSequenceNumber, nextSequenceNumber);
      }

      public void RegisterClient(NetTuple client, UInt32 initialSequenceNumber, UInt32 nextSequenceNumber = 0)
      {
         if (IsDisposed)
            throw new ObjectDisposedException(GetType().Name);


         lock (_clientPackets) {
            if (_clientPackets.ContainsKey(client))
               Console.WriteLine("PacketManager/RegisterClient(): Newly registered client is already managed by PacketManager! Caused by replay upstream or uncaught TCP_FIN?");
            _clientPackets[client] = new PacketList();
         }

         lock (_clientDataRecords)
            _clientDataRecords[client] = new ClientDataRecord() { InitialSequenceNumber = initialSequenceNumber, NextSequenceNumber = nextSequenceNumber };

         SetClientLastSeen(client, DateTime.Now);

         Fire_ClientConnect(client);
      }

      public void DeregisterClient(Packet packet)
      {
         DeregisterClient(new NetTuple(packet));
      }

      public void DeregisterClient(NetTuple client)
      {
         if (IsDisposed)
            throw new ObjectDisposedException(GetType().Name);


         Debug.Assert(client != null);

         lock (_clientPackets)
            if (!_clientPackets.ContainsKey(client))
               return;

         RunQueue(client, runAll: true);

         lock (_clientPackets)
            _clientPackets.Remove(client);

         lock (_clientDataRecords)
            _clientDataRecords.Remove(client);

         Fire_ClientDisconnect(client);

         lock (_clientClosed)
            _clientClosed.Add(client);

         Utils.ScheduleTimerAction(Timeouts.ClosedClientLinger, (x) => { lock (_clientClosed) _clientClosed.Remove((NetTuple)x); }, client);
      }

      public Boolean IsClientRegistered(Packet packet)
      {
         return IsClientRegistered(new NetTuple(packet));
      }
      public bool IsClientRegistered(NetTuple client)
      {
         lock (_clientPackets)
            return _clientPackets.ContainsKey(client);
      }

      public void Enqueue(Packet packet, Boolean sendImmediately = true)
      {
         if (IsDisposed)
            throw new ObjectDisposedException(GetType().Name);

         if (IsAsynchronous) {
            if (!IsRunning)
               throw new InvalidOperationException("PacketManager's Shuffler is not running!");
            lock (_inbox)
               _inbox.Enqueue(new InboxTuple(packet, sendImmediately));

            _shuffledEvent.Reset();
            _shuffleEvent.Set();
         } else {
            lock (_clientPackets)
               ShufflePacket(packet, sendImmediately);
         }

         return;
      }

      /// <summary>
      /// Dequeue packets for a specific client.
      /// </summary>
      /// <param name="client">The client to dequeue packets for.</param>
      /// <param name="barrier">If provided the dequeuing should not proceed past this packet (if in queue).</param>
      /// <param name="consecutiveOnly">If true, a partial dequeue may occur. Once a non-consecutive sequence is encountered, dequeuing is stopped.</param>
      /// <returns>The dequeued packets. Depending on parameters, this may or may not be equal to the complete queue of packets for the client.</returns>
      public List<Packet> Dequeue(NetTuple client, Packet barrier = null, Boolean consecutiveOnly = false, Boolean includeBarrier = true)
      {
         List<Packet> packets = null;

         lock (_clientPackets) {
            if (!_clientPackets.ContainsKey(client))
               return null;

            var list = _clientPackets[client];
            if (list.Count == 0)
               return null;

            // If the barrier is last item in queue and we should include barrier, no need to check for barrier.
            if (barrier != null && includeBarrier && barrier == list.Last.Value)
               barrier = null;

            // Equal to the entire queue
            if (barrier == null && consecutiveOnly == false) {
               packets = list.ToList();
               list.Clear();
            } else {
               // We need to proceed through the queue item by item.
               packets = new List<Packet>();
               packets.Capacity = list.Count;
               while (true) {
                  var node = list.First;

                  // barrier is not included!
                  if (barrier != null && !includeBarrier && barrier == node.Value)
                     break;

                  packets.Add(node.Value);
                  list.RemoveFirst();
                  // stop conditions: last item, barrier reached, consecutiveOnly true and next is not consecutive
                  if ((list.Count == 0))
                     break;
                  if (consecutiveOnly && node.Value.GetTcpDatagram().NextSequenceNumber != list.First.Value.GetTcpDatagram().SequenceNumber)
                     break;
                  if (barrier != null && includeBarrier && barrier == node.Value)
                     break;
               }
            }
         }

         return packets;
      }

      private void RunQueue(Packet packet, Packet barrier = null, Boolean runAll = false, Boolean includeBarrier = true)
      {
         RunQueue(new NetTuple(packet), barrier, runAll);

      }
      private void RunQueue(NetTuple client, Packet barrier = null, Boolean runAll = false, Boolean includeBarrier = true)
      {
         List<Packet> packets = null;
         Boolean resetNextSeq = false;
         UInt32 nextSeq = 0;

         lock (_clientPackets) {
            if (!_clientPackets.ContainsKey(client))
               return;

            packets = Dequeue(client, barrier, !runAll);

            // If the queue is empty, any packet is welcome again
            resetNextSeq = (_clientPackets[client].Count == 0);
            // The next sequence number is taken from the last packet in the queue, otherwise 0.
            nextSeq = (packets != null && packets.Count > 0) ? packets[packets.Count - 1].GetTcpDatagram().NextSequenceNumber : 0;

            // if there are still items on the queue, we'll need to schedule a deferred action to send them all up to the barrier, regardless if complete or not.
            if (_clientPackets[client].Count > 0) {
               Console.WriteLine("RunQueue(): missing packets from client {0}:{1}", client.SourceAddress, client.SourcePort);
               var delayedBarrier = _clientPackets[client].First.Value;
               Utils.ScheduleTimerAction(Timeouts.IncompleteSequence, (_) => RunQueue(client, delayedBarrier, runAll: true, includeBarrier: false), null);
            }
         }

         if (resetNextSeq)
            SetClientNextSequenceNumber(client, nextSeq);


         // Update the 'last transmitted sequence number' for the client. Any packets with a seq. number lower than this should be discarded.
         if (packets != null && packets.Count > 0)
            SetClientLastTransmittedSequenceNumber(client, packets[packets.Count - 1].GetTcpDatagram().SequenceNumber);

         // send packets
         if (packets != null && packets.Count > 0)
            Fire_SendPackets(client, packets);

         return;
      }


      //////////////////////////////////////////////////////////////////////////

      private void Purgatory()
      {
         Thread.CurrentThread.Name = MethodBase.GetCurrentMethod().Name;
         Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

         while (true) {
            if (_shutdownEvent.WaitOne(500))
               break;
            List<NetTuple> clientOrphans = null;
            lock (_clientDataRecords) {
               var now = DateTime.Now;
               clientOrphans = (from kvp in _clientDataRecords.Where((x) => (now - x.Value.LastSeen).TotalMilliseconds > Timeouts.ClientOrphanThreshold) select kvp.Key).ToList();
            }
            if (clientOrphans != null || clientOrphans.Count > 0)
               clientOrphans.ForEach((x) => DeregisterClient(x));
         }
      }

      private void Shuffler()
      {
         Thread.CurrentThread.Name = MethodBase.GetCurrentMethod().Name;
         Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;

         var handles = new WaitHandle[] { _shutdownEvent, _shuffleEvent };
         Int32 signal = 0;

         while (true) {
            signal = WaitHandle.WaitAny(handles);

            // Shutdown event
            if (signal == 0)
               break;

            // Just in case, should already be done in Enqueue().
            _shuffledEvent.Reset();

            lock (_inbox) {
               try {
                  foreach (var tuple in _inbox)
                     ShufflePacket(tuple.Item1, tuple.Item2);
               }
               catch (Exception) {
               }
               finally {
                  _inbox.Clear();
               }
            }
            _shuffledEvent.Set();
         }
      }

      // We should record what the client's next sequence number is going to be!
      private void ShufflePacket(Packet packet, Boolean sendImmediately = true)
      {
         var t = new NetTuple(packet);

         var ip = packet.GetIpV4Datagram();
         var tcp = ip != null ? ip.Tcp : null;

         // If the client is closed, discard packet
         if (_clientClosed.Contains(t)) {
            Console.WriteLine("WARNING: incoming packet is for closed connection {0}:{1}", t.SourceAddress, t.SourcePort);
            return;
         }

         // if we didn't trace a client from the get go, ignore its packets.
         lock (_clientPackets) {
            if (!_clientPackets.ContainsKey(t))
               return;
         }

         PacketList list = null;

         // If the packet's sequence number is lower than the last transmitted, discard it.
         UInt32 lastTransmitSeq = GetClientLastTransmittedSequenceNumber(t);
         if (lastTransmitSeq != 0 && tcp.SequenceNumber <= lastTransmitSeq) {
            Console.WriteLine("WARNING: incoming packet is past transmit window.");
            return;
         }

         lock (_clientPackets) {
            list = _clientPackets[t];

            PacketListNode predecessor = list.Last;
            while (predecessor != null) {
               // If the predecessor has a lower sequence number, we'll insert the new packet after it.
               if (predecessor.Value.GetTcpDatagram().SequenceNumber < tcp.SequenceNumber)
                  break;
               predecessor = predecessor.Previous;
            }

            if (predecessor != null)
               list.AddAfter(predecessor, packet);
            else
               list.AddLast(packet);
         }

         // out-of-order related
         UInt32 nextSeq = GetClientNextSequenceNumber(t);

         // No expectations yet concerning incoming packet
         if (nextSeq == 0)
            SetClientNextSequenceNumber(t, tcp.NextSequenceNumber);

         SetClientLastSeen(t, DateTime.Now);

         if (sendImmediately) {
            // If the packet does not match expected packet, queue should be run up the current packet (packet itself excluded!)
            Packet barrier = (nextSeq == 0 || nextSeq == tcp.SequenceNumber) ? null : packet;
            RunQueue(packet, barrier);
         }
#if DBG_TRACE_PACKETS
         Console.WriteLine("Shuffled packet #{0} (next = #{1}, transmit = #{2})", packet.GetIpV4Datagram().Tcp.SequenceNumber, GetClientNextSequenceNumber(t), GetClientLastTransmittedSequenceNumber(t));
#endif
      }


      //////////////////////////////////////////////////////////////////////////
      private void SetClientLastSeen(NetTuple client, DateTime when)
      {
         lock (_clientDataRecords)
            _clientDataRecords[client].LastSeen = when;
      }

      private UInt32 GetClientNextSequenceNumber(NetTuple client)
      {
         lock (_clientDataRecords)
            return _clientDataRecords[client].NextSequenceNumber;
      }
      private void SetClientNextSequenceNumber(NetTuple client, UInt32 sequenceNumber)
      {
         lock (_clientDataRecords)
            _clientDataRecords[client].NextSequenceNumber = sequenceNumber;
      }

      private UInt32 GetClientLastTransmittedSequenceNumber(NetTuple client)
      {
         lock (_clientDataRecords)
            return _clientDataRecords[client].LastTransmittedSeqNumber;
      }
      private void SetClientLastTransmittedSequenceNumber(NetTuple client, UInt32 sequenceNumber)
      {
         lock (_clientDataRecords)
            _clientDataRecords[client].LastTransmittedSeqNumber = sequenceNumber;
      }


      //////////////////////////////////////////////////////////////////////////

      public void Dispose()
      {
         if (IsDisposed)
            return;

         IsDisposed = true;

         _shutdownEvent.Set();
         Thread.Yield();

         if (_threadOrphans != null && _threadOrphans.IsAlive) {
            _threadOrphans.Join(Timeouts.EndThreadPacketManagerWorkers);
            if (_threadOrphans.IsAlive)
               _threadOrphans.Abort();
         }
         if (IsAsynchronous) {
            var threads = new Thread[] { _threadShuffler }.ToList();
            threads.ForEach(x => { x.Join(Timeouts.EndThreadPacketManagerWorkers); if (x.IsAlive) x.Abort(); });
            _inbox.Clear();
         }

         _clientPackets.Clear();

         IsDisposed = true;
      }


      //////////////////////////////////////////////////////////////////////////

      public ReadOnlyCollection<NetTuple> Clients { get { return new ReadOnlyCollection<NetTuple>(_clientPackets.Keys.ToList()); } }

      public Boolean IsAsynchronous { get; private set; }
      public Boolean IsDisposed { get; private set; }

      private Boolean IsRunning { get { return (_threadShuffler != null && _threadShuffler.IsAlive); } }
      private Boolean IsShutdown { get { return _shutdownEvent.WaitOne(0); } }



      //////////////////////////////////////////////////////////////////////////

      // Shuffler thread
      private Thread _threadShuffler;
      // Oprhaned client cleanup thread.
      private Thread _threadOrphans;
      // Signal shuffler work awaits
      private AutoResetEvent _shuffleEvent;
      // Signal waiters that shuffler has executed a run since last time a packet was queued.
      private ManualResetEvent _shuffledEvent;
      // Signal shuffler it should shutdown
      private ManualResetEvent _shutdownEvent;

      Dictionary<NetTuple, PacketList> _clientPackets;
      Dictionary<NetTuple, ClientDataRecord> _clientDataRecords;
      HashSet<NetTuple> _clientClosed;
      private Queue<InboxTuple> _inbox;
   }
}
