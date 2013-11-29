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
   internal class RelaySite : IDisposable
   {
      private RelaySite()
      {
         _sendQueue = new Queue<List<Packet>>();
         _sendEvent = new ManualResetEvent(false);
         _shutdownEvent = new ManualResetEvent(false);
         _sender = new Thread(QueueProcessor);
         _sender.Start();
      }

      public static RelaySite Connect(IPEndPoint endPoint)
      {
         return (endPoint != null) ? Connect(endPoint.Address, endPoint.Port) : null;
      }
      public static RelaySite Connect(IPAddress address, Int32 port)
      {
         var s = new Socket(System.Net.Sockets.AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
         s.Connect(new IPEndPoint(address, port));
         if (!s.Connected)
            return null;
         var n = new NetworkStream(s, false);
         return new RelaySite() { Socket = s, Stream = n };
      }

      public static RelaySite ConnectAsync(IPEndPoint endPoint)
      {
         return (endPoint != null) ? ConnectAsync(endPoint.Address, endPoint.Port) : null;
      }
      public static RelaySite ConnectAsync(IPAddress address, Int32 port)
      {
         // Async version always returns an object, except if arguments are incorrect
         if (address == null || !port.Between(1, 0xFFFF))
            return null;

         var s = new Socket(System.Net.Sockets.AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
         var rs = new RelaySite() { Socket = s };

         var args = new SocketAsyncEventArgs() { RemoteEndPoint = new IPEndPoint(address, port), UserToken = rs };
         args.Completed += (o, x) => {
            RelaySite site = (RelaySite)x.UserToken;
            if (x.SocketError == SocketError.Success)
               site.Stream = new NetworkStream(s, false);
            else
               site.Socket = null;
         };
         var state = s.ConnectAsync(args);
         if (state == false)
            return (args.SocketError == SocketError.Success) ? rs : null;

         return rs;
      }

      public void Enqueue(List<Packet> packets)
      {
         if (IsDisposed)
            throw new ObjectDisposedException(GetType().Name);

         // Zombie: don't bother
         if (IsZombie)
            return;

         if (packets == null || packets.Count == 0)
            return;
         lock (_sendQueue)
            _sendQueue.Enqueue(packets);
         _sendEvent.Set();
      }

      public void Close()
      {
         this.Dispose();
      }

      public void Dispose()
      {
         if (IsDisposed)
            return;

         IsDisposed = true;
         
         // Shutdown send worker
         _shutdownEvent.Set();
         if (_sender.IsAlive)
            _sender.Join(Timeouts.EndThreadRelaySite);

         if (_sender.IsAlive)
            _sender.Abort();

         _sendQueue.Clear();
         _sendEvent.Close();
         _shutdownEvent.Close();
         _sender = null;

         ThreadPool.QueueUserWorkItem(_ => Disconnect());
      }

      private void Disconnect()
      {
         var s = Socket;
         var n = Stream;

         Socket = null;
         Stream = null;
         try {
            if (n != null && n.CanWrite)
               n.Dispose();

            if (s != null && s.Connected) {
               s.Shutdown(SocketShutdown.Both);
               s.Close();
            }
         }
         catch (Exception) { }
      }

      //////////////////////////////////////////////////////////////////////////

      private void QueueProcessor()
      {
         Thread.CurrentThread.Name = "RelaySite";

         var waitHandles = new WaitHandle[] { _shutdownEvent, _sendEvent };
         int signal = 0;

         List<List<Packet>> packetList = null;
         do {
            signal = WaitHandle.WaitAny(waitHandles);

            // Zombie state, no need to continue with the thread.
            if (IsZombie)
               break;

            // If we're not connected, or no stream yet, sleep a while: it may be that we're busy with an async connect attempt.
            if (!IsConnected || !IsWritable) {
               Thread.Sleep(Timeouts.RelaySiteConnectivityCheckDelay);
               continue;
            }

            if (signal == 1) {
               lock (_sendQueue) {
                  _sendEvent.Reset();
                  packetList = _sendQueue.ToList();
                  _sendQueue.Clear();
               }

               foreach (var lst in packetList)
                  lst.ForEach(x => SendPacket(x));
            }

            // Shutdown set.
            if (signal == 0)
               break;

         } while (true);
      }

      private void SendPacket(Packet packet)
      {
         try {
            if (packet != null && Stream.CanWrite) {
               var tcp = packet.GetTcpDatagram();
               Stream.Write(tcp.Payload.ToArray(), 0, tcp.PayloadLength);
               if (tcp.PayloadLength == 0) {
                  Console.WriteLine();
                  Console.WriteLine("!!!!!!!!! how the hell did this happen? !!!!!!!");
                  Console.WriteLine();
               }
            }

         }
         catch (Exception) {
            Disconnect();
         }
      }


      //////////////////////////////////////////////////////////////////////////

      public Socket Socket { get; private set; }
      public NetworkStream Stream { get; private set; }
      public Boolean IsDisposed { get; private set; }

      public Boolean IsConnected { get { return (Socket != null && Socket.Connected); } }
      public Boolean IsWritable { get { return (Stream != null && Stream.CanWrite); } }

      private Boolean IsZombie { get { return (Socket == null && Stream == null); } }

      private Queue<List<Packet>> _sendQueue;
      private Thread _sender;
      private ManualResetEvent _sendEvent;
      private ManualResetEvent _shutdownEvent;
   }
}