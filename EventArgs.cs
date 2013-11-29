using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using PcapDotNet.Packets;

namespace Gopher
{
   public class ClientConnectionEventArgs : EventArgs
   {
      public NetTuple Client;
   }

   public class ClientSendPacketEventArgs : EventArgs
   {
      public NetTuple Client;
      public List<Packet> Packets;
   }

   public class RelayRequestUrlArgs : EventArgs
   {
      public IPEndPoint Server { get; internal set; }
      public String Path { get; internal set; }
      public String QueryString { get; internal set; }
      public String RequestUrl { get; set; }
   }

   public class RelayRequestPreRequestEventArgs : EventArgs
   {
      public HttpListenerRequest Origin;
      public HttpWebRequest Request;
   }

   public class RelayRequestPostRequestEventArgs : EventArgs
   {
      public HttpListenerRequest Origin;
      public HttpWebRequest Request;
      public HttpWebResponse Response;
      public HttpListenerResponse Destination;
   }

}
