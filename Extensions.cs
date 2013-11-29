//
// Extensions.cs
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
using System.Net;
using PcapDotNet.Packets;
using PcapDotNet.Packets.IpV4;
using PcapDotNet.Packets.Transport;
using PcapDotNet.Core;
using PcapDotNet.Core.Extensions;
using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace Gopher
{
   [Flags]
   public enum BetweenFlags
   {
      None,
      IncludeLower = 0x01,
      IncludeUpper = 0x02,
      IncludeBoth = IncludeLower | IncludeUpper
   }

   public static class Int32Extensions
   {
      public static Boolean Between(this Int32 value, Int32 lower, Int32 upper, BetweenFlags flags = BetweenFlags.IncludeBoth)
      {
         return (((flags & BetweenFlags.IncludeLower) == BetweenFlags.IncludeLower) ? value >= lower : value > lower) || (((flags & BetweenFlags.IncludeUpper) == BetweenFlags.IncludeUpper) ? value <= upper : value < upper);
      }
   }

   public static class PacketExtensions
   {
      public static IpV4Datagram GetIpV4Datagram(this Packet packet)
      {
         return (packet.DataLink.Kind == DataLinkKind.Ethernet) ? packet.Ethernet.IpV4 : (packet.DataLink.Kind == DataLinkKind.IpV4) ? packet.IpV4 : null;
      }

      public static TcpDatagram GetTcpDatagram(this Packet packet)
      {
         var ip = packet.GetIpV4Datagram();
         return (ip != null) ? ip.Tcp : null;
      }

      public static UInt32 GetSequenceNumber(this Packet packet)
      {
         var tcp = packet.GetTcpDatagram();
         return (tcp != null) ? tcp.SequenceNumber : 0;
      }

      public static UInt32 GetNextSequenceNumber(this Packet packet)
      {
         var tcp = packet.GetTcpDatagram();
         return (tcp != null) ? tcp.NextSequenceNumber : 0;
      }
   }

   public static class IPEndPointExtensions
   {
      public static Boolean Validate(this IPEndPoint endpoint)
      {
         return (endpoint != null && endpoint.Address != IPAddress.None && endpoint.Port.Between(0, 0x10000, BetweenFlags.None));
      }
   }

   public static class LivePacketDeviceExtensions
   {
      public static List<LivePacketDevice> FilterActiveDevices(this IReadOnlyCollection<LivePacketDevice> collection)
      {
         if (collection == null)
            return null;
         var activeDevices = new List<LivePacketDevice>();
         foreach (var lpd in collection) {
            var itf = lpd.GetNetworkInterface();
            if (itf.OperationalStatus == OperationalStatus.Up && itf.Supports(NetworkInterfaceComponent.IPv4) && itf.NetworkInterfaceType != NetworkInterfaceType.Loopback)
               activeDevices.Add(lpd);
         }
         return activeDevices;
      }
   }


   public static class StringUrlExtensions
   {
      /// <summary>
      /// UrlDecodes a string without requiring System.Web
      /// </summary>
      /// <param name="text">String to decode.</param>
      /// <returns>decoded string</returns>
      public static String UrlDecode(this String text)
      {
         return System.Uri.UnescapeDataString(text.Replace("+", " "));
      }
   }

   public static class StringCoreExtensions
   {
      public static String Truncate(this String text, Int32 length, String suffix = null)
      {
         if (text.Length <= length)
            return text;
         if (suffix.Length >= text.Length)
            return text;
         return (String.IsNullOrEmpty(suffix)) ? text.Substring(0, length) : text.Substring(0, length - suffix.Length) + suffix;
      }
   }

}
