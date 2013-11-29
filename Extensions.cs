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
