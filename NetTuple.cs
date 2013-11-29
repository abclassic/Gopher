using System;
using System.Diagnostics;
using PcapDotNet.Packets;
using PcapDotNet.Packets.IpV4;
using PcapDotNet.Packets.Transport;

namespace Gopher
{
   public class NetTuple
   {
      public NetTuple(String sourceAddress, Int32 sourcePort, String destinationAddress, Int32 destinationPort)
      {
         SourceAddress = sourceAddress;
         SourcePort = SourcePort;
         DestinationAddress = destinationAddress;
         DestinationPort = destinationPort;
      }

      public NetTuple(Packet packet, bool reverseDirection = false)
      {
         if (packet == null)
            throw new ArgumentNullException();

         var ip = packet.GetIpV4Datagram();
         var tcp = ip != null ? ip.Tcp : null;

         Debug.Assert(ip != null);
         Debug.Assert(tcp != null);

         SourceAddress = reverseDirection ? ip.Destination.ToString() : ip.Source.ToString();
         SourcePort = reverseDirection ? tcp.DestinationPort : tcp.SourcePort;
         DestinationAddress = reverseDirection ? ip.Source.ToString() : ip.Destination.ToString();
         DestinationPort = reverseDirection ? tcp.SourcePort : tcp.DestinationPort;
      }

      public override Int32 GetHashCode()
      {
         Int64 hash = 0x11;
         hash = hash * 23 + SourceAddress.GetHashCode();
         hash = hash * 23 + DestinationAddress.GetHashCode();
         hash = hash * 23 + SourcePort.GetHashCode();
         hash = hash * 23 + DestinationPort.GetHashCode();
         return (Int32)(hash % Int32.MaxValue);
      }

      public override Boolean Equals(Object e)
      {
         var t = e as NetTuple;
         return ((Object)t == null) ? false : (SourceAddress == t.SourceAddress) && (SourcePort == t.SourcePort && (DestinationAddress == t.DestinationAddress) && (DestinationPort == t.DestinationPort));
      }

      public static Boolean operator ==(NetTuple a, NetTuple b)
      {
         if (Object.ReferenceEquals(a, b))
            return true;
         if ((Object)a == null || (Object)b == null)
            return false;
         return a.Equals(b);
      }

      public static Boolean operator !=(NetTuple a, NetTuple b)
      {
         return !(a == b);
      }

      public static implicit operator NetTuple(Packet value)
      {
         return new NetTuple(value);
      }

      //////////////////////////////////////////////////////////////////////////

      public String SourceAddress { get; private set; }
      public Int32 SourcePort { get; private set; }
      public String DestinationAddress { get; private set; }
      public Int32 DestinationPort { get; private set; }
   }
}
