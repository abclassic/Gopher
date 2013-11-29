//
// NetTuple.cs
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
